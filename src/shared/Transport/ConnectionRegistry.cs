// Modified for Revit-MCP, 2026-09-15: multi-client connections, connection management, and/or product branding.
// Based on bimwright/rvt-mcp; original licensing and attribution retained.
using System;
using System.Collections.Generic;
using System.Linq;

namespace RvtMcp.Plugin
{
    public sealed class ConnectionInfo
    {
        public string Id { get; set; }
        public string Note { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public DateTime ConnectedAt { get; set; }
        public DateTime? LastActivity { get; set; }
        public string Command { get; set; }
        public string Status { get; set; }
        public bool Connected { get; set; }
        public bool Blocked { get; set; }
    }

    public sealed class ConnectionRegistry
    {
        private sealed class Entry
        {
            public ConnectionInfo Info;
            public string Identity;
            public Action Close;
            public bool Busy, Closing;
        }
        private readonly object _gate = new object();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private readonly HashSet<string> _blocked = new HashSet<string>();
        private int _sequence;

        public string Open(int pid, string processName, string identity, Action close)
        {
            lock (_gate)
            {
                if (identity != null && _blocked.Contains(identity)) return null;
                var id = "C" + (++_sequence).ToString("D3");
                _entries.Add(id, new Entry { Identity = identity, Close = close, Info = new ConnectionInfo {
                    Id = id, ProcessId = pid, ProcessName = processName, ConnectedAt = DateTime.Now,
                    Connected = true, Note = "", Command = "—" } });
                return id;
            }
        }
        public ConnectionInfo[] Snapshot()
        {
            lock (_gate) return _entries.Values.Select(e => new ConnectionInfo {
                Id = e.Info.Id, Note = e.Info.Note, ProcessId = e.Info.ProcessId, ProcessName = e.Info.ProcessName,
                ConnectedAt = e.Info.ConnectedAt, LastActivity = e.Info.LastActivity, Command = e.Info.Command,
                Connected = e.Info.Connected, Blocked = e.Identity != null && _blocked.Contains(e.Identity),
                Status = !e.Info.Connected ? "已阻止" : e.Closing ? "完成后断开" : e.Busy ? "处理中" : "空闲"
            }).OrderBy(e => e.Id).ToArray();
        }
        public void Rename(string id, string note)
        {
            lock (_gate) if (_entries.TryGetValue(id, out var e)) e.Info.Note = (note ?? "").Trim().Substring(0, Math.Min((note ?? "").Trim().Length, 80));
        }
        public bool Begin(string id, string command)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(id, out var e) || e.Closing || !e.Info.Connected) return false;
                e.Busy = true; e.Info.Command = command; e.Info.LastActivity = DateTime.Now;
                return true;
            }
        }
        public void Complete(string id)
        {
            Action close = null;
            lock (_gate) if (_entries.TryGetValue(id, out var e)) { e.Busy = false; if (e.Closing) close = e.Close; }
            close?.Invoke();
        }
        public void Closed(string id)
        {
            if (id == null) return;
            lock (_gate) if (_entries.TryGetValue(id, out var e)) {
                e.Info.Connected = false; e.Close = null; e.Busy = false;
                if (e.Identity == null || !_blocked.Contains(e.Identity)) _entries.Remove(id);
            }
        }
        public string Disconnect(string id, bool block)
        {
            var actions = new List<Action>();
            bool deferred = false;
            lock (_gate)
            {
                if (!_entries.TryGetValue(id, out var selected)) return "连接已退出。";
                if (block && selected.Identity == null) return "无法识别进程启动时间，不能可靠阻止重连；可以使用断开连接。";
                if (block) _blocked.Add(selected.Identity);
                foreach (var e in _entries.Values.Where(e => e == selected || (block && e.Identity == selected.Identity))) {
                    e.Closing = true;
                    if (e.Busy) deferred = true;
                    else if (e.Close != null) actions.Add(e.Close);
                }
            }
            foreach (var action in actions) action();
            return deferred ? "已安排在当前请求返回后断开。" : block ? "已断开并临时阻止该客户端进程。" : "已断开；客户端可能自动重连。";
        }
        public void Unblock(string id)
        {
            lock (_gate) if (_entries.TryGetValue(id, out var selected) && selected.Identity != null) {
                _blocked.Remove(selected.Identity);
                foreach (var key in _entries.Where(p => p.Value.Identity == selected.Identity && !p.Value.Info.Connected).Select(p => p.Key).ToArray()) _entries.Remove(key);
            }
        }
    }
}
