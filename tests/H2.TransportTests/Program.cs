using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;

class Client : IDisposable
{
    readonly NamedPipeClientStream pipe;
    readonly StreamReader reader;
    readonly StreamWriter writer;
    public Client()
    {
        pipe = new NamedPipeClientStream(".", $"RvtMcp-{Environment.ProcessId}", PipeDirection.InOut, PipeOptions.Asynchronous);
        pipe.Connect(3000);
        reader = new StreamReader(pipe, Encoding.UTF8);
        writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
    }
    public async Task<JObject> Call(string id, string token = null, string command = "echo")
    {
        await writer.WriteLineAsync(new JObject { ["id"] = id, ["command"] = command, ["token"] = token ?? AuthToken.Current }.ToString(Newtonsoft.Json.Formatting.None));
        var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        return JObject.Parse(line ?? throw new Exception("Unexpected disconnect"));
    }
    public void Dispose() { pipe.Dispose(); }
}

class Program
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static async Task Eventually(Func<bool> predicate)
    {
        for (int i = 0; i < 100; i++) { if (predicate()) return; await Task.Delay(20); }
        throw new Exception("State did not settle");
    }
    static async Task Main()
    {
        var registry = new ConnectionRegistry();
        int closedA = 0, closedB = 0;
        var a = registry.Open(1, "codex-a", "1:start", () => closedA++);
        var b = registry.Open(2, "codex-b", "2:start", () => closedB++);
        registry.Rename(a, "平面任务");
        Check(registry.Snapshot().Single(r => r.Id == a).Note == "平面任务", "Rename failed");
        Check(registry.Begin(a, "read_view"), "Begin failed");
        registry.Disconnect(a, true);
        Check(closedA == 0 && closedB == 0, "Busy request closed prematurely");
        Check(!registry.Begin(a, "next"), "Closing connection accepted next request");
        Check(registry.Open(1, "same", "1:start", () => {}) == null, "Blocked identity reconnected");
        Check(registry.Open(1, "reused-pid", "1:new-start", () => {}) != null, "Reused PID blocked");
        registry.Complete(a);
        Check(closedA == 1 && closedB == 0, "Disconnect affected wrong client");
        registry.Closed(a);
        Check(registry.Snapshot().Single(r => r.Id == a).Blocked, "Block row disappeared");
        registry.Unblock(a);
        Check(registry.Open(1, "same", "1:start", () => {}) != null, "Unblock failed");
        registry.Disconnect(b, false);
        Check(closedB == 1, "Idle client not disconnected");
        Console.WriteLine("PASS registry rename, deferred disconnect, targeted block/unblock, PID reuse");
        AuthToken.RevitVersion = "transport-test-" + Environment.ProcessId;
        using var server = new PipeTransportServer();
        TaskCompletionSource<string> held = null;
        Action<string, TaskCompletionSource<string>> callback = (line, tcs) => {
            var obj = JObject.Parse(line);
            if ((string)obj["command"] == "hold") { Volatile.Write(ref held, tcs); return; }
            tcs.TrySetResult(new JObject { ["id"] = obj["id"], ["success"] = true }.ToString(Newtonsoft.Json.Formatting.None));
        };
        try
        {
            server.Start(callback);
            var token = AuthToken.Current;
            server.Start(callback);
            Check(token == AuthToken.Current, "Duplicate Start rotated token");
            Console.WriteLine("PASS duplicate Start is idempotent");
            var clients = Enumerable.Range(0, 16).Select(_ => new Client()).ToArray();
            try
            {
                await Task.WhenAll(clients.Select(async (c, i) => {
                    for (int n = 0; n < 3; n++) {
                        var id = $"client-{i}-{n}";
                        Check((string)(await c.Call(id))["id"] == id, "Response delivered to wrong client");
                    }
                }));
                Console.WriteLine("PASS 16 simultaneous clients, 48 correlated responses");
                Check(server.Connections.Snapshot().Count(r => r.Connected) == 16, "Registry count mismatch");
                Check(server.Connections.Snapshot().All(r => r.ProcessId == Environment.ProcessId), "Native client PID lookup failed");
                Console.WriteLine("PASS registry reports real named-pipe client PIDs and connection count");
                clients[0].Dispose();
                using var replacement = new Client();
                Check((bool)(await replacement.Call("replacement"))["success"], "Replacement failed");
                Check((bool)(await clients[1].Call("survivor"))["success"], "Surviving client failed");
                Check(server.IsClientConnected, "Connection status cleared prematurely");
                Console.WriteLine("PASS disconnected client replaced while other clients remain usable");
            }
            finally { foreach (var c in clients) c.Dispose(); }
            await Eventually(() => !server.IsClientConnected);
            using (var bad = new Client()) Check(!(bool)(await bad.Call("bad", "invalid"))["success"], "Invalid token accepted");
            using (var good = new Client()) Check((bool)(await good.Call("good"))["success"], "Auth rejection affected others");
            Console.WriteLine("PASS auth rejection isolated to offending client");
            await Eventually(() => server.Connections.Snapshot().Length == 0);
            using (var first = new Client())
            {
                await first.Call("manage-first");
                var firstId = server.Connections.Snapshot().Single().Id;
                using var second = new Client();
                await second.Call("manage-second");
                server.Connections.Disconnect(firstId, false);
                await Eventually(() => server.Connections.Snapshot().Length == 1);
                Check((bool)(await second.Call("still-alive"))["success"], "Selective disconnect broke survivor");
                var secondId = server.Connections.Snapshot().Single().Id;
                server.Connections.Disconnect(secondId, true);
                await Eventually(() => server.Connections.Snapshot().All(r => !r.Connected));
                using (var rejected = new Client()) {
                    bool denied = false;
                    try { await rejected.Call("blocked-reconnect"); }
                    catch (IOException) { denied = true; }
                    catch (Exception e) when (e.Message == "Unexpected disconnect") { denied = true; }
                    Check(denied, "Real pipe reconnect was not blocked");
                }
                server.Connections.Unblock(secondId);
                using var allowed = new Client();
                Check((bool)(await allowed.Call("unblocked"))["success"], "Real pipe unblock failed");
            }
            Console.WriteLine("PASS real pipe selective disconnect, blocked reconnect, and unblock");
            using (var idle = new Client())
            using (var busy = new Client())
            {
                var pending = busy.Call("held", command: "hold");
                await Eventually(() => Volatile.Read(ref held) != null);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                server.Stop();
                Check(watch.ElapsedMilliseconds < 3000, "Stop blocked");
                Check(held.Task.IsCompleted, "Pending request not completed on Stop");
                try { await pending; } catch (IOException) { } catch (Exception e) when (e.Message == "Unexpected disconnect") { }
                Check(!server.IsRunning && !server.IsClientConnected, "Stopped state incorrect");
            }
            Console.WriteLine("PASS Stop closes idle clients and completes pending requests");
            for (int n = 0; n < 20; n++)
            {
                server.Start(callback);
                using var c = new Client();
                Check((bool)(await c.Call("restart-" + n))["success"], "Restart failed");
                server.Stop();
            }
            Console.WriteLine("PASS 20 start/connect/stop cycles");
        }
        finally { server.Stop(); AuthToken.DeleteDiscoveryFile(); }
    }
}
