using System.Collections.ObjectModel;
using UDM18.Client.Models;
using UDM18.Client.Protocol;

namespace UDM18.Client.ViewModels;

public sealed class LobbyViewModel : ObservableObject
{
    private readonly GameClient _client;
    private PlayerSummary? _selectedPlayer;
    private ChallengeSummary? _incomingChallenge;
    private string _status = "KĂ¡ÂºÂ¿t nĂ¡Â»â€˜i Ă„â€˜Ă¡Â»Æ’ tĂ¡ÂºÂ£i danh sÄ‚Â¡ch ngĂ†Â°Ă¡Â»Âi chĂ†Â¡i.";

    public LobbyViewModel(GameClient client)
    {
        _client = client;
        RefreshCommand = new AsyncRelayCommand(ct => _client.RequestPlayersAsync(ct), () => _client.State == ConnectionState.Connected);
        ChallengeCommand = new AsyncRelayCommand(ct => _client.SendChallengeAsync(SelectedPlayer!.PlayerId, ct), () => SelectedPlayer?.Status == PlayerStatus.AVAILABLE);
        AcceptCommand = new AsyncRelayCommand(ct => _client.AcceptChallengeAsync(IncomingChallenge!.ChallengeId, ct), () => IncomingChallenge is not null);
        RejectCommand = new AsyncRelayCommand(ct => _client.RejectChallengeAsync(IncomingChallenge!.ChallengeId, ct), () => IncomingChallenge is not null);
        foreach (var command in new[] { RefreshCommand, ChallengeCommand, AcceptCommand, RejectCommand })
            command.Failed += ex => Ui(() => Status = $"KhÄ‚Â´ng thĂ¡Â»Â±c hiĂ¡Â»â€¡n Ă„â€˜Ă†Â°Ă¡Â»Â£c yÄ‚Âªu cĂ¡ÂºÂ§u: {ex.Message}");
        _client.PlayersUpdated += OnPlayersUpdated;
        _client.ChallengeReceived += challenge => Ui(() => { IncomingChallenge = challenge; Status = $"{challenge.FromDisplayName} mĂ¡Â»Âi bĂ¡ÂºÂ¡n thi Ă„â€˜Ă¡ÂºÂ¥u."; });
        _client.RoomCreated += id => Ui(() => { IncomingChallenge = null; Status = $"PhÄ‚Â²ng {id} Ă„â€˜Ä‚Â£ Ă„â€˜Ă†Â°Ă¡Â»Â£c tĂ¡ÂºÂ¡o."; });
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
        Players.Clear();
        Players.Add(new PlayerSummary("DEMO-01", "Minh Anh", PlayerStatus.AVAILABLE));
        Players.Add(new PlayerSummary("DEMO-02", "Quang Huy", PlayerStatus.IN_GAME));
        Players.Add(new PlayerSummary("DEMO-03", "Lan Chi", PlayerStatus.AVAILABLE));
        Players.Add(new PlayerSummary("DEMO-04", "Gia BĂ¡ÂºÂ£o", PlayerStatus.INVITING));
        Status = "CHĂ¡ÂºÂ¾ Ă„ÂĂ¡Â»Ëœ DEMO CĂ¡Â»Â¤C BĂ¡Â»Ëœ Ă¢â‚¬â€ dĂ¡Â»Â¯ liĂ¡Â»â€¡u nÄ‚Â y khÄ‚Â´ng Ă„â€˜Ă¡ÂºÂ¿n tĂ¡Â»Â« Server.";
    }

    private void OnPlayersUpdated(IReadOnlyList<PlayerSummary> players) => Ui(() =>
    {
        Players.Clear();
        foreach (var player in players) Players.Add(player);
        Status = $"{players.Count} ngĂ†Â°Ă¡Â»Âi chĂ†Â¡i Ă„â€˜ang Ă„â€˜Ă†Â°Ă¡Â»Â£c Server cÄ‚Â´ng bĂ¡Â»â€˜.";
    });

    private static void Ui(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action);
    }
}
