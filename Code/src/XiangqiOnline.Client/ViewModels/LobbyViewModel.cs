using System.Collections.ObjectModel;
using UDM18.Client.Protocol;
using XiangqiOnline.Shared.Contracts;

namespace UDM18.Client.ViewModels;

public sealed class LobbyViewModel : ObservableObject
{
    private readonly GameClient _client;
    private PlayerSummary? _selectedPlayer;
    private ChallengeSummary? _incomingChallenge;
    private string _status = "Kết nối để tải danh sách người chơi.";
    private bool _demoMode;

    public LobbyViewModel(GameClient client)
    {
        _client = client;
        RefreshCommand = new AsyncRelayCommand(ct => _client.RequestPlayersAsync(ct), () => _client.State == ConnectionState.Connected);
        ChallengeCommand = new AsyncRelayCommand(SendChallengeAsync, () => SelectedPlayer?.Status == PlayerStatus.AVAILABLE);
        AcceptCommand = new AsyncRelayCommand(ct => _client.AcceptChallengeAsync(IncomingChallenge!.ChallengeId, ct), () => IncomingChallenge is not null);
        RejectCommand = new AsyncRelayCommand(ct => _client.RejectChallengeAsync(IncomingChallenge!.ChallengeId, ct), () => IncomingChallenge is not null);
        foreach (var command in new[] { RefreshCommand, ChallengeCommand, AcceptCommand, RejectCommand })
            command.Failed += ex => Ui(() => Status = $"Không thực hiện được yêu cầu: {ex.Message}");
        _client.PlayersUpdated += OnPlayersUpdated;
        _client.ChallengeReceived += challenge => Ui(() => { IncomingChallenge = challenge; Status = $"{challenge.FromDisplayName} mời bạn thi đấu."; });
        _client.RoomCreated += id => Ui(() => { IncomingChallenge = null; Status = $"Phòng {id} đã được tạo."; });
        _client.ConnectionChanged += (_, _) => Ui(() => RefreshCommand.NotifyCanExecuteChanged());
        _client.ErrorReceived += error => Ui(() => Status = error);
    }

    public ObservableCollection<PlayerSummary> Players { get; } = [];
    public PlayerSummary? SelectedPlayer { get => _selectedPlayer; set { if (Set(ref _selectedPlayer, value)) ChallengeCommand.NotifyCanExecuteChanged(); } }
    public ChallengeSummary? IncomingChallenge { get => _incomingChallenge; private set { if (Set(ref _incomingChallenge, value)) { AcceptCommand.NotifyCanExecuteChanged(); RejectCommand.NotifyCanExecuteChanged(); } } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ChallengeCommand { get; }
    public AsyncRelayCommand AcceptCommand { get; }
    public AsyncRelayCommand RejectCommand { get; }

    public void LoadDemoData()
    {
        _demoMode = true;
        Players.Clear();
        Players.Add(new PlayerSummary("DEMO-01", "Minh Anh", PlayerStatus.AVAILABLE));
        Players.Add(new PlayerSummary("DEMO-02", "Quang Huy", PlayerStatus.IN_GAME));
        Players.Add(new PlayerSummary("DEMO-03", "Lan Chi", PlayerStatus.AVAILABLE));
        Players.Add(new PlayerSummary("DEMO-04", "Gia Bảo", PlayerStatus.INVITING));
        Status = "CHẾ ĐỘ DEMO CỤC BỘ — dữ liệu này không đến từ Server.";
    }

    private void OnPlayersUpdated(IReadOnlyList<PlayerSummary> players) => Ui(() =>
    {
        var selectedPlayerId = SelectedPlayer?.PlayerId;
        Players.Clear();
        foreach (var player in players) Players.Add(player);
        SelectedPlayer = selectedPlayerId is null ? null : Players.FirstOrDefault(p => p.PlayerId == selectedPlayerId);
        Status = $"{players.Count} người chơi đang được Server công bố.";
    });

    private Task SendChallengeAsync(CancellationToken cancellationToken)
    {
        if (!_demoMode) return _client.SendChallengeAsync(SelectedPlayer!.PlayerId, cancellationToken);
        Status = $"DEMO: đã chọn gửi lời mời tới {SelectedPlayer!.DisplayName}; không gửi dữ liệu lên Server.";
        return Task.CompletedTask;
    }

    private static void Ui(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action);
    }
}
