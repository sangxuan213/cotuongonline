using System.Collections.ObjectModel;
using UDM18.Client.Models;
using UDM18.Client.Protocol;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using System.Windows.Threading;
using UDM18.Client.Services;

namespace UDM18.Client.ViewModels;

public sealed class GameRoomViewModel : ObservableObject
{
    private readonly GameClient _client;
    private string? _roomId;
    private long _revision;
    private SideColor _currentTurn;
    private Position? _selected;
    private Position? _lastFrom;
    private Position? _lastTo;
    private bool _isMovePending;
    private string _status = "Đang chờ Server tạo phòng và gửi snapshot.";
    private BoardOrientation _orientation = BoardOrientation.RedAtBottom;
    private bool _demoMode;
    private bool _isSpectator;
    private long _redRemainingMs;
    private long _blackRemainingMs;
    private string? _warning;
    private ClockSnapshotModel? _clockBase;
    private bool _hasIncomingDrawOffer;
    private bool _isGameEnded;
    private SideColor? _ownSide;
    private readonly GameAudioService _audio = new();
    private bool _isCheckAlert;
    private string _checkBanner = "CHIẾU TƯỚNG";
    private CancellationTokenSource? _checkAlertCts;
    private bool _useClassicPieces;
    private bool _isWaitingForOpponent;
    private bool _hasIncomingRematchOffer;
    private bool _isRematchPending;
    private CancellationTokenSource? _rematchExpiryCts;
    private string _chatInput = "";
    private IReadOnlyList<ReplayFrame> _replayFrames = [];
    private int _replayIndex;
    private bool _isReplayMode;
    private string _replayResult = "";

    public event Action? ReturnToLobbyRequested;

