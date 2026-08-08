using System.Collections.ObjectModel;
using UDM18.Client.Models;
using UDM18.Client.Protocol;

namespace UDM18.Client.ViewModels;

public sealed class GameRoomViewModel : ObservableObject
{
    private readonly GameClient _client;
    private string? _roomId;
    private long _revision;
    private Side _currentTurn;
    private Coordinate? _selected;
    private Coordinate? _lastFrom;
    private Coordinate? _lastTo;
    private bool _isMovePending;
    private string _status = "Ă„Âang chĂ¡Â»Â Server tĂ¡ÂºÂ¡o phÄ‚Â²ng vÄ‚Â  gĂ¡Â»Â­i snapshot.";
    private BoardOrientation _orientation = BoardOrientation.RedAtBottom;
    private bool _demoMode;

    public GameRoomViewModel(GameClient client)
    {
        _client = client;
        CoordinateClickedCommand = new RelayCommand<Coordinate>(OnCoordinateClicked, _ => !IsMovePending && RoomId is not null);
        FlipBoardCommand = new RelayCommand(() => Orientation = Orientation == BoardOrientation.RedAtBottom ? BoardOrientation.BlackAtBottom : BoardOrientation.RedAtBottom);
        _client.RoomCreated += roomId => Ui(() => { RoomId = roomId; Status = "Ă„ÂÄ‚Â£ vÄ‚Â o phÄ‚Â²ng; Ă„â€˜ang chĂ¡Â»Â snapshot authoritative tĂ¡Â»Â« Server."; });
        _client.SnapshotReceived += snapshot => Ui(() => ApplySnapshot(snapshot));
        _client.MoveCommitted += (revision, delta) => Ui(() => ApplyCommittedMove(revision, delta));
        _client.MoveRejected += (code, message, revision) => Ui(() => RejectMove(code, message, revision));
        _client.ErrorReceived += message => Ui(() => Status = message);
    }

    public ObservableCollection<PieceState> Pieces { get; } = [];
    public string? RoomId { get => _roomId; private set { if (Set(ref _roomId, value)) CoordinateClickedCommand.NotifyCanExecuteChanged(); } }
    public long Revision { get => _revision; private set => Set(ref _revision, value); }
    public Side CurrentTurn { get => _currentTurn; private set => Set(ref _currentTurn, value); }
    public Coordinate? Selected { get => _selected; private set => Set(ref _selected, value); }
    public Coordinate? LastFrom { get => _lastFrom; private set => Set(ref _lastFrom, value); }
    public Coordinate? LastTo { get => _lastTo; private set => Set(ref _lastTo, value); }
    public bool IsMovePending { get => _isMovePending; private set { if (Set(ref _isMovePending, value)) CoordinateClickedCommand.NotifyCanExecuteChanged(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public BoardOrientation Orientation { get => _orientation; private set => Set(ref _orientation, value); }
    public RelayCommand<Coordinate> CoordinateClickedCommand { get; }
    public RelayCommand FlipBoardCommand { get; }

    public void LoadDemoData()
    {
        _demoMode = true;
        ApplySnapshot(new GameSnapshot("DEMO-ROOM", 18, Side.RED, InitialBoard.Create()));
        Status = "CHĂ¡ÂºÂ¾ Ă„ÂĂ¡Â»Ëœ DEMO CĂ¡Â»Â¤C BĂ¡Â»Ëœ Ă¢â‚¬â€ board mĂ¡ÂºÂ«u 32 quÄ‚Â¢n, khÄ‚Â´ng phĂ¡ÂºÂ£i state tĂ¡Â»Â« Server.";
    }

    private async void OnCoordinateClicked(Coordinate coordinate)
    {
        if (Selected is null)
        {
            Selected = coordinate;
            Status = $"Ă„ÂÄ‚Â£ chĂ¡Â»Ân Ä‚Â´ {coordinate}; chĂ¡Â»Ân Ä‚Â´ Ă„â€˜Ä‚Â­ch.";
            return;
        }

        var from = Selected.Value;
        Selected = null;
        if (_demoMode)
        {
            LastFrom = from;
            LastTo = coordinate;
            Status = $"DEMO: sĂ¡ÂºÂ½ gĂ¡Â»Â­i MOVE_REQUEST {from} Ă¢â€ â€™ {coordinate}. Board khÄ‚Â´ng Ă„â€˜Ă¡Â»â€¢i khi chĂ†Â°a cÄ‚Â³ Server commit.";
            return;
        }
        IsMovePending = true;
        Status = $"Ă„Âang gĂ¡Â»Â­i {from} Ă¢â€ â€™ {coordinate}; chĂ¡Â»Â Server xÄ‚Â¡c nhĂ¡ÂºÂ­n...";
        try { await _client.SendMoveAsync(RoomId!, Revision, from, coordinate); }
        catch (Exception ex)
        {
            IsMovePending = false;
            Status = $"KhÄ‚Â´ng gĂ¡Â»Â­i Ă„â€˜Ă†Â°Ă¡Â»Â£c nĂ†Â°Ă¡Â»â€ºc Ă„â€˜i: {ex.Message}";
        }
    }

    private void ApplySnapshot(GameSnapshot snapshot)
    {
        RoomId = snapshot.RoomId;
        Revision = snapshot.Revision;
        CurrentTurn = snapshot.CurrentTurn;
        Pieces.Clear();
        foreach (var piece in snapshot.Pieces.Where(p => !p.Captured)) Pieces.Add(piece);
        Selected = null;
        IsMovePending = false;
        Status = $"Snapshot revision {Revision}; lĂ†Â°Ă¡Â»Â£t {CurrentTurn}; {Pieces.Count} quÄ‚Â¢n.";
    }

    private void ApplyCommittedMove(long revision, MoveDelta delta)
    {
        if (revision <= Revision) return;
        if (revision != Revision + 1)
        {
            IsMovePending = false;
            Status = $"ThiĂ¡ÂºÂ¿u event giĂ¡Â»Â¯a revision {Revision} vÄ‚Â  {revision}; cĂ¡ÂºÂ§n snapshot mĂ¡Â»â€ºi tĂ¡Â»Â« Server.";
            return;
        }
        var moving = string.IsNullOrWhiteSpace(delta.PieceId)
            ? Pieces.FirstOrDefault(p => p.Position == delta.From)
            : Pieces.FirstOrDefault(p => p.PieceId == delta.PieceId);
        if (moving is null)
        {
            IsMovePending = false;
            Status = $"KhÄ‚Â´ng tÄ‚Â¬m thĂ¡ÂºÂ¥y quÄ‚Â¢n {delta.PieceId} trong revision {Revision}; cĂ¡ÂºÂ§n snapshot mĂ¡Â»â€ºi tĂ¡Â»Â« Server.";
            return;
        }
        var target = Pieces.FirstOrDefault(p => p.Position == delta.To || p.PieceId == delta.CapturedPieceId);
        if (target is not null) Pieces.Remove(target);
        var index = Pieces.IndexOf(moving);
        Pieces[index] = moving with { Position = delta.To };
        Revision = revision;
        LastFrom = delta.From;
        LastTo = delta.To;
        IsMovePending = false;
        Status = $"Server Ă„â€˜Ä‚Â£ commit nĂ†Â°Ă¡Â»â€ºc Ă„â€˜i; revision {revision}.";
    }

    private void RejectMove(string code, string message, long serverRevision)
    {
        IsMovePending = false;
        Selected = null;
        Status = $"{code}: {message} (board giĂ¡Â»Â¯ nguyÄ‚Âªn, server revision {serverRevision}).";
    }

    private static void Ui(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action);
    }
}
