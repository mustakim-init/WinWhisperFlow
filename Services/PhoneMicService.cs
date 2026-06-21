using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace WinWhisperFlow.Services;

public sealed class PhoneMicService : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _firewallRuleAdded;
    private bool _firewallRuleAttempted;
    private const string FirewallRuleName = "WinWhisperFlow Phone Mic (Local)";

    public event EventHandler<(string WavPath, TaskCompletionSource<string> Result)>? AudioReceived;
    public event EventHandler<string>? LogMessage;

    public int Port { get; private set; } = 8766;
    public bool IsRunning => _listener is not null;
    public string? LocalIp { get; private set; }

    public void Start(int port = 8766)
    {
        Stop();
        Port = port;

        // Localhost binding does not require firewall rules or admin UAC prompts
        _firewallRuleAttempted = true;

        _cts = new CancellationTokenSource();

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();

        LogMessage?.Invoke(this, $"Phone mic server started on ws://localhost:{Port}/ (for WinWhisperFlow transcription)");
        _ = ListenAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_listener is not null)
        {
            try { _listener.Stop(); } catch (ObjectDisposedException) { }
            (_listener as IDisposable)?.Dispose();
            _listener = null;
        }

        LogMessage?.Invoke(this, "Phone mic server stopped");
    }

    public string GetUrl() => $"ws://localhost:{Port}/";

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = HandleClientAsync(ctx);
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) { break; }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task HandleClientAsync(HttpListenerContext ctx)
    {
        if (!ctx.Request.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            return;
        }

        WebSocketContext wsCtx;
        try
        {
            wsCtx = await ctx.AcceptWebSocketAsync(null);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"WebSocket upgrade failed: {ex.Message}");
            ctx.Response.StatusCode = 500;
            ctx.Response.Close();
            return;
        }

        var ws = wsCtx.WebSocket;
        LogMessage?.Invoke(this, "PhoneMicVirtualCable connected to local transcription feed");

        var ringBuffer = new MemoryStream();
        var readBuffer = new byte[65536];
        long totalBytes = 0;

        const int sampleRate = 16000;
        const int chunkSamples = sampleRate * 3;
        const int chunkBytes = chunkSamples * 2;

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(readBuffer), _cts?.Token ?? CancellationToken.None);

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
                        var chunk = new byte[chunkBytes];
                        ringBuffer.Position = 0;
                        ringBuffer.Read(chunk, 0, chunkBytes);

                        var remaining = ringBuffer.Length - chunkBytes;
                        byte[] leftover = [];
                        if (remaining > 0)
                        {
                            leftover = new byte[remaining];
                            ringBuffer.Read(leftover, 0, (int)remaining);
                        }
                        ringBuffer.SetLength(0);
                        ringBuffer.Write(leftover, 0, (int)remaining);
                        ringBuffer.Position = ringBuffer.Length;

                        await FireTranscriptionEventAsync(chunk);
                    }
                }
            }
        }
        catch (WebSocketException ex)
        {
            LogMessage?.Invoke(this, $"WebSocket error: {ex.Message}");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"Error: {ex.Message}");
        }
        finally
        {
            LogMessage?.Invoke(this, $"PhoneMicVirtualCable disconnected. Total bytes received: {totalBytes}");
        }
    }

    private async Task FireTranscriptionEventAsync(byte[] pcmChunk)
    {
        string wavPath = Path.Combine(Path.GetTempPath(), $"winwhisper-phone-{Guid.NewGuid():N}.wav");

        try
        {
            WriteWavHeader(wavPath, pcmChunk);

            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            AudioReceived?.Invoke(this, (wavPath, tcs));

            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            LogMessage?.Invoke(this, "Transcription timed out for a 3-second chunk");
        }
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

    private void TryAddFirewallRule()
    {
        // Firewall rule for local-only port 8766 — less critical since it's localhost-accessible,
        // but added for completeness if the user wants to allow remote access.
        if (RuleExists()) { _firewallRuleAdded = false; return; }
        try
        {
            var psi = new ProcessStartInfo("netsh",
                $"advfirewall firewall add rule name=\"{FirewallRuleName}\" dir=in action=allow protocol=TCP localport={Port} profile=private,domain description=\"Allow WinWhisperFlow phone mic transcription feed\"")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            using var fwProc = System.Diagnostics.Process.Start(psi);
            if (fwProc is not null) fwProc.WaitForExit(5000);
            _firewallRuleAdded = true;
        }
        catch { }
    }

    private static bool RuleExists()
    {
        try
        {
            using var proc = new System.Diagnostics.Process
            {
                StartInfo = new ProcessStartInfo("netsh",
                    $"advfirewall firewall show rule name=\"{FirewallRuleName}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            proc.Start();
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return output.Contains(FirewallRuleName);
        }
        catch { return false; }
    }

    public void Dispose() => Stop();
}
