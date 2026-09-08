using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.RegularExpressions;

namespace XiangqiOnline.ServerAdmin;

internal sealed partial class ServerDashboard : Form
{
    private static readonly Color Ink = Color.FromArgb(39, 25, 27);
    private static readonly Color Wine = Color.FromArgb(143, 25, 38);
    private static readonly Color DeepWine = Color.FromArgb(68, 10, 25);
    private static readonly Color Gold = Color.FromArgb(236, 177, 65);
    private static readonly Color Paper = Color.FromArgb(250, 244, 233);
    private static readonly Color Muted = Color.FromArgb(116, 91, 83);
    private static readonly Regex LogPattern = new(@"^\[(?<time>[^]]+)\]\s+\[(?<category>[^]]+)\]\s+(?<message>.*)$", RegexOptions.Compiled);

    private readonly string _serverPath;
    private readonly bool _monitorOnly;
    private readonly string? _logDirectory;
    private readonly Label _stateValue = new();
    private readonly Label _clientValue = new();
    private readonly Label _roomValue = new();
    private readonly Label _uptimeValue = new();
    private readonly Label _footer = new();
    private readonly RichTextBox _log = new();
    private readonly DataGridView _players = CreateGrid();
    private readonly DataGridView _rooms = CreateGrid();
    private readonly System.Windows.Forms.Timer _uptimeTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _logMonitorTimer = new() { Interval = 1500 };
    private readonly HashSet<string> _connections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DataGridViewRow> _playerRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DataGridViewRow> _roomRows = new(StringComparer.Ordinal);
    private Process? _server;
    private DateTime _startedAt;
    private bool _closing;
    private string? _monitoredLogPath;
    private int _monitoredLineCount;

    public ServerDashboard(string[] args)
    {
        _serverPath = ResolveServerPath(args);
        _monitorOnly = args.Any(value => value.Equals("--monitor", StringComparison.OrdinalIgnoreCase));
        _logDirectory = ResolveArgument(args, "--log-dir");
        Text = "UDM18 • Trung tâm quản trị máy chủ";
        Icon = SystemIcons.Shield;
        MinimumSize = new Size(980, 650);
        Size = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Paper;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildInterface();
        Shown += async (_, _) =>
        {
            if (_monitorOnly) StartLogMonitor();
            else await StartServerAsync();
        };
        FormClosing += OnClosing;
        _uptimeTimer.Tick += (_, _) => UpdateUptime();
        _logMonitorTimer.Tick += (_, _) => RefreshMonitoredLog();
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(22, 18, 22, 12), BackColor = Paper };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(root);

