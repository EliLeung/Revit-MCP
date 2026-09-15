// Modified for Revit-MCP, 2026-09-15: multi-client connections, connection management, and/or product branding.
// Based on bimwright/rvt-mcp; original licensing and attribution retained.
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace RvtMcp.Plugin.Views
{
    public sealed class ConnectionManagerWindow : Window
    {
        private readonly Func<PipeTransportServer> _provider;
        private readonly DataGrid _grid;
        private readonly TextBlock _summary, _message;
        private readonly TextBox _note;
        private readonly DispatcherTimer _timer;
        private readonly Button _disconnect, _block, _unblock, _save;
        private bool _refreshing;
        public ConnectionManagerWindow(Func<PipeTransportServer> provider)
        {
            _provider = provider;
            Title = "Revit-MCP · 连接管理"; Width = 1080; Height = 570; MinWidth = 920; MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 251));
            var root = new Grid { Margin = new Thickness(24) };
            foreach (var h in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) root.RowDefinitions.Add(new RowDefinition { Height = h });
            Content = root;
            var header = new StackPanel();
            header.Children.Add(new TextBlock { Text = "客户端连接", FontSize = 24, FontWeight = FontWeights.SemiBold });
            _summary = new TextBlock { Margin = new Thickness(0, 8, 0, 14), Foreground = Brushes.DimGray };
            header.Children.Add(_summary); root.Children.Add(header);
            var hint = new TextBlock { Text = "这里显示 MCP 客户端进程，不是 Codex 聊天标题。可添加备注便于识别。", Margin = new Thickness(0, 0, 0, 14), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(hint, 1); root.Children.Add(hint);
            _grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single,
                HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 38, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                BorderBrush = Brushes.LightGray, Background = Brushes.White };
            AddColumn("连接", "Id", 65); AddColumn("备注", "Note", 140); AddColumn("客户端", "ProcessName", 130); AddColumn("PID", "ProcessId", 70);
            AddColumn("接入时间", "ConnectedAt", 90, "HH:mm:ss"); AddColumn("最近活动", "LastActivity", 90, "HH:mm:ss");
            AddColumn("最近请求", "Command", 200); AddColumn("状态", "Status", 115);
            _grid.SelectionChanged += (s, e) => { if (!_refreshing) _note.Text = (_grid.SelectedItem as ConnectionInfo)?.Note ?? ""; UpdateButtons(); };
            Grid.SetRow(_grid, 2); root.Children.Add(_grid);
            var bar = new WrapPanel { Margin = new Thickness(0, 16, 0, 10) };
            bar.Children.Add(new TextBlock { Text = "备注", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _note = new TextBox { Width = 175, MaxLength = 80, Padding = new Thickness(7), Margin = new Thickness(0, 0, 8, 0) }; bar.Children.Add(_note);
            _save = Button(bar, "保存备注", () => { var c = Selected; if (c != null) Registry?.Rename(c.Id, _note.Text); Refresh(); });
            _disconnect = Button(bar, "断开连接", () => Act(false));
            _block = Button(bar, "断开并阻止重连", () => Act(true));
            _unblock = Button(bar, "解除阻止", () => { if (Selected != null) Registry?.Unblock(Selected.Id); _message.Text = "已解除阻止，客户端可重新连接。"; Refresh(); });
            Button(bar, "刷新", Refresh);
            Grid.SetRow(bar, 3); root.Children.Add(bar);
            var footer = new StackPanel();
            _message = new TextBlock { Text = "选择一条连接进行管理。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }; footer.Children.Add(_message);
            footer.Children.Add(new TextBlock { Text = "处理中会等待请求返回后断开，不撤销模型操作。阻止按进程及启动时间识别；客户端进程重启或 Revit 重启后需重新设置。",
                FontSize = 12, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap });
            Grid.SetRow(footer, 4); root.Children.Add(footer);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; _timer.Tick += (s, e) => Refresh();
            Loaded += (s, e) => { Refresh(); _timer.Start(); }; Closed += (s, e) => _timer.Stop();
        }
        private ConnectionInfo Selected => _grid.SelectedItem as ConnectionInfo;
        private ConnectionRegistry Registry => _provider()?.Connections;
        private void AddColumn(string title, string property, double width, string format = null) => _grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property) { StringFormat = format }, Width = width });
        private static Button Button(Panel panel, string label, Action action)
        {
            var b = new Button { Content = label, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
            b.Click += (s, e) => action(); panel.Children.Add(b); return b;
        }
        private void Act(bool block)
        {
            if (Selected == null) return;
            _message.Text = Registry?.Disconnect(Selected.Id, block) ?? "MCP 服务未运行。";
            Refresh();
        }
        private void UpdateButtons()
        {
            if (_save == null) return;
            var c = Selected;
            _save.IsEnabled = c != null; _disconnect.IsEnabled = c?.Connected == true;
            _block.IsEnabled = c?.Connected == true && !c.Blocked; _unblock.IsEnabled = c?.Blocked == true;
        }
        private void Refresh()
        {
            var id = Selected?.Id;
            var transport = _provider();
            var rows = transport?.Connections.Snapshot() ?? new ConnectionInfo[0];
            _refreshing = true;
            _grid.ItemsSource = rows;
            _grid.SelectedItem = rows.FirstOrDefault(r => r.Id == id);
            _refreshing = false;
            if (Selected == null) _note.Text = "";
            _summary.Text = transport?.IsRunning == true
                ? $"已连接 {rows.Count(r => r.Connected)} / 16  ·  处理中 {rows.Count(r => r.Status == "处理中")}  ·  已阻止 {rows.Count(r => r.Blocked)}  ·  每秒自动刷新"
                : "MCP 服务未运行，请先开启 MCP。";
            UpdateButtons();
        }
    }
}
