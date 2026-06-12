using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace WinWhisperFlow.Services;

public sealed class PhoneMicService : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private X509Certificate2? _cert;
    public event EventHandler<(string WavPath, TaskCompletionSource<string> Result)>? AudioReceived;
    public int Port { get; private set; } = 8765;
    public bool IsRunning => _listener is not null;
    public string? LocalIp { get; private set; }

    public void Start(int port = 8765)
    {
        Stop();
        Port = port;
        LocalIp = GetLocalIpAddress();
        // Self-signed cert — browser will show a security warning. This is expected
        // for local-network use; the connection is encrypted but not CA-verified.
        _cert = CreateSelfSignedCert();
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _ = ListenAsync(_cts.Token);
    }

    public void Stop()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
        if (_listener is not null)
        {
            try { _listener.Stop(); } catch { }
            ((IDisposable)_listener).Dispose();
            _listener = null;
        }
        _cert?.Dispose();
        _cert = null;
    }

    public string GetUrl() => $"https://{LocalIp}:{Port}/";

    private static X509Certificate2 CreateSelfSignedCert()
    {
        var subject = new X500DistinguishedName("CN=WinWhisperFlow");
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        return req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(10));
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (InvalidOperationException) { break; }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (var sslStream = new SslStream(client.GetStream(), false))
        {
            if (_cert is null) return;
            try
            {
                await sslStream.AuthenticateAsServerAsync(_cert, clientCertificateRequired: false, checkCertificateRevocation: false);
            }
            catch
            {
                return;
            }

            byte[] buffer = new byte[65536];
            int bytesRead = await sslStream.ReadAsync(buffer, 0, buffer.Length);

            string request = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            var lines = request.Split("\r\n");

            if (lines.Length == 0) return;

            var firstLine = lines[0].Split(' ');
            if (firstLine.Length < 2) return;

            string method = firstLine[0];
            string path = firstLine[1];

            if (method == "POST" && path == "/audio")
            {
                int headerEnd = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd < 0) return;
                headerEnd += 4;

                int contentLength = 0;
                foreach (var line in lines)
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = line["Content-Length: ".Length..].Trim();
                        if (!int.TryParse(val, out contentLength) || contentLength < 0 || contentLength > 100_000_000)
                        {
                            await WriteErrorResponseAsync(sslStream, "Invalid Content-Length");
                            return;
                        }
                        break;
                    }
                }

                if (contentLength <= 0)
                {
                    await WriteErrorResponseAsync(sslStream, "Missing or invalid Content-Length");
                    return;
                }

                string wavPath = Path.Combine(Path.GetTempPath(), $"winwhisper-phone-{Guid.NewGuid():N}.wav");
                using (var fs = File.Create(wavPath))
                {
                    int bodyStart = headerEnd;
                    int remaining = bytesRead - bodyStart;
                    if (remaining > 0)
                        await fs.WriteAsync(buffer, bodyStart, remaining);

                    int totalRead = remaining;
                    while (totalRead < contentLength)
                    {
                        int chunk = await sslStream.ReadAsync(buffer, 0, Math.Min(buffer.Length, contentLength - totalRead));
                        if (chunk == 0) break;
                        await fs.WriteAsync(buffer, 0, chunk);
                        totalRead += chunk;
                    }
                }

                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                AudioReceived?.Invoke(this, (wavPath, tcs));

                string result;
                try
                {
                    result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(60));
                }
                catch (TimeoutException)
                {
                    result = "Transcription timed out";
                }
                catch (Exception ex)
                {
                    result = $"Transcription error: {ex.Message}";
                }

                byte[] response = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/plain; charset=utf-8\r\n" +
                    $"Content-Length: {Encoding.UTF8.GetByteCount(result)}\r\n" +
                    "Connection: close\r\n\r\n" + result);
                await sslStream.WriteAsync(response, 0, response.Length);

                try { File.Delete(wavPath); } catch { }
            }
            else
            {
                byte[] html = Encoding.UTF8.GetBytes(GetPhonePage());
                byte[] response = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/html; charset=utf-8\r\n" +
                    $"Content-Length: {html.Length}\r\n" +
                    "Connection: close\r\n\r\n");
                await sslStream.WriteAsync(response, 0, response.Length);
                await sslStream.WriteAsync(html, 0, html.Length);
            }
        }
    }

    private static async Task WriteErrorResponseAsync(SslStream stream, string message)
    {
        byte[] body = Encoding.UTF8.GetBytes(message);
        byte[] response = Encoding.UTF8.GetBytes(
            "HTTP/1.1 400 Bad Request\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(response, 0, response.Length);
        await stream.WriteAsync(body, 0, body.Length);
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
                if (ip.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ip))
                    return ip.ToString();
        }
        catch { }
        return "127.0.0.1";
    }

    private static string GetPhonePage()
    {
        return @"<!DOCTYPE html>
<html><head><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1,user-scalable=no'>
<title>WinWhisper Flow</title>
<style>
*{box-sizing:border-box;margin:0;padding:0}
body{font-family:-apple-system,BlinkMacSystemFont,sans-serif;background:#111;color:#eee;min-height:100vh;display:flex;flex-direction:column;align-items:center;padding:20px}
h1{font-size:20px;margin:10px 0;color:#888}
.btn{width:200px;height:200px;border-radius:50%;border:none;font-size:18px;cursor:pointer;display:flex;flex-direction:column;align-items:center;justify-content:center;transition:all .2s;margin:30px 0}
.btn.idle{background:#2a2a2a;color:#eee;border:3px solid #444}
.btn.idle .icon{font-size:60px}
.btn.idle:active{transform:scale(.95)}
.btn.recording{background:#8b0000;color:#fff;border:3px solid #ff4444;animation:pulse 1s infinite}
.btn.recording .icon{font-size:40px}
@keyframes pulse{0%,100%{box-shadow:0 0 0 0 rgba(255,68,68,.4)}50%{box-shadow:0 0 0 30px rgba(255,68,68,0)}}
.btn .label{font-size:14px;margin-top:8px}
#status{font-size:14px;color:#aaa;margin:10px 0;min-height:20px}
#result{width:100%;max-width:500px;margin-top:10px;padding:16px;background:#1a1a1a;border-radius:12px;font-size:16px;line-height:1.5;min-height:60px;white-space:pre-wrap;word-wrap:break-word;border:1px solid #333;color:#eee}
#result.empty{color:#555}
#error{color:#ff6b6b;font-size:14px;margin:10px 0;min-height:20px}
</style></head><body>
<h1>WinWhisper Flow</h1>
<div id=status>Tap to record</div>
<button class='btn idle' id=btn>
<span class=icon>🎤</span>
<span class=label>Tap to Record</span>
</button>
<div id=error></div>
<div id=result class=empty>Transcription will appear here</div>
<script>
let stream, ctx, src, proc, pcmData = [], recording = false;
const btn = document.getElementById('btn'), statusEl = document.getElementById('status');
const resultEl = document.getElementById('result'), errorEl = document.getElementById('error');

function getUserMedia(constraints) {
    if (navigator.mediaDevices?.getUserMedia) return navigator.mediaDevices.getUserMedia(constraints);
    return new Promise((resolve, reject) => {
        const gum = navigator.getUserMedia || navigator.webkitGetUserMedia || navigator.mozGetUserMedia || navigator.msGetUserMedia;
        if (!gum) reject(new Error('getUserMedia not supported by this browser'));
        gum.call(navigator, constraints, resolve, reject);
    });
}

btn.onclick = async () => {
    if (recording) { await stopRecording(); return; }
    try {
        errorEl.textContent = '';
        stream = await getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, sampleRate: 16000 } });
        ctx = new AudioContext();
        src = ctx.createMediaStreamSource(stream);
        pcmData = [];
        proc = ctx.createScriptProcessor(4096, 1, 1);
        proc.onaudioprocess = e => { pcmData.push(new Float32Array(e.inputBuffer.getChannelData(0))); };
        src.connect(proc);
        proc.connect(ctx.destination);
        recording = true;
        btn.className = 'btn recording';
        btn.innerHTML = '<span class=icon>⏹</span><span class=label>Stop</span>';
        statusEl.textContent = 'Recording... tap Stop when done';
        resultEl.textContent = '';
        resultEl.className = '';
    } catch(e) {
        errorEl.textContent = 'Mic error: ' + e.message;
    }
};

async function stopRecording() {
    recording = false;
    btn.className = 'btn idle';
    btn.innerHTML = '<span class=icon>⏳</span><span class=label>Sending...</span>';
    statusEl.textContent = 'Processing audio...';
    try {
        src?.disconnect();
        proc?.disconnect();
        stream?.getTracks().forEach(t => t.stop());
        ctx?.close();
        const totalLen = pcmData.reduce((a, b) => a + b.length, 0);
        const all = new Float32Array(totalLen);
        let off = 0;
        for (const c of pcmData) { all.set(c, off); off += c.length; }
        const sr = ctx?.sampleRate || 48000;
        const buf = new ArrayBuffer(44 + all.length * 2), v = new DataView(buf);
        v.setUint32(0, 0x46464952, true); v.setUint32(4, 36 + all.length * 2, true);
        v.setUint32(8, 0x45564157, true); v.setUint32(12, 0x20746D66, true);
        v.setUint32(16, 16, true); v.setUint16(20, 1, true); v.setUint16(22, 1, true);
        v.setUint32(24, sr, true); v.setUint32(28, sr * 2, true);
        v.setUint16(32, 2, true); v.setUint16(34, 16, true);
        v.setUint32(36, 0x61746164, true); v.setUint32(40, all.length * 2, true);
        for (let i = 0; i < all.length; i++) {
            let s = Math.max(-32768, Math.min(32767, Math.floor(all[i] * 32767)));
            v.setInt16(44 + i * 2, s, true);
        }
        statusEl.textContent = 'Uploading...';
        const resp = await fetch('audio', { method: 'POST', body: new Blob([buf], { type: 'audio/wav' }) });
        if (!resp.ok) { resultEl.textContent = 'Server error'; statusEl.textContent = 'Error'; btn.innerHTML = '<span class=icon>🎤</span><span class=label>Tap to Record</span>'; return; }
        const text = await resp.text();
        resultEl.textContent = text || '(empty)';
        statusEl.textContent = text ? 'Done' : 'No speech detected';
        btn.innerHTML = '<span class=icon>🎤</span><span class=label>Tap to Record</span>';
    } catch(e) {
        errorEl.textContent = 'Error: ' + e.message;
        btn.className = 'btn idle';
        btn.innerHTML = '<span class=icon>🎤</span><span class=label>Tap to Record</span>';
        statusEl.textContent = 'Error';
    }
}
</script></body></html>";
    }

    public void Dispose() => Stop();
}