        var header = new GradientPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 22,
            Margin = new Padding(0, 0, 0, 8),
            StartColor = DeepWine,
            EndColor = Color.FromArgb(176, 35, 40)
        };
        var mark = new RoundedPanel { BackColor = Color.FromArgb(246, 194, 83), CornerRadius = 18, Size = new Size(60, 60), Location = new Point(18, 8) };
        mark.Controls.Add(new Label { Text = "帥", ForeColor = DeepWine, Font = new Font("Microsoft YaHei", 24F, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent });
        header.Controls.Add(mark);
        header.Controls.Add(new Label { Text = "LẬP TRÌNH MẠNG NHÓM 6", ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Segoe UI Semibold", 21F), AutoSize = true, Location = new Point(94, 10) });
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 310, Padding = new Padding(0, 14, 14, 0), BackColor = Color.Transparent, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var stop = MakeButton("■  Dừng server", Color.FromArgb(221, 66, 65), Color.White, 128);
        var restart = MakeButton("↻  Khởi động lại", Color.FromArgb(249, 210, 119), DeepWine, 142);
        if (_monitorOnly)
        {
            stop.Text = "●  Server chạy nền";
            stop.Enabled = false;
            restart.Text = "↻  Làm mới log";
            restart.Click += (_, _) => ReloadMonitoredLog();
        }
        else
        {
            stop.Click += async (_, _) => await StopServerAsync();
            restart.Click += async (_, _) => { await StopServerAsync(); await StartServerAsync(); };
        }
        actions.Controls.Add(stop); actions.Controls.Add(restart); header.Controls.Add(actions);
        root.Controls.Add(header, 0, 0);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(0, 5, 0, 13), BackColor = Color.Transparent };
        for (var i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        cards.Controls.Add(CreateCard("TRẠNG THÁI", "●", _stateValue, "ĐANG KHỞI ĐỘNG", Color.FromArgb(28, 153, 105), Color.FromArgb(224, 247, 237)), 0, 0);
        cards.Controls.Add(CreateCard("KẾT NỐI", "♟", _clientValue, "0", Color.FromArgb(194, 44, 61), Color.FromArgb(253, 229, 232)), 1, 0);
        cards.Controls.Add(CreateCard("PHÒNG ĐẤU", "棋", _roomValue, "0", Color.FromArgb(206, 139, 26), Color.FromArgb(255, 243, 207)), 2, 0);
        cards.Controls.Add(CreateCard("THỜI GIAN CHẠY", "◷", _uptimeValue, "00:00:00", Color.FromArgb(77, 63, 161), Color.FromArgb(235, 232, 255)), 3, 0);
        root.Controls.Add(cards, 0, 1);

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 10F), DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(185, 42) };
        tabs.DrawItem += DrawTab;
        var playerTab = new TabPage("♟   Người chơi") { BackColor = Color.FromArgb(255, 252, 247), Padding = new Padding(12) };
        var roomTab = new TabPage("棋   Phòng đấu") { BackColor = Color.FromArgb(255, 252, 247), Padding = new Padding(12) };
        var logTab = new TabPage("▣   Nhật ký hoạt động") { BackColor = Color.FromArgb(255, 252, 247), Padding = new Padding(12) };
        tabs.TabPages.AddRange([playerTab, roomTab, logTab]);

        _players.Columns.Add("name", "Tên người chơi");
        _players.Columns.Add("status", "Trạng thái");
        _players.Columns.Add("connection", "Kết nối");
        _players.Columns.Add("activity", "Hoạt động gần nhất");
        _players.Columns[0].FillWeight = 26; _players.Columns[1].FillWeight = 20; _players.Columns[2].FillWeight = 18; _players.Columns[3].FillWeight = 36;
        playerTab.Controls.Add(_players);

        _rooms.Columns.Add("id", "Mã phòng");
        _rooms.Columns.Add("mode", "Chế độ");
        _rooms.Columns.Add("activity", "Hoạt động gần nhất");
        _rooms.Columns.Add("time", "Cập nhật");
        _rooms.Columns[0].FillWeight = 34; _rooms.Columns[1].FillWeight = 18; _rooms.Columns[2].FillWeight = 34; _rooms.Columns[3].FillWeight = 14;
        roomTab.Controls.Add(_rooms);

        var logTools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.FromArgb(255, 252, 247), Padding = new Padding(0, 2, 0, 4) };
        var clear = MakeButton("✕  Xóa hiển thị", Color.FromArgb(239, 228, 216), Ink, 126);
        clear.Click += (_, _) => _log.Clear();
        var save = MakeButton("↓  Lưu nhật ký", Wine, Color.White, 126);
        save.Click += SaveLog;
        logTools.Controls.Add(clear); logTools.Controls.Add(save);
        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.BackColor = Color.FromArgb(22, 16, 24);
        _log.ForeColor = Color.FromArgb(244, 231, 214);
        _log.BorderStyle = BorderStyle.None;
        _log.Font = new Font("Cascadia Mono", 10F);
        _log.DetectUrls = false;
        _log.ZoomFactor = 1.05F;
        logTab.Controls.Add(_log); logTab.Controls.Add(logTools);
        root.Controls.Add(tabs, 0, 2);

        _footer.Text = "●  Đang chuẩn bị máy chủ...   •   Hệ thống giám sát UDM18";
        _footer.ForeColor = Muted;
        _footer.Dock = DockStyle.Fill;
        _footer.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_footer, 0, 3);
    }

    private async Task StartServerAsync()
    {
        if (_server is { HasExited: false }) return;
        if (!File.Exists(_serverPath))
        {
            SetOffline($"Không tìm thấy server: {_serverPath}");
            MessageBox.Show(this, "Không tìm thấy file máy chủ. Hãy chạy start.bat để dự án tự build lại.", "UDM18 Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _connections.Clear();
        _clientValue.Text = "0";
        _stateValue.Text = "ĐANG KHỞI ĐỘNG";
        _stateValue.ForeColor = Gold;
        AppendSystemLog("Đang mở XiangqiOnline.Server.exe...");
        var start = new ProcessStartInfo(_serverPath)
        {
            WorkingDirectory = Path.GetDirectoryName(_serverPath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        _server = new Process { StartInfo = start, EnableRaisingEvents = true };
        _server.OutputDataReceived += (_, e) => ReceiveLine(e.Data, false);
        _server.ErrorDataReceived += (_, e) => ReceiveLine(e.Data, true);
        _server.Exited += (_, _) => BeginInvoke(() => SetOffline("Tiến trình máy chủ đã dừng."));
        try
        {
            _server.Start();
            _server.BeginOutputReadLine();
            _server.BeginErrorReadLine();
            _startedAt = DateTime.Now;
            _uptimeTimer.Start();
            await Task.Delay(350);
            if (!_server.HasExited)
            {
                _stateValue.Text = "TRỰC TUYẾN";
                _stateValue.ForeColor = Color.FromArgb(34, 139, 94);
                _footer.Text = $"●  Server đang chạy  •  PID {_server.Id}  •  127.0.0.1:5000";
                _footer.ForeColor = Color.FromArgb(34, 139, 94);
            }
        }
        catch (Exception ex)
        {
            SetOffline(ex.Message);
            AppendLog("LỖI", ex.Message, true);
        }
    }

    private void StartLogMonitor()
    {
        _startedAt = DateTime.Now;
        _stateValue.Text = "TRỰC TUYẾN";
        _stateValue.ForeColor = Color.FromArgb(34, 139, 94);
        _footer.Text = "●  Đang xem server chạy nền  •  127.0.0.1:5000  •  Không làm gián đoạn trận";
        _footer.ForeColor = Color.FromArgb(34, 139, 94);
        AppendSystemLog("Đã kết nối bảng giám sát với nhật ký server chạy nền.");
        ReloadMonitoredLog();
        _uptimeTimer.Start();
        _logMonitorTimer.Start();
    }

    private void ReloadMonitoredLog()
    {
        _log.Clear();
        _connections.Clear();
        _playerRows.Clear();
        _roomRows.Clear();
        _players.Rows.Clear();
        _rooms.Rows.Clear();
        _clientValue.Text = "0";
        _roomValue.Text = "0";
        _monitoredLogPath = null;
        _monitoredLineCount = 0;
        RefreshMonitoredLog(loadTailOnly: true);
    }

    private void RefreshMonitoredLog(bool loadTailOnly = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_logDirectory) || !Directory.Exists(_logDirectory))
            {
                _footer.Text = "●  Không tìm thấy thư mục nhật ký server.";
                _footer.ForeColor = Wine;
                return;
            }

            var latest = Directory.GetFiles(_logDirectory, "server-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (latest is null) return;

            var lines = ReadSharedLines(latest);
            if (!string.Equals(latest, _monitoredLogPath, StringComparison.OrdinalIgnoreCase) || lines.Length < _monitoredLineCount)
            {
                _monitoredLogPath = latest;
                _monitoredLineCount = loadTailOnly ? Math.Max(0, lines.Length - 500) : 0;
            }

            for (var index = _monitoredLineCount; index < lines.Length; index++)
                if (!string.IsNullOrWhiteSpace(lines[index])) ProcessLogLine(lines[index], false);
            _monitoredLineCount = lines.Length;
        }
        catch (IOException)
        {
            // Server có thể đang ghi đúng lúc đọc; lần timer kế tiếp sẽ thử lại.
        }
    }

    private static string[] ReadSharedLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd().Split(["\r\n", "\n"], StringSplitOptions.None);
    }

    private async Task StopServerAsync()
    {
        _uptimeTimer.Stop();
        if (_server is null || _server.HasExited) { SetOffline("Server đã dừng."); return; }
        AppendSystemLog("Đang dừng máy chủ...");
        try
        {
            _server.Kill(entireProcessTree: true);
            await _server.WaitForExitAsync();
        }
        catch (InvalidOperationException) { }
        SetOffline("Server đã dừng.");
    }

    private void ReceiveLine(string? line, bool error)
    {
        if (string.IsNullOrWhiteSpace(line) || IsDisposed) return;
        BeginInvoke(() => ProcessLogLine(line, error));
    }

    private void ProcessLogLine(string line, bool error)
    {
        var match = LogPattern.Match(line);
        var category = match.Success ? match.Groups["category"].Value : (error ? "LỖI" : "SERVER");
        var message = match.Success ? match.Groups["message"].Value : line;
        var time = match.Success ? match.Groups["time"].Value : DateTime.Now.ToString("HH:mm:ss");
        AppendLog(category, message, error, time);

        if (category.Equals("KẾT NỐI", StringComparison.OrdinalIgnoreCase))
        {
            var id = ExtractConnectionId(message);
            if (id is not null) _connections.Add(id);
            _clientValue.Text = _connections.Count.ToString();
        }
        else if (category.Equals("NGẮT", StringComparison.OrdinalIgnoreCase))
        {
            var id = ExtractConnectionId(message);
            if (id is not null) _connections.Remove(id);
            _clientValue.Text = _connections.Count.ToString();
        }
        else if (category.Equals("SẢNH", StringComparison.OrdinalIgnoreCase)) UpdatePlayer(message, time);

        if (category is "ĐẤU MÁY" or "TẠO PHÒNG" or "VÀO PHÒNG" or "NƯỚC ĐI" or "MÁY ĐI" or "VÒNG ĐỜI") UpdateRoom(category, message, time);
    }

    private void UpdatePlayer(string message, string time)
    {
        var parts = message.Split('•', StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;
        var identity = parts[0];
        var open = identity.LastIndexOf('(');
        var name = open > 0 ? identity[..open].Trim() : identity;
        var status = open > 0 ? identity[(open + 1)..].TrimEnd(')') : "UNKNOWN";
        var activity = parts.Length > 1 ? parts[1] : "Cập nhật";
        if (!_playerRows.TryGetValue(name, out var row))
        {
            var index = _players.Rows.Add(name, status, status.Contains("DISCONNECTED") ? "Ngoại tuyến" : "Trực tuyến", $"{activity} • {time}");
            row = _players.Rows[index];
            _playerRows[name] = row;
        }
        else
        {
            row.SetValues(name, status, status.Contains("DISCONNECTED") ? "Ngoại tuyến" : "Trực tuyến", $"{activity} • {time}");
        }
    }

    private void UpdateRoom(string category, string message, string time)
    {
        var roomMatch = Regex.Match(message, @"phòng\s+([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase);
        if (!roomMatch.Success) return;
        var id = roomMatch.Groups[1].Value.TrimEnd('.', ',', '•');
        var mode = category is "ĐẤU MÁY" or "MÁY ĐI" || message.Contains("BOT_", StringComparison.OrdinalIgnoreCase) ? "Đấu máy" : "Trực tuyến";
        if (!_roomRows.TryGetValue(id, out var row))
        {
            var index = _rooms.Rows.Add(id, mode, $"{category}: {message}", time);
            row = _rooms.Rows[index];
            _roomRows[id] = row;
            _roomValue.Text = _roomRows.Count.ToString();
        }
        else row.SetValues(id, mode, $"{category}: {message}", time);
    }

    private void AppendLog(string category, string message, bool error = false, string? time = null)
    {
        time ??= DateTime.Now.ToString("HH:mm:ss");
        var categoryColor = error ? Color.FromArgb(255, 107, 120) : CategoryColor(category);
        var lineBackground = CategoryBackground(category, error);
        _log.SelectionStart = _log.TextLength;
        _log.SelectionBackColor = lineBackground;
        _log.SelectionColor = categoryColor;
        _log.SelectionFont = new Font(_log.Font, FontStyle.Bold);
        _log.AppendText("● ");
        _log.SelectionColor = Color.FromArgb(174, 157, 185);
        _log.SelectionFont = _log.Font;
        _log.AppendText($"{time,-12} ");
        _log.SelectionColor = categoryColor;
        _log.SelectionFont = new Font(_log.Font, FontStyle.Bold);
        _log.AppendText($"{category,-12}");
        _log.SelectionFont = _log.Font;
        _log.SelectionColor = Color.FromArgb(100, 82, 112);
        _log.AppendText("│ ");
        AppendColoredMessage(message, lineBackground);
        _log.SelectionBackColor = _log.BackColor;
        _log.AppendText(Environment.NewLine);
        _log.ScrollToCaret();
    }

    private void AppendColoredMessage(string message, Color background)
    {
        var matches = Regex.Matches(message,
            @"XiangqiOnline(?:\.[A-Za-z0-9_]+)+(?:\.exe)?|(?:\d{1,3}\.){3}\d{1,3}:\d+|\.NET\s+10|Ctrl\+C|PID\s+\d+|#\d+|\b(?:ONLINE|OFFLINE|AVAILABLE|IN_GAME|RECONNECTING|TRỰC TUYẾN|NGOẠI TUYẾN)\b|\b\d+(?:\.\d+)?\b",
            RegexOptions.IgnoreCase);
        var offset = 0;
        foreach (Match match in matches)
        {
            if (match.Index > offset)
            {
                _log.SelectionColor = Color.FromArgb(244, 235, 224);
                _log.SelectionBackColor = background;
                _log.SelectionFont = _log.Font;
                _log.AppendText(message[offset..match.Index]);
            }
            var token = match.Value;
            _log.SelectionBackColor = background;
            _log.SelectionFont = new Font(_log.Font, FontStyle.Bold);
            _log.SelectionColor = token.Contains(':') && char.IsDigit(token[0])
                ? Color.FromArgb(102, 220, 255)
                : token.StartsWith("XiangqiOnline", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb(206, 158, 255)
                    : token.StartsWith('#') || token.StartsWith("PID", StringComparison.OrdinalIgnoreCase)
                        ? Color.FromArgb(255, 137, 177)
                        : token.Contains("ONLINE", StringComparison.OrdinalIgnoreCase) || token.Contains("TRỰC TUYẾN", StringComparison.OrdinalIgnoreCase) || token.Contains("AVAILABLE", StringComparison.OrdinalIgnoreCase)
                            ? Color.FromArgb(91, 229, 161)
                            : token.Contains("OFFLINE", StringComparison.OrdinalIgnoreCase) || token.Contains("NGOẠI TUYẾN", StringComparison.OrdinalIgnoreCase)
                                ? Color.FromArgb(255, 112, 124)
                                : Color.FromArgb(255, 205, 105);
            _log.AppendText(token);
            offset = match.Index + match.Length;
        }
        if (offset < message.Length)
        {
            _log.SelectionColor = Color.FromArgb(244, 235, 224);
            _log.SelectionBackColor = background;
            _log.SelectionFont = _log.Font;
            _log.AppendText(message[offset..]);
        }
    }

    private void AppendSystemLog(string message) => AppendLog("HỆ THỐNG", message);

    private void SetOffline(string detail)
    {
        _uptimeTimer.Stop();
        _stateValue.Text = "NGOẠI TUYẾN";
        _stateValue.ForeColor = Wine;
        _footer.Text = "●  " + detail;
        _footer.ForeColor = Wine;
    }

    private void UpdateUptime()
    {
        var elapsed = DateTime.Now - _startedAt;
        _uptimeValue.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing) return;
        _closing = true;
        if (_monitorOnly)
        {
            _logMonitorTimer.Stop();
            _uptimeTimer.Stop();
            return;
        }
        await StopServerAsync();
    }

    private void SaveLog(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog { Filter = "Nhật ký (*.log)|*.log|Văn bản (*.txt)|*.txt", FileName = $"UDM18-server-{DateTime.Now:yyyyMMdd-HHmmss}.log" };
        if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, _log.Text, Encoding.UTF8);
    }

    private static string? ExtractConnectionId(string message)
    {
        var match = Regex.Match(message, @"#(\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static Color CategoryColor(string category) => category switch
    {
        "LỖI" or "MẠNG" or "XỬ LÝ" => Color.FromArgb(248, 103, 103),
        "SẴN SÀNG" or "KẾT NỐI" or "NƯỚC ĐI" or "MÁY ĐI" => Color.FromArgb(93, 205, 146),
        "NGẮT" or "VÒNG ĐỜI" => Color.FromArgb(242, 191, 88),
        "ĐẤU MÁY" => Color.FromArgb(111, 178, 255),
        _ => Color.FromArgb(215, 170, 81)
    };

    private static Color CategoryBackground(string category, bool error) => error || category is "LỖI" or "MẠNG" or "XỬ LÝ"
        ? Color.FromArgb(49, 19, 29)
        : category is "SẴN SÀNG" or "KẾT NỐI" or "NƯỚC ĐI" or "MÁY ĐI"
            ? Color.FromArgb(17, 38, 34)
            : category is "NGẮT" or "VÒNG ĐỜI"
                ? Color.FromArgb(47, 35, 18)
                : category is "ĐẤU MÁY"
                    ? Color.FromArgb(18, 31, 51)
                    : Color.FromArgb(30, 22, 33);

    private static void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs) return;
        var selected = e.Index == tabs.SelectedIndex;
        var bounds = e.Bounds;
        using var background = new SolidBrush(selected ? Wine : Color.FromArgb(239, 229, 214));
        using var foreground = new SolidBrush(selected ? Color.White : Color.FromArgb(79, 58, 52));
        e.Graphics.FillRectangle(background, bounds);
        if (selected)
        {
            using var goldPen = new Pen(Gold, 3);
            e.Graphics.DrawLine(goldPen, bounds.Left + 1, bounds.Bottom - 2, bounds.Right - 1, bounds.Bottom - 2);
        }
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, bounds, foreground.Color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static string ResolveServerPath(string[] args)
    {
        var index = Array.FindIndex(args, value => value.Equals("--server", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length) return Path.GetFullPath(args[index + 1]);
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "XiangqiOnline.Server", "bin", "Release", "net10.0", "XiangqiOnline.Server.exe"));
    }

    private static string? ResolveArgument(string[] args, string name)
    {
        var index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null;
    }

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill,
        BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        GridColor = Color.FromArgb(235, 228, 216),
        ColumnHeadersHeight = 42,
        RowTemplate = { Height = 38 },
        EnableHeadersVisualStyles = false,
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(244, 236, 224), ForeColor = Ink, Font = new Font("Segoe UI Semibold", 9.5F), SelectionBackColor = Color.FromArgb(244, 236, 224), Alignment = DataGridViewContentAlignment.MiddleLeft },
        DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.White, ForeColor = Ink, SelectionBackColor = Color.FromArgb(252, 234, 222), SelectionForeColor = Wine, Padding = new Padding(5, 0, 5, 0) },
        AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(252, 249, 244) }
    };

    private static Control CreateCard(string title, string glyph, Label value, string initial, Color accent, Color tint)
    {
        var card = new GradientPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 14, 0),
            CornerRadius = 18,
            StartColor = tint,
            EndColor = Color.White
        };
        card.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 5, BackColor = accent });
        var icon = new RoundedPanel { BackColor = accent, CornerRadius = 17, Size = new Size(34, 34), Location = new Point(18, 17) };
        icon.Controls.Add(new Label { Text = glyph, ForeColor = Color.White, BackColor = Color.Transparent, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(glyph == "棋" ? "Microsoft YaHei" : "Segoe UI Symbol", 14F, FontStyle.Bold) });
        card.Controls.Add(icon);
        card.Controls.Add(new Label { Text = title, ForeColor = Muted, BackColor = Color.Transparent, Font = new Font("Segoe UI Semibold", 8.5F), AutoSize = true, Location = new Point(62, 17) });
        value.Text = initial; value.ForeColor = accent; value.BackColor = Color.Transparent; value.Font = new Font("Segoe UI Semibold", 16F); value.AutoSize = true; value.Location = new Point(60, 40);
        card.Controls.Add(value);
        return card;
    }

    private static Button MakeButton(string text, Color back, Color fore, int width) => new()
    {
        Text = text,
        BackColor = back,
        ForeColor = fore,
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderSize = 0 },
        Width = width,
        Height = 40,
        Cursor = Cursors.Hand,
        Font = new Font("Segoe UI Semibold", 9.5F),
        UseVisualStyleBackColor = false,
        Margin = new Padding(7, 0, 0, 0)
    };
}