    public GameRoomViewModel(GameClient client)
    {
        _client = client;
        CoordinateClickedCommand = new RelayCommand<Position>(
            coordinate => _ = ObserveAsync(OnCoordinateClickedAsync(coordinate)), _ => CanMove);
        FlipBoardCommand = new RelayCommand(() => Orientation = Orientation == BoardOrientation.RedAtBottom ? BoardOrientation.BlackAtBottom : BoardOrientation.RedAtBottom);
        ReplayFirstCommand = new RelayCommand(() => ShowReplayFrame(0), () => IsReplayMode && _replayIndex > 0);
        ReplayPreviousCommand = new RelayCommand(() => ShowReplayFrame(_replayIndex - 1), () => IsReplayMode && _replayIndex > 0);
        ReplayNextCommand = new RelayCommand(() => ShowReplayFrame(_replayIndex + 1), () => IsReplayMode && _replayIndex < _replayFrames.Count - 1);
        ReplayLastCommand = new RelayCommand(() => ShowReplayFrame(_replayFrames.Count - 1), () => IsReplayMode && _replayIndex < _replayFrames.Count - 1);
        ResignCommand = new AsyncRelayCommand(ct => _client.ResignAsync(RoomId!, ct), () => RoomId is not null && !IsSpectator && !IsGameEnded && !IsWaitingForOpponent);
        OfferDrawCommand = new AsyncRelayCommand(ct => _client.OfferDrawAsync(RoomId!, ct), () => RoomId is not null && !IsSpectator && !IsGameEnded && !IsWaitingForOpponent);
        AcceptDrawCommand = new AsyncRelayCommand(ct => RespondDrawAsync(true, ct), () => HasIncomingDrawOffer && RoomId is not null && !IsGameEnded);
        DeclineDrawCommand = new AsyncRelayCommand(ct => RespondDrawAsync(false, ct), () => HasIncomingDrawOffer && RoomId is not null && !IsGameEnded);
        RequestRematchCommand = new AsyncRelayCommand(ct => RequestRematchAsync(ct),
            () => RoomId is not null && IsGameEnded && !IsSpectator && !IsRematchPending && !HasIncomingRematchOffer);
        AcceptRematchCommand = new AsyncRelayCommand(ct => RespondRematchAsync(true, ct),
            () => RoomId is not null && HasIncomingRematchOffer && IsGameEnded);
        DeclineRematchCommand = new AsyncRelayCommand(ct => RespondRematchAsync(false, ct),
            () => RoomId is not null && HasIncomingRematchOffer && IsGameEnded);
        LeaveSpectatorCommand = new AsyncRelayCommand(ct => _client.LeaveSpectatorAsync(RoomId!, ct), () => RoomId is not null && IsSpectator && !IsGameEnded);
        CancelWaitingRoomCommand = new AsyncRelayCommand(ct => _client.CancelWaitingRoomAsync(RoomId!, ct), () => RoomId is not null && IsWaitingForOpponent);
        SendQuickChatCommand = new RelayCommand<string>(code => _ = ObserveAsync(_client.SendQuickChatAsync(RoomId!, code)),
            _ => RoomId is not null && !IsWaitingForOpponent);
        SendChatCommand = new AsyncRelayCommand(SendChatAsync,
            () => RoomId is not null && !IsWaitingForOpponent && !string.IsNullOrWhiteSpace(ChatInput));
        ReturnToLobbyCommand = new RelayCommand(() => _ = ObserveAsync(ReturnToLobbyAsync()));
        var clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        clockTimer.Tick += (_, _) => RefreshDisplayedClock();
        clockTimer.Start();
        _client.RoomCreated += roomId => Ui(() => PrepareForRoom(roomId));
        _client.WaitingRoomCreated += room => Ui(() => PrepareWaitingRoom(room));
        _client.WaitingRoomCancelled += _ => Ui(() =>
        {
            ResetRoomState("Đã hủy phòng chờ.");
            ReturnToLobbyRequested?.Invoke();
        });
        _client.SnapshotReceived += snapshot => Ui(() => ApplySnapshot(snapshot));
        _client.ReplayTimelineLoaded += replay => Ui(() => LoadReplay(replay));
        _client.MoveCommitted += (revision, delta) => Ui(() => ApplyCommittedMove(revision, delta));
        _client.MoveRejected += (code, message, revision) => Ui(() => RejectMove(code, message, revision));
        _client.ErrorReceived += message => Ui(() =>
        {
            if (IsGameEnded && (IsRematchPending || HasIncomingRematchOffer))
            {
                CancelRematchExpiry();
                IsRematchPending = false;
                HasIncomingRematchOffer = false;
            }
            Status = message;
        });
        _client.ClockSynchronized += clocks => Ui(() => ApplyClock(clocks));
        _client.RepetitionWarningReceived += (side, _) => Ui(() => Warning = $"{side} phải đổi nước để tránh vi phạm lặp.");
        _client.DrawOffered += (offeredBy, expiresAt) => Ui(() =>
        {
            if (offeredBy == _client.PlayerId) { Status = $"Đã gửi đề nghị hòa; hết hạn lúc {expiresAt:HH:mm:ss}."; return; }
            _audio.PlayNotification();
            HasIncomingDrawOffer = true;
            Status = $"Đối thủ đề nghị hòa; hết hạn lúc {expiresAt:HH:mm:ss}.";
        });
        _client.DrawDeclined += () => Ui(() => { HasIncomingDrawOffer = false; Status = "Đề nghị hòa đã bị từ chối."; });
        _client.RematchOffered += (requestedBy, _, expiresAt) => Ui(() =>
        {
            ScheduleRematchExpiry(expiresAt);
            if (!string.Equals(requestedBy, _client.PlayerId, StringComparison.Ordinal))
            {
                _audio.PlayNotification();
                HasIncomingRematchOffer = true;
                IsRematchPending = false;
                Status = $"Đối thủ muốn đấu lại. Yêu cầu hết hạn lúc {expiresAt.ToLocalTime():HH:mm:ss}.";
            }
            else
            {
                IsRematchPending = true;
                Status = $"Đã gửi yêu cầu đấu lại; đang chờ đối thủ đến {expiresAt.ToLocalTime():HH:mm:ss}.";
            }
        });
        _client.RematchDeclined += () => Ui(() =>
        {
            CancelRematchExpiry();
            HasIncomingRematchOffer = false;
            IsRematchPending = false;
            Status = "Đối thủ đã từ chối đấu lại. Bạn có thể trở về sảnh.";
        });
        _client.RematchCancelled += () => Ui(() =>
        {
            CancelRematchExpiry();
            HasIncomingRematchOffer = false;
            IsRematchPending = false;
            Status = "Yêu cầu đấu lại đã được hủy.";
        });
        _client.GameEnded += result => Ui(() =>
        {
            CancelRematchExpiry();
            _audio.PlayGameEnded();
            IsMovePending = false;
            IsGameEnded = true;
            HasIncomingDrawOffer = false;
            HasIncomingRematchOffer = false;
            IsRematchPending = false;
            _clockBase = null;
            Warning = null;
            Status = $"Kết thúc: {result.ResultType} - {result.EndReason}. {result.Explanation}";
            CoordinateClickedCommand.NotifyCanExecuteChanged();
        });
        _client.SpectatorLeft += _ => Ui(() =>
        {
            ResetRoomState("Đã rời khỏi phòng khán giả.");
            ReturnToLobbyRequested?.Invoke();
        });
        _client.QuickChatReceived += message => Ui(() =>
        {
            if (!string.Equals(message.RoomId, RoomId, StringComparison.Ordinal)) return;
            ChatMessages.Insert(0, message);
            while (ChatMessages.Count > 30) ChatMessages.RemoveAt(ChatMessages.Count - 1);
            _audio.PlayNotification();
        });
    }

