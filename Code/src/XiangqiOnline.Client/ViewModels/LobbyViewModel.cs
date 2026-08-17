using System.Collections.ObjectModel;
using UDM18.Client.Models;
using UDM18.Client.Protocol;
using System.Windows.Threading;

namespace UDM18.Client.ViewModels;

public sealed class LobbyViewModel : ObservableObject
{
    private readonly GameClient _client;
    private PlayerSummary? _selectedPlayer;
    private ChallengeSummary? _incomingChallenge;
    private string _status = "Kết nối để tải danh sách người chơi.";
    private bool _demoMode;
    private ActiveMatchSummary? _selectedMatch;
    private WaitingRoomSummary? _selectedWaitingRoom;
    private MatchHistorySummary? _selectedHistory;
    private BotDifficultyChoice _selectedBotDifficulty;
    private string? _outgoingChallengeId;
    private string? _outgoingTargetName;
    private string _roomPassword = "";
    private string _joinRoomPassword = "";
    private bool _liveRefreshRunning;

    public event Action? OpenGameRequested;
    public event Action? LogoutRequested;

    public LobbyViewModel(GameClient client)
    {
        _client = client;
        BotDifficulties.Add(new("Dễ", "EASY", "Đi ngẫu nhiên trong các nước hợp lệ"));
        BotDifficulties.Add(new("Trung bình", "MEDIUM", "Ưu tiên ăn quân và chiếu tướng"));
        BotDifficulties.Add(new("Khó", "HARD", "Đánh giá vật chất và tính trước phản đòn"));
        _selectedBotDifficulty = BotDifficulties[0];
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _client.State == ConnectionState.Connected);
        ChallengeCommand = new AsyncRelayCommand(SendChallengeAsync, () => SelectedPlayer?.Status == LobbyPlayerStatus.AVAILABLE);
        CancelChallengeCommand = new AsyncRelayCommand(
            ct => _client.CancelChallengeAsync(_outgoingChallengeId!, ct),
            () => !string.IsNullOrWhiteSpace(_outgoingChallengeId));
        AcceptCommand = new AsyncRelayCommand(ct => _client.AcceptChallengeAsync(IncomingChallenge!.ChallengeId, ct), () => IncomingChallenge is not null);
        RejectCommand = new AsyncRelayCommand(ct => _client.RejectChallengeAsync(IncomingChallenge!.ChallengeId, ct), () => IncomingChallenge is not null);
        JoinSpectatorCommand = new AsyncRelayCommand(JoinSpectatorAsync, () => SelectedMatch is not null);
        WatchMatchCommand = new RelayCommand<ActiveMatchSummary>(match => _ = WatchMatchAsync(match), _ => _client.State == ConnectionState.Connected);
        RefreshHistoryCommand = new AsyncRelayCommand(RefreshHistoryAsync, () => _client.State == ConnectionState.Connected);
        ReplayCommand = new AsyncRelayCommand(ReplayHistoryAsync, () => SelectedHistory is not null);
        StartBotCommand = new AsyncRelayCommand(ct => _client.StartBotGameAsync(SelectedBotDifficulty.WireValue, ct), () => _client.State == ConnectionState.Connected);
        CreateRoomCommand = new AsyncRelayCommand(CreateRoomAsync, () => _client.State == ConnectionState.Connected);
        JoinRoomCommand = new AsyncRelayCommand(JoinRoomAsync,
            () => SelectedWaitingRoom is not null && SelectedWaitingRoom.OwnerPlayerId != _client.PlayerId);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync, () => _client.State == ConnectionState.Connected);
        foreach (var command in new[] { RefreshCommand, ChallengeCommand, CancelChallengeCommand, AcceptCommand, RejectCommand, JoinSpectatorCommand, RefreshHistoryCommand, ReplayCommand, StartBotCommand, CreateRoomCommand, JoinRoomCommand, LogoutCommand })
            command.Failed += ex => Ui(() => Status = $"Không thực hiện được yêu cầu: {ex.Message}");
        _client.PlayersUpdated += OnPlayersUpdated;
        _client.ActiveMatchesUpdated += matches => Ui(() =>
        {
            var selected = SelectedMatch?.RoomId;
            ActiveMatches.Clear();
            foreach (var match in matches) ActiveMatches.Add(match);
            SelectedMatch = ActiveMatches.FirstOrDefault(match => match.RoomId == selected);
        });
        _client.SnapshotReceived += snapshot => Ui(() =>
        {
            if (!snapshot.ViewerRole.Equals("SPECTATOR", StringComparison.OrdinalIgnoreCase)) return;
            Status = $"Đang xem trực tiếp phòng {snapshot.RoomId}.";
            OpenGameRequested?.Invoke();
        });
        _client.WaitingRoomsUpdated += rooms => Ui(() =>
        {
            var selected = SelectedWaitingRoom?.RoomId;
            WaitingRooms.Clear();
            foreach (var room in rooms) WaitingRooms.Add(room);
            SelectedWaitingRoom = WaitingRooms.FirstOrDefault(room => room.RoomId == selected);
        });
        _client.WaitingRoomCreated += room => Ui(() =>
        {
            Status = $"Đã tạo phòng {room.RoomId}. Đang chờ người chơi khác tham gia.";
            OpenGameRequested?.Invoke();
        });
        _client.ChallengeReceived += challenge => Ui(() => { IncomingChallenge = challenge; Status = $"{challenge.FromDisplayName} mời bạn thi đấu."; });
        _client.ChallengeSent += (challengeId, targetName) => Ui(() =>
        {
            SetOutgoingChallenge(challengeId, targetName);
            Status = $"Đã gửi lời thách đấu tới {targetName}.";
        });
        _client.ChallengeRejected += challengeId => Ui(() =>
        {
            var matched = IncomingChallenge?.ChallengeId == challengeId || _outgoingChallengeId == challengeId;
            if (!matched) return;
            if (IncomingChallenge?.ChallengeId == challengeId) IncomingChallenge = null;
            if (_outgoingChallengeId == challengeId) SetOutgoingChallenge(null, null);
            Status = "Lời thách đấu đã bị từ chối.";
        });
        _client.ChallengeCancelled += challengeId => Ui(() =>
        {
            if (IncomingChallenge?.ChallengeId == challengeId) IncomingChallenge = null;
            if (_outgoingChallengeId == challengeId) SetOutgoingChallenge(null, null);
            Status = "Lời thách đấu đã được hủy.";
        });
        _client.RoomCreated += id => Ui(() => { IncomingChallenge = null; SetOutgoingChallenge(null, null); Status = $"Phòng {id} đã được tạo."; OpenGameRequested?.Invoke(); });
        _client.ConnectionChanged += (_, _) => Ui(() => { RefreshCommand.NotifyCanExecuteChanged(); StartBotCommand.NotifyCanExecuteChanged(); CreateRoomCommand.NotifyCanExecuteChanged(); LogoutCommand.NotifyCanExecuteChanged(); });
        _client.ErrorReceived += error => Ui(() => Status = error);
        _client.HistoryUpdated += matches => Ui(() =>
        {
            var selectedMatchId = SelectedHistory?.MatchId;
            MatchHistory.Clear();
            foreach (var match in matches) MatchHistory.Add(match);
            SelectedHistory = MatchHistory.FirstOrDefault(match => match.MatchId == selectedMatchId) ?? MatchHistory.FirstOrDefault();
            Status = matches.Count == 0 ? "Bạn chưa có ván đấu nào đã hoàn tất." : $"Đã tải {matches.Count} ván trong lịch sử.";
        });
        _client.ReplayLoaded += () => Ui(() => { Status = "Đã mở trình phát lại từng nước."; OpenGameRequested?.Invoke(); });

        var liveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
        liveTimer.Tick += async (_, _) => await RefreshLiveTablesAsync();
        liveTimer.Start();
    }

    public ObservableCollection<PlayerSummary> Players { get; } = [];
    public ObservableCollection<ActiveMatchSummary> ActiveMatches { get; } = [];
    public ObservableCollection<WaitingRoomSummary> WaitingRooms { get; } = [];
    public ObservableCollection<MatchHistorySummary> MatchHistory { get; } = [];
    public ObservableCollection<BotDifficultyChoice> BotDifficulties { get; } = [];
    public ActiveMatchSummary? SelectedMatch { get => _selectedMatch; set { if (Set(ref _selectedMatch, value)) JoinSpectatorCommand.NotifyCanExecuteChanged(); } }
    public WaitingRoomSummary? SelectedWaitingRoom { get => _selectedWaitingRoom; set { if (Set(ref _selectedWaitingRoom, value)) { if (value?.IsLocked != true) JoinRoomPassword = ""; JoinRoomCommand.NotifyCanExecuteChanged(); } } }
    public MatchHistorySummary? SelectedHistory { get => _selectedHistory; set { if (Set(ref _selectedHistory, value)) ReplayCommand.NotifyCanExecuteChanged(); } }
    public BotDifficultyChoice SelectedBotDifficulty { get => _selectedBotDifficulty; set => Set(ref _selectedBotDifficulty, value); }
    public PlayerSummary? SelectedPlayer { get => _selectedPlayer; set { if (Set(ref _selectedPlayer, value)) ChallengeCommand.NotifyCanExecuteChanged(); } }
    public ChallengeSummary? IncomingChallenge { get => _incomingChallenge; private set { if (Set(ref _incomingChallenge, value)) { AcceptCommand.NotifyCanExecuteChanged(); RejectCommand.NotifyCanExecuteChanged(); } } }
    public bool HasOutgoingChallenge => !string.IsNullOrWhiteSpace(_outgoingChallengeId);
    public string OutgoingChallengeText => HasOutgoingChallenge ? $"Đang chờ {_outgoingTargetName} phản hồi" : "Không có lời mời đang chờ";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string RoomPassword { get => _roomPassword; set => Set(ref _roomPassword, value); }
    public string JoinRoomPassword { get => _joinRoomPassword; set => Set(ref _joinRoomPassword, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ChallengeCommand { get; }
    public AsyncRelayCommand CancelChallengeCommand { get; }
    public AsyncRelayCommand AcceptCommand { get; }
    public AsyncRelayCommand RejectCommand { get; }
    public AsyncRelayCommand JoinSpectatorCommand { get; }
    public RelayCommand<ActiveMatchSummary> WatchMatchCommand { get; }
    public AsyncRelayCommand RefreshHistoryCommand { get; }
    public AsyncRelayCommand ReplayCommand { get; }
    public AsyncRelayCommand StartBotCommand { get; }
    public AsyncRelayCommand CreateRoomCommand { get; }
    public AsyncRelayCommand JoinRoomCommand { get; }
    public AsyncRelayCommand LogoutCommand { get; }

    public void ClearForLogout()
    {
        Players.Clear();
        ActiveMatches.Clear();
        WaitingRooms.Clear();
        MatchHistory.Clear();
        SelectedPlayer = null;
        SelectedMatch = null;
        SelectedWaitingRoom = null;
        SelectedHistory = null;
        IncomingChallenge = null;
        SetOutgoingChallenge(null, null);
        Status = "Đã đăng xuất. Hẹn gặp lại bạn!";
    }

    public void LoadDemoData()
    {
        _demoMode = true;
        Players.Clear();
        Players.Add(new PlayerSummary("DEMO-01", "Minh Anh", LobbyPlayerStatus.AVAILABLE));
        Players.Add(new PlayerSummary("DEMO-02", "Quang Huy", LobbyPlayerStatus.IN_GAME));
        Players.Add(new PlayerSummary("DEMO-03", "Lan Chi", LobbyPlayerStatus.AVAILABLE));
        Players.Add(new PlayerSummary("DEMO-04", "Gia Bảo", LobbyPlayerStatus.INVITING));
        Status = "CHẾ ĐỘ DEMO CỤC BỘ — dữ liệu này không đến từ Server.";
    }

    private void OnPlayersUpdated(IReadOnlyList<PlayerSummary> players) => Ui(() =>
    {
        var selectedPlayerId = SelectedPlayer?.PlayerId;
        Players.Clear();
        foreach (var player in players.Where(player => player.PlayerId != _client.PlayerId)) Players.Add(player);
        SelectedPlayer = selectedPlayerId is null ? null : Players.FirstOrDefault(p => p.PlayerId == selectedPlayerId);
        Status = $"{players.Count} người chơi đang được Server công bố.";
    });

    private Task SendChallengeAsync(CancellationToken cancellationToken)
    {
        if (!_demoMode) return _client.SendChallengeAsync(SelectedPlayer!.PlayerId, cancellationToken);
        Status = $"DEMO: đã chọn gửi lời mời tới {SelectedPlayer!.DisplayName}; không gửi dữ liệu lên Server.";
        return Task.CompletedTask;
    }

    private async Task JoinSpectatorAsync(CancellationToken cancellationToken)
    {
        var roomId = SelectedMatch?.RoomId;
        if (string.IsNullOrWhiteSpace(roomId)) return;
        Status = $"Đang vào khán đài phòng {roomId}...";
        await _client.JoinSpectatorAsync(roomId, cancellationToken);
    }

    private async Task WatchMatchAsync(ActiveMatchSummary match)
    {
        SelectedMatch = match;
        Status = $"Đang mở trực tiếp bàn {match.ShortRoomId}...";
        try { await _client.JoinSpectatorAsync(match.RoomId); }
        catch (Exception ex) { Ui(() => Status = $"Không xem được trận: {ex.Message}"); }
    }

    private async Task CreateRoomAsync(CancellationToken cancellationToken)
    {
        var password = RoomPassword.Trim();
        if (password.Length > 24) { Status = "Mật khẩu phòng tối đa 24 ký tự."; return; }
        Status = password.Length == 0 ? "Đang tạo phòng công khai..." : "Đang tạo phòng có mật khẩu...";
        await _client.CreateWaitingRoomAsync(password.Length == 0 ? null : password, cancellationToken);
        RoomPassword = "";
    }

    private async Task JoinRoomAsync(CancellationToken cancellationToken)
    {
        var room = SelectedWaitingRoom;
        if (room is null) return;
        var password = JoinRoomPassword.Trim();
        if (room.IsLocked && password.Length == 0) { Status = "Phòng này có khóa; hãy nhập mật khẩu."; return; }
        Status = $"Đang vào phòng {room.ShortRoomId}...";
        await _client.JoinWaitingRoomAsync(room.RoomId, password.Length == 0 ? null : password, cancellationToken);
        JoinRoomPassword = "";
    }

    private async Task RefreshLiveTablesAsync()
    {
        if (_liveRefreshRunning || _client.State != ConnectionState.Connected) return;
        _liveRefreshRunning = true;
        try
        {
            await _client.RequestActiveMatchesAsync();
            await _client.RequestWaitingRoomsAsync();
        }
        catch { /* Kết nối sẽ tự báo trạng thái; bộ làm mới không làm gián đoạn UI. */ }
        finally { _liveRefreshRunning = false; }
    }

    private void SetOutgoingChallenge(string? challengeId, string? targetName)
    {
        _outgoingChallengeId = challengeId;
        _outgoingTargetName = targetName;
        Raise(nameof(HasOutgoingChallenge));
        Raise(nameof(OutgoingChallengeText));
        CancelChallengeCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _client.RequestPlayersAsync(cancellationToken);
        await _client.RequestActiveMatchesAsync(cancellationToken);
        await _client.RequestWaitingRoomsAsync(cancellationToken);
        await _client.RequestHistoryAsync(cancellationToken);
    }

    private async Task RefreshHistoryAsync(CancellationToken cancellationToken)
    {
        Status = "Đang tải lịch sử từ máy chủ...";
        await _client.RequestHistoryAsync(cancellationToken);
    }

    private async Task ReplayHistoryAsync(CancellationToken cancellationToken)
    {
        Status = "Đang tải toàn bộ nước đi của ván...";
        await _client.RequestHistoryDetailAsync(SelectedHistory!.MatchId, cancellationToken);
    }

    private async Task LogoutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _client.LogoutAsync();
        ClearForLogout();
        LogoutRequested?.Invoke();
    }

    private static void Ui(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action);
    }
}