internal class RoundedPanel : Panel
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 12;
    public RoundedPanel() { DoubleBuffered = true; }
    protected override void OnResize(EventArgs eventargs) { base.OnResize(eventargs); UpdateRegion(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateRegion(); }
    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = new GraphicsPath();
        var d = CornerRadius * 2;
        path.AddArc(0, 0, d, d, 180, 90); path.AddArc(Width - d, 0, d, d, 270, 90);
        path.AddArc(Width - d, Height - d, d, d, 0, 90); path.AddArc(0, Height - d, d, d, 90, 90);
        path.CloseFigure(); Region = new Region(path);
    }
}

internal sealed class GradientPanel : RoundedPanel
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color StartColor { get; set; } = Color.White;
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color EndColor { get; set; } = Color.White;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new LinearGradientBrush(ClientRectangle, StartColor, EndColor, LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(brush, ClientRectangle);
        using var glow = new SolidBrush(Color.FromArgb(22, Color.White));
        e.Graphics.FillEllipse(glow, Width - 180, -95, 250, 210);
        using var spark = new SolidBrush(Color.FromArgb(105, 255, 218, 128));
        e.Graphics.FillEllipse(spark, Width - 52, 18, 5, 5);
        e.Graphics.FillEllipse(spark, Width - 82, 52, 3, 3);
    }
}