    public ObservableCollection<PieceState> Pieces { get; } = [];
    public ObservableCollection<QuickChatMessage> ChatMessages { get; } = [];
    public string? RoomId { get => _roomId; private set { if (Set(ref _roomId, value)) { CoordinateClickedCommand.NotifyCanExecuteChanged(); ResignCommand.NotifyCanExecuteChanged(); OfferDrawCommand.NotifyCanExecuteChanged(); CancelWaitingRoomCommand.NotifyCanExecuteChanged(); SendQuickChatCommand.NotifyCanExecuteChanged(); SendChatCommand.NotifyCanExecuteChanged(); RequestRematchCommand.NotifyCanExecuteChanged(); AcceptRematchCommand.NotifyCanExecuteChanged(); DeclineRematchCommand.NotifyCanExecuteChanged(); } } }
    public long Revision { get => _revision; private set => Set(ref _revision, value); }
    public SideColor CurrentTurn
    {
        get => _currentTurn;
        private set
        {
            if (Set(ref _currentTurn, value))
            {
                Raise(nameof(CurrentTurnLabel));
                Raise(nameof(RedClockStateText));
                Raise(nameof(BlackClockStateText));
                Raise(nameof(CanMove));
                CoordinateClickedCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public Position? Selected { get => _selected; private set => Set(ref _selected, value); }
    public Position? LastFrom { get => _lastFrom; private set => Set(ref _lastFrom, value); }
    public Position? LastTo { get => _lastTo; private set => Set(ref _lastTo, value); }
    public bool IsMovePending { get => _isMovePending; private set { if (Set(ref _isMovePending, value)) CoordinateClickedCommand.NotifyCanExecuteChanged(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public BoardOrientation Orientation { get => _orientation; private set => Set(ref _orientation, value); }
    public bool IsSpectator
    {
        get => _isSpectator;
        private set
        {
            if (Set(ref _isSpectator, value))
            {
                Raise(nameof(ViewerLabel));
                CoordinateClickedCommand.NotifyCanExecuteChanged();
                ResignCommand.NotifyCanExecuteChanged();
                OfferDrawCommand.NotifyCanExecuteChanged();
                LeaveSpectatorCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public long RedRemainingMs
    {
        get => _redRemainingMs;
        private set
        {
            var previousSecond = Math.Max(0, _redRemainingMs) / 1000;
            if (Set(ref _redRemainingMs, value) && previousSecond != Math.Max(0, value) / 1000)
                Raise(nameof(RedClockText));
        }
    }
    public long BlackRemainingMs
    {
        get => _blackRemainingMs;
        private set
        {
            var previousSecond = Math.Max(0, _blackRemainingMs) / 1000;
            if (Set(ref _blackRemainingMs, value) && previousSecond != Math.Max(0, value) / 1000)
                Raise(nameof(BlackClockText));
        }
    }
    public string RedClockText => FormatClock(RedRemainingMs);
    public string BlackClockText => FormatClock(BlackRemainingMs);
    public string RedClockStateText => IsWaitingForOpponent ? "CHỜ ĐỐI THỦ" : CurrentTurn == SideColor.Red ? "ĐANG CHẠY" : "ĐANG CHỜ";
    public string BlackClockStateText => IsWaitingForOpponent ? "CHỜ ĐỐI THỦ" : CurrentTurn == SideColor.Black ? "ĐANG CHẠY" : "ĐANG CHỜ";
    public string CurrentTurnLabel => CurrentTurn == SideColor.Red ? "LƯỢT ĐỎ" : "LƯỢT ĐEN";
    public string ViewerLabel => IsReplayMode ? "ĐANG XEM LẠI VÁN ĐẤU · dùng nút tua để xem từng nước"
        : IsWaitingForOpponent ? "Bạn là BÊN ĐỎ · đang chờ đối thủ"
        : IsSpectator ? "Bạn đang xem với vai trò KHÁN GIẢ"
        : _ownSide == SideColor.Red ? "Bạn là BÊN ĐỎ · quân Đỏ ở phía dưới"
        : _ownSide == SideColor.Black ? "Bạn là BÊN ĐEN · quân Đen ở phía dưới"
        : "Đang xác định bên chơi";
    public string OwnSideLabel => IsReplayMode ? "PHÁT LẠI LỊCH SỬ" : IsSpectator ? "KHÁN GIẢ" : _ownSide == SideColor.Red ? "BẠN CẦM QUÂN ĐỎ" : _ownSide == SideColor.Black ? "BẠN CẦM QUÂN ĐEN" : "ĐANG ĐỒNG BỘ PHE";
    public string OwnSideColor => _ownSide == SideColor.Red ? "#B51F24" : _ownSide == SideColor.Black ? "#24201C" : "#75675C";
    public bool CanMove => !IsWaitingForOpponent && !IsMovePending && !IsSpectator && !IsGameEnded && RoomId is not null && _ownSide == CurrentTurn;
    public bool IsWaitingForOpponent
    {
        get => _isWaitingForOpponent;
        private set
        {
            if (!Set(ref _isWaitingForOpponent, value)) return;
            Raise(nameof(ViewerLabel));
            Raise(nameof(RedClockStateText));
            Raise(nameof(BlackClockStateText));
            Raise(nameof(CanMove));
            CoordinateClickedCommand.NotifyCanExecuteChanged();
            ResignCommand.NotifyCanExecuteChanged();
            OfferDrawCommand.NotifyCanExecuteChanged();
            CancelWaitingRoomCommand.NotifyCanExecuteChanged();
            SendQuickChatCommand.NotifyCanExecuteChanged();
            SendChatCommand.NotifyCanExecuteChanged();
        }
    }
    public bool IsCheckAlert { get => _isCheckAlert; private set => Set(ref _isCheckAlert, value); }
    public string CheckBanner { get => _checkBanner; private set { if (Set(ref _checkBanner, value)) Raise(nameof(CheckBannerImage)); } }
    public string CheckBannerImage => CheckBanner == "CHIẾU BÍ"
        ? "/Assets/Classic/checkmate-banner.png" : "/Assets/Classic/check-banner.png";
    public bool IsSoundEnabled
    {
        get => _audio.Enabled;
        set { if (_audio.Enabled == value) return; _audio.Enabled = value; Raise(); }
    }
    public bool UseClassicPieces { get => _useClassicPieces; set => Set(ref _useClassicPieces, value); }
    public string? Warning { get => _warning; private set => Set(ref _warning, value); }
    public bool HasIncomingDrawOffer
    {
        get => _hasIncomingDrawOffer;
        private set
        {
            if (Set(ref _hasIncomingDrawOffer, value))
            {
                AcceptDrawCommand.NotifyCanExecuteChanged();
                DeclineDrawCommand.NotifyCanExecuteChanged();
                LeaveSpectatorCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public bool IsGameEnded
    {
        get => _isGameEnded;
        private set
        {
            if (Set(ref _isGameEnded, value))
            {
                CoordinateClickedCommand.NotifyCanExecuteChanged();
                ResignCommand.NotifyCanExecuteChanged();
                OfferDrawCommand.NotifyCanExecuteChanged();
                AcceptDrawCommand.NotifyCanExecuteChanged();
                DeclineDrawCommand.NotifyCanExecuteChanged();
                RequestRematchCommand.NotifyCanExecuteChanged();
                AcceptRematchCommand.NotifyCanExecuteChanged();
                DeclineRematchCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public bool HasIncomingRematchOffer
    {
        get => _hasIncomingRematchOffer;
        private set
        {
            if (!Set(ref _hasIncomingRematchOffer, value)) return;
            RequestRematchCommand.NotifyCanExecuteChanged();
            AcceptRematchCommand.NotifyCanExecuteChanged();
            DeclineRematchCommand.NotifyCanExecuteChanged();
        }
    }
    public bool IsRematchPending
    {
        get => _isRematchPending;
        private set
        {
            if (!Set(ref _isRematchPending, value)) return;
            RequestRematchCommand.NotifyCanExecuteChanged();
        }
    }
    public bool IsReplayMode
    {
        get => _isReplayMode;
        private set
        {
            if (!Set(ref _isReplayMode, value)) return;
            Raise(nameof(ViewerLabel));
            Raise(nameof(OwnSideLabel));
            NotifyReplayCommands();
        }
    }
    public string ReplayStepLabel => IsReplayMode ? $"NƯỚC {_replayIndex}/{Math.Max(0, _replayFrames.Count - 1)}" : string.Empty;
    public string ReplayMoveLabel => IsReplayMode && _replayFrames.Count > 0 ? _replayFrames[_replayIndex].Description : string.Empty;
    public string ReplayResult => _replayResult;
    public RelayCommand<Position> CoordinateClickedCommand { get; }
    public RelayCommand FlipBoardCommand { get; }
    public RelayCommand ReplayFirstCommand { get; }
    public RelayCommand ReplayPreviousCommand { get; }
    public RelayCommand ReplayNextCommand { get; }
    public RelayCommand ReplayLastCommand { get; }
    public AsyncRelayCommand ResignCommand { get; }
    public AsyncRelayCommand OfferDrawCommand { get; }
    public AsyncRelayCommand AcceptDrawCommand { get; }
    public AsyncRelayCommand DeclineDrawCommand { get; }
    public AsyncRelayCommand RequestRematchCommand { get; }
    public AsyncRelayCommand AcceptRematchCommand { get; }
    public AsyncRelayCommand DeclineRematchCommand { get; }
    public AsyncRelayCommand LeaveSpectatorCommand { get; }
    public AsyncRelayCommand CancelWaitingRoomCommand { get; }
    public RelayCommand<string> SendQuickChatCommand { get; }
    public AsyncRelayCommand SendChatCommand { get; }
    public RelayCommand ReturnToLobbyCommand { get; }
    public string ChatInput
    {
        get => _chatInput;
        set
        {
            if (!Set(ref _chatInput, value)) return;
            SendChatCommand.NotifyCanExecuteChanged();
        }
    }

    public void LoadDemoData()
    {
        _demoMode = true;
        ApplySnapshot(new GameSnapshot("DEMO-ROOM", 18, SideColor.Red, InitialBoard.Create()));
        Status = "CHẾ ĐỘ DEMO CỤC BỘ — board mẫu 32 quân, không phải state từ Server.";
    }

    public void ClearForLogout() => ResetRoomState("Đã đăng xuất khỏi phòng chơi.");

    private async Task OnCoordinateClickedAsync(Position coordinate)
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
        if (from == coordinate)
        {
            Selected = null;
            Status = "Đã bỏ chọn quân.";
            return;
        }
        var replacement = Pieces.FirstOrDefault(piece =>
            !piece.Captured && piece.Position == coordinate && piece.Side == CurrentTurn);
        if (replacement is not null)
        {
            Selected = coordinate;
            Status = $"Đã chuyển chọn sang {replacement.Type} tại ô {coordinate}.";
            return;
        }
        Selected = null;
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
        if (!snapshot.ViewerRole.Equals("REPLAY", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(snapshot.RoomId, RoomId, StringComparison.Ordinal) && snapshot.Revision < Revision)
        {
            Status = $"Bỏ qua snapshot cũ revision {snapshot.Revision}; hiện tại là {Revision}.";
            return;
        }
        CancelRematchExpiry();
        RoomId = snapshot.RoomId;
        IsWaitingForOpponent = false;
        Revision = snapshot.Revision;
        CurrentTurn = snapshot.CurrentTurn;
        Pieces.Clear();
        foreach (var piece in snapshot.Pieces.Where(p => !p.Captured)) Pieces.Add(piece);
        Selected = null;
        LastFrom = null;
        LastTo = null;
        IsMovePending = false;
        IsGameEnded = !snapshot.Status.Equals("PLAYING", StringComparison.OrdinalIgnoreCase);
        HasIncomingDrawOffer = false;
        HasIncomingRematchOffer = false;
        IsRematchPending = false;
        IsSpectator = snapshot.ViewerRole.Equals("SPECTATOR", StringComparison.OrdinalIgnoreCase) ||
                      snapshot.ViewerRole.Equals("REPLAY", StringComparison.OrdinalIgnoreCase);
        if (!snapshot.ViewerRole.Equals("REPLAY", StringComparison.OrdinalIgnoreCase)) IsReplayMode = false;
        _ownSide = snapshot.ViewerRole.Equals("PLAYER_RED", StringComparison.OrdinalIgnoreCase) ? SideColor.Red
            : snapshot.ViewerRole.Equals("PLAYER_BLACK", StringComparison.OrdinalIgnoreCase) ? SideColor.Black
            : snapshot.ViewerRole.Equals("PLAYER", StringComparison.OrdinalIgnoreCase) && _demoMode ? snapshot.CurrentTurn
            : null;
        if (_ownSide == SideColor.Red) Orientation = BoardOrientation.RedAtBottom;
        else if (_ownSide == SideColor.Black) Orientation = BoardOrientation.BlackAtBottom;
        Raise(nameof(ViewerLabel));
        Raise(nameof(OwnSideLabel));
        Raise(nameof(OwnSideColor));
        Raise(nameof(CanMove));
        CoordinateClickedCommand.NotifyCanExecuteChanged();
        if (snapshot.Clocks is not null) ApplyClock(snapshot.Clocks);
        Warning = snapshot.MustVarySide is null ? null : $"{snapshot.MustVarySide} phải đổi nước.";
        Status = $"Snapshot revision {Revision}; lượt {CurrentTurn}; {Pieces.Count} quân.";
    }

    private void LoadReplay(ReplaySession replay)
    {
        _replayFrames = replay.Frames;
        _replayResult = replay.ResultLabel;
        Raise(nameof(ReplayResult));
        IsReplayMode = true;
        IsSpectator = true;
        IsGameEnded = true;
        IsWaitingForOpponent = false;
        RoomId = replay.RoomId;
        _ownSide = replay.ViewerSide;
        Orientation = replay.ViewerSide == SideColor.Black ? BoardOrientation.BlackAtBottom : BoardOrientation.RedAtBottom;
        Raise(nameof(ViewerLabel));
        Raise(nameof(OwnSideLabel));
        Raise(nameof(OwnSideColor));
        ShowReplayFrame(0);
    }

    private void ShowReplayFrame(int index)
    {
        if (!IsReplayMode || _replayFrames.Count == 0) return;
        _replayIndex = Math.Clamp(index, 0, _replayFrames.Count - 1);
        var frame = _replayFrames[_replayIndex];
        Revision = frame.Revision;
        CurrentTurn = frame.CurrentTurn;
        Pieces.Clear();
        foreach (var piece in frame.Pieces.Where(piece => !piece.Captured)) Pieces.Add(piece);
        Selected = null;
        LastFrom = frame.LastFrom;
        LastTo = frame.LastTo;
        Status = $"Xem lại · {frame.Description}";
        Raise(nameof(ReplayStepLabel));
        Raise(nameof(ReplayMoveLabel));
        NotifyReplayCommands();
    }

    private void NotifyReplayCommands()
    {
        ReplayFirstCommand.NotifyCanExecuteChanged();
        ReplayPreviousCommand.NotifyCanExecuteChanged();
        ReplayNextCommand.NotifyCanExecuteChanged();
        ReplayLastCommand.NotifyCanExecuteChanged();
    }

    private void PrepareForRoom(string roomId)
    {
        IsReplayMode = false;
        var isNewRoom = !string.Equals(RoomId, roomId, StringComparison.Ordinal);
        RoomId = roomId;
        if (!isNewRoom)
        {
            Status = IsWaitingForOpponent
                ? "Đối thủ đã tham gia; đang đồng bộ bàn cờ để bắt đầu."
                : "Đã vào phòng; đang chờ trạng thái bàn cờ từ máy chủ.";
            return;
        }

        CancelRematchExpiry();
        IsWaitingForOpponent = false;
        Revision = 0;
        CurrentTurn = SideColor.Red;
        Pieces.Clear();
        Selected = null;
        LastFrom = null;
        LastTo = null;
        IsMovePending = false;
        IsGameEnded = false;
        IsSpectator = false;
        HasIncomingDrawOffer = false;
        HasIncomingRematchOffer = false;
        IsRematchPending = false;
        Warning = null;
        _ownSide = null;
        _clockBase = null;
        RedRemainingMs = 0;
        BlackRemainingMs = 0;
        IsCheckAlert = false;
        ChatMessages.Clear();
        CoordinateClickedCommand.NotifyCanExecuteChanged();
        Status = "Đã tạo phòng mới; đang tải bàn cờ từ máy chủ.";
    }

    private void PrepareWaitingRoom(WaitingRoomSummary room)
    {
        IsReplayMode = false;
        CancelRematchExpiry();
        RoomId = room.RoomId;
        Revision = 0;
        CurrentTurn = SideColor.Red;
        Pieces.Clear();
        foreach (var piece in InitialBoard.Create()) Pieces.Add(piece);
        Selected = null;
        LastFrom = null;
        LastTo = null;
        IsMovePending = false;
        IsGameEnded = false;
        IsSpectator = false;
        HasIncomingDrawOffer = false;
        HasIncomingRematchOffer = false;
        IsRematchPending = false;
        Warning = null;
        _ownSide = SideColor.Red;
        _clockBase = null;
        RedRemainingMs = 600_000;
        BlackRemainingMs = 600_000;
        Orientation = BoardOrientation.RedAtBottom;
        Raise(nameof(ViewerLabel));
        Raise(nameof(OwnSideLabel));
        Raise(nameof(OwnSideColor));
        IsCheckAlert = false;
        ChatMessages.Clear();
        IsWaitingForOpponent = true;
        Status = $"Phòng {room.ShortRoomId} đã mở. Đang chờ đối thủ tham gia.";
    }

    private void ResetRoomState(string status)
    {
        CancelRematchExpiry();
        RoomId = null;
        Revision = 0;
        Pieces.Clear();
        Selected = null;
        LastFrom = null;
        LastTo = null;
        IsMovePending = false;
        IsGameEnded = false;
        IsSpectator = false;
        IsWaitingForOpponent = false;
        HasIncomingDrawOffer = false;
        HasIncomingRematchOffer = false;
        IsRematchPending = false;
        Warning = null;
        _ownSide = null;
        _clockBase = null;
        RedRemainingMs = 0;
        BlackRemainingMs = 0;
        ChatMessages.Clear();
        ChatInput = "";
        _replayFrames = [];
        _replayIndex = 0;
        _replayResult = "";
        IsReplayMode = false;
        Status = status;
    }

    private async Task SendChatAsync(CancellationToken cancellationToken)
    {
        if (RoomId is null) return;
        var text = ChatInput.Trim();
        if (text.Length is < 1 or > 200)
        {
            Status = "Tin nhắn cần từ 1 đến 200 ký tự.";
            return;
        }
        await _client.SendChatMessageAsync(RoomId, text, cancellationToken);
        ChatInput = "";
    }

    private void ApplyCommittedMove(long revision, MoveDelta delta)
    {
        if (revision <= Revision) return;
        if (revision != Revision + 1)
        {
            _ = ObserveAsync(RequestResyncAsync($"Thiếu event giữa revision {Revision} và {revision}"));
            return;
        }
        var moving = string.IsNullOrWhiteSpace(delta.PieceId)
            ? Pieces.FirstOrDefault(p => p.Position == delta.From)
            : Pieces.FirstOrDefault(p => p.PieceId == delta.PieceId);
        if (moving is null)
        {
            _ = ObserveAsync(RequestResyncAsync($"Không tìm thấy quân {delta.PieceId} trong revision {Revision}"));
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
            _ = ObserveAsync(RequestResyncAsync($"Trạng thái bàn cờ không nhất quán tại revision {Revision}"));
            return;
        }
        Pieces[index] = moving with { Position = delta.To };
        Revision = revision;
        CurrentTurn = delta.CurrentTurn ?? (moving.Side == SideColor.Red ? SideColor.Black : SideColor.Red);
        LastFrom = delta.From;
        LastTo = delta.To;
        IsMovePending = false;
        _audio.PlayMove(delta.PieceId, delta.CapturedPieceId is not null, delta.IsCheck);
        if (delta.IsCheck) _ = ObserveAsync(TriggerCheckAlertAsync(delta.IsCheckmate));
        Status = delta.IsCheckmate ? "CHIẾU BÍ! Ván đấu kết thúc."
            : delta.IsCheck ? $"CHIẾU TƯỚNG! Revision {revision}."
            : $"Server đã commit nước đi; revision {revision}.";
    }

    private void RejectMove(string code, string message, long serverRevision)
    {
        _audio.PlayRejected();
        IsMovePending = false;
        Selected = null;
        Status = $"{code}: {message} (board giữ nguyên, server revision {serverRevision}).";
    }

    private async Task TriggerCheckAlertAsync(bool checkmate)
    {
        var previous = Interlocked.Exchange(ref _checkAlertCts, new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();
        var current = _checkAlertCts!;
        CheckBanner = checkmate ? "CHIẾU BÍ" : "CHIẾU TƯỚNG";
        IsCheckAlert = false;
        IsCheckAlert = true;
        try
        {
            await Task.Delay(checkmate ? 1800 : 1200, current.Token);
            Ui(() => IsCheckAlert = false);
        }
        catch (OperationCanceledException) { }
    }

    private void ApplyClock(ClockSnapshotModel clocks)
    {
        _clockBase = clocks;
        RefreshDisplayedClock();
    }

    private void RefreshDisplayedClock()
    {
        if (_clockBase is null || IsGameEnded) return;
        var elapsed = Math.Max(0, (long)(DateTimeOffset.UtcNow - _clockBase.ServerAnchorUtc).TotalMilliseconds);
        RedRemainingMs = _clockBase.ActiveSide == SideColor.Red
            ? Math.Max(0, _clockBase.RedRemainingMs - elapsed) : _clockBase.RedRemainingMs;
        BlackRemainingMs = _clockBase.ActiveSide == SideColor.Black
            ? Math.Max(0, _clockBase.BlackRemainingMs - elapsed) : _clockBase.BlackRemainingMs;
    }

    private async Task RespondDrawAsync(bool accept, CancellationToken cancellationToken)
    {
        await _client.RespondDrawAsync(RoomId!, accept, cancellationToken);
        HasIncomingDrawOffer = false;
        Status = accept ? "Đã chấp nhận đề nghị hòa." : "Đã từ chối đề nghị hòa.";
    }

    private async Task RequestRematchAsync(CancellationToken cancellationToken)
    {
        if (RoomId is null) return;
        IsRematchPending = true;
        Status = "Đang gửi yêu cầu đấu lại...";
        try { await _client.RequestRematchAsync(RoomId, cancellationToken); }
        catch
        {
            IsRematchPending = false;
            throw;
        }
    }

    private async Task RespondRematchAsync(bool accept, CancellationToken cancellationToken)
    {
        if (RoomId is null) return;
        await _client.RespondRematchAsync(RoomId, accept, cancellationToken);
        CancelRematchExpiry();
        HasIncomingRematchOffer = false;
        IsRematchPending = accept;
        Status = accept
            ? "Đã chấp nhận. Server đang tạo bàn mới và đổi màu quân..."
            : "Bạn đã từ chối đấu lại.";
    }

    private void ScheduleRematchExpiry(DateTimeOffset expiresAt)
    {
        CancelRematchExpiry();
        var cts = new CancellationTokenSource();
        _rematchExpiryCts = cts;
        var roomId = RoomId;
        _ = ExpireRematchUiAsync(roomId, expiresAt, cts);
    }

    private async Task ExpireRematchUiAsync(string? roomId, DateTimeOffset expiresAt, CancellationTokenSource cts)
    {
        var delay = expiresAt - DateTimeOffset.UtcNow;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        try
        {
            await Task.Delay(delay, cts.Token);
            Ui(() =>
            {
                if (!ReferenceEquals(_rematchExpiryCts, cts) || !string.Equals(RoomId, roomId, StringComparison.Ordinal)) return;
                _rematchExpiryCts = null;
                cts.Dispose();
                HasIncomingRematchOffer = false;
                IsRematchPending = false;
                Status = "Yêu cầu đấu lại đã hết hạn. Bạn có thể gửi yêu cầu mới hoặc trở về sảnh.";
            });
        }
        catch (OperationCanceledException) { }
    }

    private void CancelRematchExpiry()
    {
        var previous = Interlocked.Exchange(ref _rematchExpiryCts, null);
        if (previous is null) return;
        previous.Cancel();
        previous.Dispose();
    }

    private async Task ReturnToLobbyAsync()
    {
        var roomId = RoomId;
        try
        {
            if (roomId is not null && HasIncomingRematchOffer)
                await _client.RespondRematchAsync(roomId, false);
            else if (roomId is not null && IsRematchPending)
                await _client.CancelRematchAsync(roomId);
        }
        catch
        {
            // Navigation must remain available; Server also expires stale offers after 60 seconds.
        }
        finally
        {
            CancelRematchExpiry();
            HasIncomingRematchOffer = false;
            IsRematchPending = false;
            ReturnToLobbyRequested?.Invoke();
        }
    }

    private static string FormatClock(long milliseconds)
    {
        var safe = Math.Max(0, milliseconds);
        var totalSeconds = safe / 1000;
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private async Task RequestResyncAsync(string reason)
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

    private async Task ObserveAsync(Task operation)
    {
        try { await operation; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Ui(() => Status = $"Lỗi xử lý giao diện: {ex.Message}"); }
    }

    private static void Ui(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action);
    }
}
