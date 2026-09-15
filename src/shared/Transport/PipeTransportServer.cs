// Modified for H2-Revit, 2026-09-15: multi-client connections, connection management, and/or product branding.
// Based on bimwright/rvt-mcp; original licensing and attribution retained.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RvtMcp.Plugin
{
    public class PipeTransportServer : ITransportServer
    {
        // Each start owns its cancellation and connections; old workers cannot
        // rejoin a newly started listener. Revit commands still use its UI queue.
        private sealed class RunState
        {
            public readonly CancellationTokenSource Stop = new CancellationTokenSource();
            public readonly SemaphoreSlim Slots = new SemaphoreSlim(16, 16);
            public readonly System.Collections.Concurrent.ConcurrentDictionary<NamedPipeServerStream, byte> Pipes =
                new System.Collections.Concurrent.ConcurrentDictionary<NamedPipeServerStream, byte>();
            public Action<string, TaskCompletionSource<string>> OnRequest;
            public Thread Listener;
            public int ClientCount;
        }

        public ConnectionRegistry Connections { get; } = new ConnectionRegistry();
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);

        private readonly object _lifecycleLock = new object();
        private volatile RunState _run;
        private string _pipeName;
        private long _lastCommandTicks;

        public bool IsRunning => _run != null;
        public bool IsClientConnected
        {
            get { var run = _run; return run != null && Volatile.Read(ref run.ClientCount) > 0; }
        }
        public DateTime? LastCommandTime
        {
            get { var ticks = Interlocked.Read(ref _lastCommandTicks); return ticks == 0 ? (DateTime?)null : new DateTime(ticks); }
        }
        public string ConnectionInfo => $"Pipe:{_pipeName}";

        public void Start(Action<string, TaskCompletionSource<string>> onRequest)
        {
            if (onRequest == null) throw new ArgumentNullException(nameof(onRequest));
            lock (_lifecycleLock)
            {
                if (_run != null) return; // Do not rotate auth or start duplicate listeners.
                _pipeName = $"RvtMcp-{Process.GetCurrentProcess().Id}";
                AuthToken.GenerateAndPersistPipe(_pipeName);
                var run = new RunState { OnRequest = onRequest };
                run.Listener = new Thread(() => ListenLoop(run))
                    { IsBackground = true, Name = "RvtMcp.PipeTransportServer" };
                _run = run;
                run.Listener.Start();
                Log($"Listening on pipe {_pipeName} (auth: enabled; max clients: 16)");
            }
        }

        public void Stop()
        {
            lock (_lifecycleLock)
            {
                var run = _run;
                if (run == null) return;
                run.Stop.Cancel();
                foreach (var pipe in run.Pipes.Keys)
                    try { pipe.Dispose(); } catch { }
                // Async WaitForConnection observes cancellation, including a stop
                // between pipe creation and registration in Pipes.
                run.Listener.Join();
                _run = null;
                AuthToken.DeleteDiscoveryFile();
                Log("Stopped");
            }
        }

        public void Dispose() { Stop(); }

        private void ListenLoop(RunState run)
        {
            while (!run.Stop.IsCancellationRequested)
            {
                NamedPipeServerStream pipe = null;
                bool ownsSlot = false;
                try
                {
                    run.Slots.Wait(run.Stop.Token);
                    ownsSlot = true;
                    pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    run.Pipes.TryAdd(pipe, 0);
                    pipe.WaitForConnectionAsync(run.Stop.Token).GetAwaiter().GetResult();
                    run.Stop.Token.ThrowIfCancellationRequested();
                    var clientPipe = pipe;
                    // Dedicated background workers avoid thread-pool starvation
                    // when several clients are idle in a blocking pipe read.
                    var worker = new Thread(() => ServeClient(clientPipe, run))
                        { IsBackground = true, Name = "RvtMcp.PipeClient" };
                    worker.Start();
                    pipe = null;
                    ownsSlot = false; // Worker now owns pipe and slot.
                }
                catch (Exception) when (run.Stop.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Log($"Listen error: {ex.Message}");
                    run.Stop.Token.WaitHandle.WaitOne(250);
                }
                finally
                {
                    if (pipe != null)
                    {
                        run.Pipes.TryRemove(pipe, out _);
                        try { pipe.Dispose(); } catch { }
                    }
                    if (ownsSlot) run.Slots.Release();
                }
            }
        }

        private void ServeClient(NamedPipeServerStream pipe, RunState run)
        {
            string connectionId = null;
            try
            {
                int pid = 0;
                string name = "未知客户端", identity = null;
                try {
                    if (GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var nativePid)) {
                        pid = checked((int)nativePid);
                        using (var process = Process.GetProcessById(pid)) {
                            name = process.ProcessName;
                            identity = pid + ":" + process.StartTime.ToUniversalTime().Ticks;
                        }
                    }
                } catch { }
                connectionId = Connections.Open(pid, name, identity, () => { try { pipe.Dispose(); } catch { } });
                if (connectionId == null) return;
                Log("Client connected: " + connectionId);
                HandleClient(pipe, run, connectionId);
            }
            catch (Exception ex)
            {
                if (!run.Stop.IsCancellationRequested) Log($"Client error: {ex.Message}");
            }
            finally
            {
                Connections.Closed(connectionId);
                run.Pipes.TryRemove(pipe, out _);
                try { pipe.Dispose(); } catch { }
                run.Slots.Release();
                Log("Client disconnected");
            }
        }

        private void HandleClient(NamedPipeServerStream pipe, RunState run, string connectionId)
        {
            Interlocked.Increment(ref run.ClientCount);
            try
            {
                var reader = new StreamReader(pipe, Encoding.UTF8);
                var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };

                var requestTimestamps = new System.Collections.Generic.Queue<DateTime>();
                const int RateLimitMax = 20;
                var RateLimitWindow = TimeSpan.FromSeconds(10);

                while (!run.Stop.IsCancellationRequested && pipe.IsConnected)
                {
                    string line;
                    try
                    {
                        line = ReadLineBounded(reader, MaxLineBytes, out bool overflow);
                        if (overflow)
                        {
                            Log("Dropped oversized request (>1 MiB)");
                            try
                            {
                                writer.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new
                                {
                                    success = false,
                                    error = "Request exceeded 1 MiB size limit."
                                }));
                            }
                            catch { }
                            break;
                        }
                        if (line == null) break; // Client disconnected
                    }
                    catch (IOException)
                    {
                        Log("Client read error or broken pipe");
                        break;
                    }
                    catch
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Parse request
                    Newtonsoft.Json.Linq.JObject request;
                    try
                    {
                        request = Newtonsoft.Json.Linq.JObject.Parse(line);
                    }
                    catch
                    {
                        continue;
                    }

                    string token = request.Value<string>("token");
                    if (!AuthToken.Verify(token))
                    {
                        var denied = Newtonsoft.Json.JsonConvert.SerializeObject(new
                        {
                            id = request.Value<string>("id"),
                            success = false,
                            error = "Unauthorized: invalid or missing token."
                        });
                        try { writer.WriteLine(denied); } catch { }
                        Log("Auth rejected");
                        break; // drop the connection on auth failure
                    }

                    string id = request.Value<string>("id");
                    string command = request.Value<string>("command");
                    string paramsJson = request["params"]?.ToString() ?? "{}";

                    // Create TCS and invoke callback
                    var tcs = new TaskCompletionSource<string>();

                    var now = DateTime.UtcNow;
                    while (requestTimestamps.Count > 0 && (now - requestTimestamps.Peek()) > RateLimitWindow)
                        requestTimestamps.Dequeue();
                    if (requestTimestamps.Count >= RateLimitMax)
                    {
                        Log("Rate limit exceeded, dropping connection");
                        try
                        {
                            writer.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new
                            {
                                id,
                                success = false,
                                error = "Rate limit: 20 requests / 10 seconds per connection."
                            }));
                        }
                        catch { }
                        break;
                    }
                    requestTimestamps.Enqueue(now);

                    if (!Connections.Begin(connectionId, command)) break;
                    Interlocked.Exchange(ref _lastCommandTicks, DateTime.Now.Ticks);
                    string response;
                    using (run.Stop.Token.Register(() => tcs.TrySetResult(
                        Newtonsoft.Json.JsonConvert.SerializeObject(new { id, success = false, error = "Transport stopped." }))))
                    {
                        if (run.Stop.IsCancellationRequested) break;
                        run.OnRequest(line, tcs);
                        response = RequestWait.WaitOrTimeout(tcs, id);
                    }

                    try
                    {
                        writer.WriteLine(response);
                    }
                    catch
                    {
                        break;
                    }
                    finally { Connections.Complete(connectionId); }
                }
            }
            finally
            {
                Interlocked.Decrement(ref run.ClientCount);
            }
        }

        private const int MaxLineBytes = 1024 * 1024; // 1 MiB

        private static string ReadLineBounded(StreamReader reader, int maxBytes, out bool overflow)
        {
            overflow = false;
            var sb = new System.Text.StringBuilder();
            int count = 0;
            while (true)
            {
                int ch = reader.Read();
                if (ch == -1) return sb.Length == 0 ? null : sb.ToString();
                if (ch == '\n') return sb.ToString();
                if (ch == '\r') continue;
                count++;
                if (count > maxBytes) { overflow = true; return null; }
                sb.Append((char)ch);
            }
        }

        private void Log(string message)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RvtMcp");
                Directory.CreateDirectory(dir);
                var logFile = Path.Combine(dir, "revit-mcp.log");
                File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] [PipeTransport] {SecretMasker.Mask(message)}\n");
            }
            catch { }
        }
    }
}
