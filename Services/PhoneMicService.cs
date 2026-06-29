using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace WinWhisperFlow.Services;

public sealed class PhoneMicService : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private X509Certificate2? _cert;
    private readonly object _certLock = new();
    private int _phoneRecordingCount;
    private readonly SemaphoreSlim _connectionThrottle = new(20, 20);

    public event EventHandler<(string WavPath, TaskCompletionSource<string> Result)>? AudioReceived;
    public event EventHandler<string>? LogMessage;
    public event EventHandler<bool>? RecordingChanged;

    public int Port { get; private set; } = 8766;
    public bool IsRunning => _listener is not null;
    public bool IsPhoneRecording => _phoneRecordingCount > 0;
    public string? LocalIp { get; private set; }

    public void Start(int port = 8766)
    {
        Stop();
        Port = port;

        LocalIp = GetLanIp();
        var newCert = CreateSelfSignedCert();
        lock (_certLock) { _cert = newCert; }
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        string url = GetUrl();
        LogMessage?.Invoke(this, $"Phone mic server started on {url}");
        _ = AcceptClientsAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_listener is not null)
        {
            try { _listener.Stop(); } catch { }
            ((IDisposable)_listener).Dispose();
            _listener = null;
        }

        X509Certificate2? oldCert;
        lock (_certLock) { oldCert = _cert; _cert = null; }
        oldCert?.Dispose();

        LogMessage?.Invoke(this, "Phone mic server stopped");
    }

    public string GetUrl()
    {
        string ip = !string.IsNullOrEmpty(LocalIp) ? LocalIp : "127.0.0.1";
        return $"https://{ip}:{Port}/";
    }

    private static string? GetLanIp()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                ?.ToString();
        }
        catch { return null; }
    }

    private static X509Certificate2 CreateSelfSignedCert()
    {
        string certDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinWhisperFlow", "certs");
        Directory.CreateDirectory(certDir);
        string pfxPath = Path.Combine(certDir, "phone-mic.pfx");

        string pwdPath = pfxPath + ".pwd";
        string? password = null;

        if (File.Exists(pfxPath) && File.Exists(pwdPath))
        {
            try
            {
                byte[] encrypted = File.ReadAllBytes(pwdPath);
                byte[] decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
                    encrypted, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                password = System.Text.Encoding.UTF8.GetString(decrypted);
                return new X509Certificate2(pfxPath, password,
                    X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
            }
            catch (CryptographicException)
            {
                // Corrupted PFX or password — will generate a new one below
                try { File.Delete(pfxPath); } catch { }
                try { File.Delete(pwdPath); } catch { }
            }
            catch (IOException)
            {
                // File access issue — will generate a new one below
                try { File.Delete(pfxPath); } catch { }
                try { File.Delete(pwdPath); } catch { }
            }
        }

        var subject = new X500DistinguishedName("CN=WinWhisperFlow");
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        using var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(10));

        // Export to PFX with a random password, store password via DPAPI (machine-local encryption)
        password = Guid.NewGuid().ToString("N");
        byte[] pfxBytes = cert.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(pfxPath, pfxBytes);

        byte[] encryptedPwd = System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(password), null,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        File.WriteAllBytes(pwdPath, encryptedPwd);

        return new X509Certificate2(pfxPath, password,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                await _connectionThrottle.WaitAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (InvalidOperationException) { break; }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Accept error: {ex.Message}");
                try { await Task.Delay(1000, ct); } catch { break; }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                try
                {
                    byte[] peekBuf = new byte[1];
                    int peeked = await client.Client.ReceiveAsync(peekBuf.AsMemory(), SocketFlags.Peek, ct);
                    if (peeked == 0) return;

                    if (peekBuf[0] == 0x16) // TLS handshake — browser client
                    {
                        await HandleHttpsClientAsync(client, ct);
                    }
                    else // Plain HTTP — WebSocket forwarder client
                    {
                        await HandleWebSocketClientAsync(client, ct);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    LogMessage?.Invoke(this, $"Client error: {ex.Message}");
                }
            }
        }
        finally
        {
            _connectionThrottle.Release();
        }
    }

    private async Task HandleHttpsClientAsync(TcpClient client, CancellationToken ct)
    {
        X509Certificate2? cert;
        lock (_certLock) { cert = _cert; }
        if (cert is null) return;

        using var sslStream = new SslStream(client.GetStream(), false);
        try
        {
            var options = new SslServerAuthenticationOptions
            {
                ServerCertificate = cert,
                ClientCertificateRequired = false,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            };
            await sslStream.AuthenticateAsServerAsync(options, ct);
        }
        catch (AuthenticationException ex)
        {
            LogMessage?.Invoke(this, $"TLS handshake failed: {ex.Message}");
            return;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"TLS error: {ex.Message}");
            return;
        }

        byte[] buffer = new byte[65536];
        int bytesRead = await sslStream.ReadAsync(buffer, 0, buffer.Length, ct);
        if (bytesRead == 0) return;

        string request = Encoding.ASCII.GetString(buffer, 0, bytesRead);
        var lines = request.Split("\r\n");
        if (lines.Length == 0) return;

        var firstLine = lines[0].Split(' ');
        if (firstLine.Length < 2) return;

        string method = firstLine[0];
        string path = firstLine[1];

        if (method == "GET" && path == "/")
        {
            byte[] html = Encoding.UTF8.GetBytes(GetPhonePage());
            byte[] response = Encoding.UTF8.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {html.Length}\r\n" +
                "Connection: close\r\n\r\n");
            await sslStream.WriteAsync(response, 0, response.Length, ct);
            await sslStream.WriteAsync(html, 0, html.Length, ct);
        }
        else if (method == "POST" && path == "/recording")
        {
            if (Interlocked.Increment(ref _phoneRecordingCount) == 1)
                RecordingChanged?.Invoke(this, true);
            LogMessage?.Invoke(this, "Phone recording started");
            byte[] response = Encoding.UTF8.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Length: 0\r\n" +
                "Connection: close\r\n\r\n");
            await sslStream.WriteAsync(response, 0, response.Length, ct);
        }
        else if (method == "POST" && path == "/audio")
        {
            LogMessage?.Invoke(this, "Processing phone audio...");
            await HandleAudioPostAsync(sslStream, buffer, bytesRead, request, ct);
        }
        else
        {
            byte[] response = Encoding.UTF8.GetBytes(
                "HTTP/1.1 404 Not Found\r\n" +
                "Content-Length: 0\r\n" +
                "Connection: close\r\n\r\n");
            await sslStream.WriteAsync(response, 0, response.Length, ct);
        }
    }

    private async Task HandleAudioPostAsync(SslStream sslStream, byte[] buffer, int bytesRead, string request, CancellationToken ct)
    {
        int headerEnd = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEnd < 0) return;
        headerEnd += 4;

        int contentLength = 0;
        var lines = request.Split("\r\n");
        foreach (var line in lines)
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                string val = line["Content-Length:".Length..].Trim();
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
        try
        {
            using (var fs = File.Create(wavPath))
            {
                int bodyStart = headerEnd;
                int remaining = bytesRead - bodyStart;
                if (remaining > 0)
                    await fs.WriteAsync(buffer, bodyStart, remaining, ct);

                int totalRead = remaining;
                while (totalRead < contentLength)
                {
                    int chunk = await sslStream.ReadAsync(buffer, 0,
                        Math.Min(buffer.Length, contentLength - totalRead), ct);
                    if (chunk == 0) break;
                    await fs.WriteAsync(buffer, 0, chunk, ct);
                    totalRead += chunk;
                }
            }

            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            AudioReceived?.Invoke(this, (wavPath, tcs));

            string result;
            try
            {
                result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
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
            await sslStream.WriteAsync(response, 0, response.Length, ct);

            LogMessage?.Invoke(this, "Phone transcription complete");
        }
        finally
        {
            if (Interlocked.Decrement(ref _phoneRecordingCount) <= 0)
                RecordingChanged?.Invoke(this, false);
            try { File.Delete(wavPath); } catch { }
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

    private async Task HandleWebSocketClientAsync(TcpClient client, CancellationToken ct)
    {
        var stream = client.GetStream();

        string? httpRequest = await ReadHttpRequestAsync(stream, ct);
        if (string.IsNullOrEmpty(httpRequest)) return;

        var lines = httpRequest.Split("\r\n");
        if (lines.Length < 1) return;

        var firstLine = lines[0].Split(' ');
        if (firstLine.Length < 2 || firstLine[0] != "GET") return;

        string? wsKey = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            {
                wsKey = line["Sec-WebSocket-Key:".Length..].Trim();
                break;
            }
        }

        if (wsKey is null) return;

        string acceptKey = ComputeWebSocketAcceptKey(wsKey);

        byte[] upgradeResponse = Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n");
        await stream.WriteAsync(upgradeResponse, 0, upgradeResponse.Length, ct);

        using var ws = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(30));

        LogMessage?.Invoke(this, "PhoneMicVirtualCable connected to local transcription feed");

        var ringBuffer = new MemoryStream();
        byte[] readBuffer = new byte[65536];
        long totalBytes = 0;

        const int sampleRate = 16000;
        const int chunkSamples = sampleRate * 3;
        const int chunkBytes = chunkSamples * 2;

        byte[] chunk = new byte[chunkBytes];

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(readBuffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    ringBuffer.Write(readBuffer, 0, result.Count);
                    totalBytes += result.Count;

                    while (ringBuffer.Length >= chunkBytes)
                    {
                        ringBuffer.Position = 0;
                        ringBuffer.Read(chunk, 0, chunkBytes);

                        long remaining = ringBuffer.Length - chunkBytes;
                        if (remaining > 0)
                        {
                            byte[] buf = ringBuffer.GetBuffer();
                            Buffer.BlockCopy(buf, chunkBytes, buf, 0, (int)remaining);
                        }
                        ringBuffer.SetLength(remaining);
                        ringBuffer.Position = remaining;

                        await FireTranscriptionEventAsync(chunk, ct);
                    }
                }
            }
        }
        catch (WebSocketException ex)
        {
            LogMessage?.Invoke(this, $"WebSocket error: {ex.Message}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"Error: {ex.Message}");
        }
        finally
        {
            LogMessage?.Invoke(this, $"PhoneMicVirtualCable disconnected. Total bytes received: {totalBytes}");
        }
    }

    private static string ComputeWebSocketAcceptKey(string key)
    {
        const string magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        using var sha1 = SHA1.Create();
        byte[] hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(key + magic));
        return Convert.ToBase64String(hash);
    }

    private static async Task<string?> ReadHttpRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        byte[] buf = new byte[4096];

        while (true)
        {
            int read = await stream.ReadAsync(buf, 0, buf.Length, ct);
            if (read == 0) return null;
            for (int i = 0; i < read; i++)
            {
                sb.Append((char)buf[i]);
                if (sb.Length >= 4 &&
                    sb[^4] == '\r' && sb[^3] == '\n' &&
                    sb[^2] == '\r' && sb[^1] == '\n')
                    return sb.ToString();
            }
        }
    }

    private async Task FireTranscriptionEventAsync(byte[] pcmChunk, CancellationToken ct = default)
    {
        string wavPath = Path.Combine(Path.GetTempPath(), $"winwhisper-phone-{Guid.NewGuid():N}.wav");

        try
        {
            WriteWavHeader(wavPath, pcmChunk);

            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            AudioReceived?.Invoke(this, (wavPath, tcs));

            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
        }
        catch (TimeoutException)
        {
            LogMessage?.Invoke(this, "Transcription timed out for a 3-second chunk");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"Transcription error: {ex.Message}");
        }
        finally
        {
            try { File.Delete(wavPath); } catch { }
        }
    }

    private static void WriteWavHeader(string wavPath, byte[] pcmData)
    {
        int sampleRate = 16000;
        short channels = 1;
        short bitsPerSample = 16;
        int dataSize = pcmData.Length;

        using var fs = new FileStream(wavPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * bitsPerSample / 8);
        bw.Write((short)(channels * bitsPerSample / 8));
        bw.Write(bitsPerSample);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        bw.Write(pcmData);
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
        if (!gum) reject(new Error('getUserMedia not supported'));
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
        fetch('/recording', { method: 'POST' });
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

    public void Dispose()
    {
        Stop();
        _connectionThrottle.Dispose();
    }
}
