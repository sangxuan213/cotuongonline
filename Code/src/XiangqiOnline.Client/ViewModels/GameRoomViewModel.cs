using System.Collections.ObjectModel;
using UDM18.Client.Models;
using UDM18.Client.Protocol;
using XiangqiOnline.Shared.Contracts;

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
    private string _status = "Đang chờ Server tạo phòng và gửi snapshot.";
    private BoardOrientation _orientation = BoardOrientation.RedAtBottom;
    private bool _demoMode;

    public GameRoomViewModel(GameClient client)
    {
        _client = client;
        CoordinateClickedCommand = new RelayCommand<Coordinate>(OnCoordinateClicked, _ => !IsMovePending && RoomId is not null);
        FlipBoardCommand = new RelayCommand(() => Orientation = Orientation == BoardOrientation.RedAtBottom ? BoardOrientation.BlackAtBottom : BoardOrientation.RedAtBottom);
        _client.RoomCreated += roomId => Ui(() => { RoomId = roomId; Status = "Đã vào phòng; đang chờ snapshot authoritative từ Server."; });
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
        Status = "CHẾ ĐỘ DEMO CỤC BỘ — board mẫu 32 quân, không phải state từ Server.";
    }

    private async void OnCoordinateClicked(Coordinate coordinate)
    {
        if (Selected is null)
        {
            var piece = Pieces.FirstOrDefault(p => p.Position == coordinate && !p.Captured);
            if (piece is null || piece.Side != CurrentTurn)
            {
                Status = $"Chỉ được chọn quân của bên đang đi ({CurrentTurn}).";
                return;
            }
            Selected = coordinate;
            Status = $"Đã chọn ô {coordinate}; chọn ô đích.";
            return;
        }

        var from = Selected.Value;
        Selected = null;
        if (from == coordinate)
        {
            Status = "Đã bỏ chọn quân.";
            return;
        }
        if (_demoMode)
        {
            LastFrom = from;
            LastTo = coordinate;
            Status = $"DEMO: sẽ gửi MOVE_REQUEST {from} → {coordinate}. Board không đổi khi chưa có Server commit.";
            return;
        }
        IsMovePending = true;
        Status = $"Đang gửi {from} → {coordinate}; chờ Server xác nhận...";
        try { await _client.SendMoveAsync(RoomId!, Revision, from, coordinate); }
        catch (Exception ex)
        {
            IsMovePending = false;
            Status = $"Không gửi được nước đi: {ex.Message}";
        }
    }

    private void ApplySnapshot(GameSnapshot snapshot)
    {
        if (snapshot.Revision < Revision)
        {
            Status = $"Bỏ qua snapshot cũ revision {snapshot.Revision}; hiện tại là {Revision}.";
            return;
        }
        RoomId = snapshot.RoomId;
        Revision = snapshot.Revision;
        CurrentTurn = snapshot.CurrentTurn;
        Pieces.Clear();
        foreach (var piece in snapshot.Pieces.Where(p => !p.Captured)) Pieces.Add(piece);
        Selected = null;
        LastFrom = null;
        LastTo = null;
        IsMovePending = false;
        Status = $"Snapshot revision {Revision}; lượt {CurrentTurn}; {Pieces.Count} quân.";
    }

    private void ApplyCommittedMove(long revision, MoveDelta delta)
    {
        if (revision <= Revision) return;
        if (revision != Revision + 1)
        {
            RequestResync($"Thiếu event giữa revision {Revision} và {revision}");
            return;
        }
        var moving = string.IsNullOrWhiteSpace(delta.PieceId)
            ? Pieces.FirstOrDefault(p => p.Position == delta.From)
            : Pieces.FirstOrDefault(p => p.PieceId == delta.PieceId);
        if (moving is null)
        {
            RequestResync($"Không tìm thấy quân {delta.PieceId} trong revision {Revision}");
            return;
        }
        var target = Pieces.FirstOrDefault(p =>
            !ReferenceEquals(p, moving) &&
            (p.Position == delta.To ||
             (!string.IsNullOrWhiteSpace(delta.CapturedPieceId) && p.PieceId == delta.CapturedPieceId)));
        if (target is not null) Pieces.Remove(target);
        var index = Pieces.IndexOf(moving);
        if (index < 0)
        {
            RequestResync($"Trạng thái bàn cờ không nhất quán tại revision {Revision}");
            return;
        }
        Pieces[index] = moving with { Position = delta.To };
        Revision = revision;
        CurrentTurn = delta.CurrentTurn ?? (moving.Side == Side.RED ? Side.BLACK : Side.RED);
        LastFrom = delta.From;
        LastTo = delta.To;
        IsMovePending = false;
        Status = $"Server đã commit nước đi; revision {revision}.";
    }

    private void RejectMove(string code, string message, long serverRevision)
    {
        IsMovePending = false;
        Selected = null;
        Status = $"{code}: {message} (board giữ nguyên, server revision {serverRevision}).";
    }

    private async void RequestResync(string reason)
    {
        IsMovePending = false;
        if (RoomId is null)
        {
            Status = $"{reason}; chưa có roomId để đồng bộ lại.";
            return;
        }
        Status = $"{reason}; đang yêu cầu snapshot mới từ Server.";
        try { await _client.RequestResyncAsync(RoomId, Revision); }
        catch (Exception ex) { Status = $"Không thể yêu cầu đồng bộ lại: {ex.Message}"; }
    }

    private static void Ui(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action);
    }
}
