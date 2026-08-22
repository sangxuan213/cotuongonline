using System.Diagnostics.CodeAnalysis;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using System.Security.Cryptography;
using System.Text;

namespace XiangqiOnline.Server.Lobby;

public sealed class ChallengeManager
{
    public const string DefaultRuleProfileId = "UDM18_WXF_PRO_2018";

    private readonly PlayerSessionDirectory _players;
    private readonly Func<string> _challengeIdFactory;
    private readonly Func<string> _roomIdFactory;
    private readonly Dictionary<string, Challenge> _challengesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GameRoom> _roomsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WaitingRoom> _waitingRoomsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RematchOffer> _rematchesByOriginalRoomId = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public event Action<GameRoom>? RoomCreated;

    public ChallengeManager(
        PlayerSessionDirectory players,
        Func<string>? challengeIdFactory = null,
        Func<string>? roomIdFactory = null)
    {
        _players = players;
        _challengeIdFactory = challengeIdFactory ?? (() => Guid.NewGuid().ToString("N"));
        _roomIdFactory = roomIdFactory ?? (() => Guid.NewGuid().ToString("N"));
    }

    public ChallengeActionResult SendChallenge(
        string challengerPlayerId,
        string targetPlayerId,
        string timeProfile,
        DateTimeOffset nowUtc,
        TimeSpan lifetime)
    {
        if (challengerPlayerId == targetPlayerId)
            return ChallengeActionResult.Fail(ErrorCodes.PLAYER_NOT_AVAILABLE, "A player cannot challenge themselves.");
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentException("Challenge lifetime must be positive.", nameof(lifetime));

        lock (_gate)
        {
            if (!TryGetAvailablePlayer(challengerPlayerId, out var challenger) ||
                !TryGetAvailablePlayer(targetPlayerId, out var target))
            {
                return ChallengeActionResult.Fail(ErrorCodes.PLAYER_NOT_AVAILABLE, "Both players must be available.");
            }

            var challenge = new Challenge(
                _challengeIdFactory(),
                challenger.PlayerId,
                target.PlayerId,
                timeProfile,
                nowUtc,
                nowUtc.Add(lifetime));

            _players.MarkInviting(challenger.PlayerId, challenge.ChallengeId);
            _players.MarkInvited(target.PlayerId, challenge.ChallengeId);
            _challengesById.Add(challenge.ChallengeId, challenge);

            return ChallengeActionResult.Sent(challenge);
        }
    }

    public ChallengeActionResult AcceptChallenge(string challengeId, string acceptingPlayerId, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            if (!_challengesById.TryGetValue(challengeId, out var challenge))
                return ChallengeActionResult.Fail(ErrorCodes.CHALLENGE_NOT_FOUND, "Challenge was not found.");
            if (!challenge.IsPending)
                return ChallengeActionResult.Fail(ErrorCodes.CHALLENGE_NOT_PENDING, "Challenge is no longer pending.");
            if (nowUtc >= challenge.ExpiresAtUtc)
            {
                challenge.Expire(nowUtc);
                _players.ClearChallenge(challenge.ChallengerPlayerId);
                _players.ClearChallenge(challenge.TargetPlayerId);
                return ChallengeActionResult.Fail(ErrorCodes.CHALLENGE_EXPIRED, "Challenge expired before it could be accepted.");
            }
            if (acceptingPlayerId != challenge.TargetPlayerId)
                return ChallengeActionResult.Fail(ErrorCodes.CHALLENGE_UNAUTHORIZED, "Only the challenged player can accept this challenge.");

            challenge.Accept(acceptingPlayerId, nowUtc);

            var board = BoardState.CreateInitialBoard(SideColor.Red);
            var room = new GameRoom(
                _roomIdFactory(),
                challenge.ChallengerPlayerId,
                challenge.TargetPlayerId,
                DefaultRuleProfileId,
                challenge.TimeProfile,
                nowUtc,
                board);
            room.Start();

            _players.EnterRoom(room.RedPlayerId, room.RoomId);
            _players.EnterRoom(room.BlackPlayerId, room.RoomId);
            _roomsById.Add(room.RoomId, room);
            RoomCreated?.Invoke(room);

            return ChallengeActionResult.Accepted(challenge, room);
        }
    }

    public ChallengeActionResult RejectChallenge(string challengeId, string rejectingPlayerId, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            if (!_challengesById.TryGetValue(challengeId, out var challenge))
                return ChallengeActionResult.Fail(ErrorCodes.CHALLENGE_NOT_FOUND, "Challenge was not found.");
            if (!challenge.IsPending)
                return ChallengeActionResult.Fail(ErrorCodes.CHALLENGE_NOT_PENDING, "Challenge is no longer pending.");
            if (nowUtc >= challenge.ExpiresAtUtc)
            {
                challenge.Expire(nowUtc);
                _players.ClearChallenge(challenge.ChallengerPlayerId);
                _players.ClearChallenge(challenge.TargetPlayerId);
                return ChallengeActionResult.Fail(ErrorCodes.CHALLENGE_EXPIRED, "Challenge expired before it could be rejected.");
            }
            if (rejectingPlayerId != challenge.TargetPlayerId)
                return ChallengeActionResult.Fail(ErrorCodes.CHALLENGE_UNAUTHORIZED, "Only the challenged player can reject this challenge.");

            challenge.Reject(rejectingPlayerId);

            _players.ClearChallenge(challenge.ChallengerPlayerId);
            _players.ClearChallenge(challenge.TargetPlayerId);

            return ChallengeActionResult.Rejected(challenge);
        }
    }

    public ChallengeActionResult CancelChallenge(string challengeId, string cancellingPlayerId)
    {
        lock (_gate)
        {
            if (!_challengesById.TryGetValue(challengeId, out var challenge))
                return ChallengeActionResult.Fail(ErrorCodes.CHALLENGE_NOT_FOUND, "Challenge was not found.");
            if (!challenge.IsPending)
                return ChallengeActionResult.Fail(ErrorCodes.CHALLENGE_NOT_PENDING, "Challenge is no longer pending.");

            try
            {
                challenge.Cancel(cancellingPlayerId);
            }
            catch (InvalidOperationException)
            {
                return ChallengeActionResult.Fail(ErrorCodes.CHALLENGE_UNAUTHORIZED, "Only the challenger can cancel this challenge.");
            }

            _players.ClearChallenge(challenge.ChallengerPlayerId);
            _players.ClearChallenge(challenge.TargetPlayerId);

            return ChallengeActionResult.Cancelled(challenge);
        }
    }

    public void ExpireOverdueChallenges(DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            foreach (var challenge in _challengesById.Values.Where(c => c.IsPending).ToArray())
            {
                if (challenge.Expire(nowUtc))
                {
                    _players.ClearChallenge(challenge.ChallengerPlayerId);
                    _players.ClearChallenge(challenge.TargetPlayerId);
                }
            }
        }
    }

    public bool TryGetChallenge(string challengeId, [MaybeNullWhen(false)] out Challenge challenge)
    {
        lock (_gate)
        {
            return _challengesById.TryGetValue(challengeId, out challenge!);
        }
    }

    public bool TryGetRoom(string roomId, [MaybeNullWhen(false)] out GameRoom room)
    {
        lock (_gate)
        {
            return _roomsById.TryGetValue(roomId, out room!);
        }
    }

    public bool TryCreateBotRoom(
        string playerId,
        string difficulty,
        DateTimeOffset nowUtc,
        out GameRoom room,
        out string error)
    {
        room = null!;
        error = string.Empty;
        lock (_gate)
        {
            if (!TryGetAvailablePlayer(playerId, out _))
            {
                error = "Player must be available before starting a bot game.";
                return false;
            }
            var roomId = _roomIdFactory();
            room = new GameRoom(
                roomId,
                playerId,
                $"BOT_{difficulty.ToUpperInvariant()}_{roomId[..Math.Min(8, roomId.Length)]}",
                DefaultRuleProfileId,
                "10+0",
                nowUtc,
                BoardState.CreateInitialBoard(SideColor.Red));
            room.Start();
            _players.EnterRoom(playerId, room.RoomId);
            _roomsById.Add(room.RoomId, room);
            RoomCreated?.Invoke(room);
            return true;
        }
    }

    public bool TryCreateWaitingRoom(
        string playerId,
        DateTimeOffset nowUtc,
        out WaitingRoom waitingRoom,
        out string error)
        => TryCreateWaitingRoom(playerId, null, nowUtc, out waitingRoom, out error);

    public bool TryCreateWaitingRoom(
        string playerId,
        string? password,
        DateTimeOffset nowUtc,
        out WaitingRoom waitingRoom,
        out string error)
    {
        waitingRoom = null!;
        error = string.Empty;
        lock (_gate)
        {
            if (!TryGetAvailablePlayer(playerId, out var owner))
            {
                error = "Player must be available before creating a room.";
                return false;
            }

            var existing = _waitingRoomsById.Values.FirstOrDefault(room => room.OwnerPlayerId == playerId);
            if (existing is not null)
            {
                waitingRoom = existing;
                error = "Player already owns a waiting room.";
                return false;
            }

            var roomId = _roomIdFactory();
            waitingRoom = new WaitingRoom(roomId, owner.PlayerId, owner.DisplayName, "10+0", nowUtc)
            {
                PasswordHash = string.IsNullOrWhiteSpace(password) ? null : HashRoomPassword(roomId, password)
            };
            _players.MarkInviting(owner.PlayerId, $"ROOM_{roomId}");
            _waitingRoomsById.Add(roomId, waitingRoom);
            return true;
        }
    }

    public bool TryJoinWaitingRoom(
        string roomId,
        string joiningPlayerId,
        DateTimeOffset nowUtc,
        out GameRoom room,
        out string error)
        => TryJoinWaitingRoom(roomId, joiningPlayerId, null, nowUtc, out room, out error);

    public bool TryJoinWaitingRoom(
        string roomId,
        string joiningPlayerId,
        string? password,
        DateTimeOffset nowUtc,
        out GameRoom room,
        out string error)
    {
        room = null!;
        error = string.Empty;
        lock (_gate)
        {
            if (!_waitingRoomsById.TryGetValue(roomId, out var waiting))
            {
                error = "Waiting room was not found.";
                return false;
            }
            if (waiting.OwnerPlayerId == joiningPlayerId)
            {
                error = "The room owner cannot join their own room.";
                return false;
            }
            if (waiting.IsLocked && !PasswordMatches(waiting, password))
            {
                error = "Mật khẩu phòng không đúng.";
                return false;
            }
            var ownerFound = _players.TryGetByPlayerId(waiting.OwnerPlayerId, out var owner);
            if (!TryGetAvailablePlayer(joiningPlayerId, out _) ||
                !ownerFound || owner.Status != PlayerStatus.INVITING ||
                owner.ConnectionState != PlayerSessionConnectionState.CONNECTED)
            {
                if (ownerFound) _players.ClearChallenge(waiting.OwnerPlayerId);
                _waitingRoomsById.Remove(roomId);
                error = "One of the players is no longer available.";
                return false;
            }

            room = new GameRoom(
                roomId,
                waiting.OwnerPlayerId,
                joiningPlayerId,
                DefaultRuleProfileId,
                waiting.TimeProfile,
                nowUtc,
                BoardState.CreateInitialBoard(SideColor.Red));
            room.Start();
            _players.ClearChallenge(waiting.OwnerPlayerId);
            _players.EnterRoom(room.RedPlayerId, room.RoomId);
            _players.EnterRoom(room.BlackPlayerId, room.RoomId);
            _waitingRoomsById.Remove(roomId);
            _roomsById.Add(room.RoomId, room);
            RoomCreated?.Invoke(room);
            return true;
        }
    }

    public IReadOnlyList<WaitingRoom> GetWaitingRoomsSnapshot()
    {
        lock (_gate)
        {
            return _waitingRoomsById.Values.OrderBy(room => room.CreatedAtUtc).ToArray();
        }
    }

    private static string HashRoomPassword(string roomId, string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{roomId}:{password}"));
        return Convert.ToHexString(bytes);
    }

    private static bool PasswordMatches(WaitingRoom room, string? password)
    {
        if (room.PasswordHash is null) return true;
        if (string.IsNullOrEmpty(password)) return false;
        var expected = Convert.FromHexString(room.PasswordHash);
        var actual = Convert.FromHexString(HashRoomPassword(room.RoomId, password));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public bool RemoveWaitingRoomForPlayer(string playerId)
    {
        lock (_gate)
        {
            var room = _waitingRoomsById.Values.FirstOrDefault(candidate => candidate.OwnerPlayerId == playerId);
            if (room is null) return false;
            _waitingRoomsById.Remove(room.RoomId);
            _players.ClearChallenge(playerId);
            return true;
        }
    }

    public IReadOnlyList<GameRoom> GetRoomsSnapshot(bool activeOnly = false)
    {
        lock (_gate)
        {
            return _roomsById.Values
                .Where(room => !activeOnly || !room.IsTerminal)
                .OrderBy(room => room.CreatedAtUtc)
                .ToArray();
        }
    }

    public bool TryRequestRematch(
        string originalRoomId,
        string requesterPlayerId,
        DateTimeOffset nowUtc,
        TimeSpan lifetime,
        out RematchOffer offer,
        out string error)
    {
        offer = null!;
        error = string.Empty;
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));

        lock (_gate)
        {
            if (!_roomsById.TryGetValue(originalRoomId, out var room) || !room.IsTerminal)
            {
                error = "Chỉ có thể yêu cầu đấu lại sau khi ván đấu kết thúc.";
                return false;
            }
            if (!room.HasPlayer(requesterPlayerId))
            {
                error = "Chỉ người chơi trong ván mới có thể yêu cầu đấu lại.";
                return false;
            }
            if (room.BlackPlayerId.StartsWith("BOT_", StringComparison.Ordinal))
            {
                error = "Ván đấu với máy không cần gửi yêu cầu đấu lại.";
                return false;
            }
            if (_rematchesByOriginalRoomId.TryGetValue(originalRoomId, out var existing))
            {
                if (nowUtc < existing.ExpiresAtUtc)
                {
                    error = "Phòng này đã có một yêu cầu đấu lại đang chờ phản hồi.";
                    return false;
                }
                ReleaseRematchPlayers(existing);
                _rematchesByOriginalRoomId.Remove(originalRoomId);
            }

            var opponentId = requesterPlayerId == room.RedPlayerId ? room.BlackPlayerId : room.RedPlayerId;
            if (!TryGetAvailablePlayer(requesterPlayerId, out _) || !TryGetAvailablePlayer(opponentId, out _))
            {
                error = "Đối thủ đã rời mạng hoặc một trong hai người đang ở phòng khác.";
                return false;
            }

            offer = new RematchOffer(originalRoomId, requesterPlayerId, opponentId, nowUtc, nowUtc.Add(lifetime));
            _players.MarkInviting(requesterPlayerId, $"REMATCH_{originalRoomId}");
            _players.MarkInvited(opponentId, $"REMATCH_{originalRoomId}");
            _rematchesByOriginalRoomId.Add(originalRoomId, offer);
            return true;
        }
    }

    public bool TryRespondToRematch(
        string originalRoomId,
        string respondingPlayerId,
        bool accept,
        DateTimeOffset nowUtc,
        out RematchOffer offer,
        out GameRoom? newRoom,
        out string error)
    {
        offer = null!;
        newRoom = null;
        error = string.Empty;
        lock (_gate)
        {
            if (!_rematchesByOriginalRoomId.TryGetValue(originalRoomId, out offer!))
            {
                error = "Không còn yêu cầu đấu lại nào đang chờ.";
                return false;
            }
            if (respondingPlayerId != offer.TargetPlayerId)
            {
                error = "Chỉ đối thủ được quyền phản hồi yêu cầu đấu lại.";
                return false;
            }
            if (nowUtc >= offer.ExpiresAtUtc)
            {
                ReleaseRematchPlayers(offer);
                _rematchesByOriginalRoomId.Remove(originalRoomId);
                error = "Yêu cầu đấu lại đã hết hạn.";
                return false;
            }

            _rematchesByOriginalRoomId.Remove(originalRoomId);
            ReleaseRematchPlayers(offer);
            if (!accept) return true;

            if (!_roomsById.TryGetValue(originalRoomId, out var original) ||
                !TryGetAvailablePlayer(original.RedPlayerId, out _) ||
                !TryGetAvailablePlayer(original.BlackPlayerId, out _))
            {
                error = "Không thể bắt đầu vì một người chơi đã rời mạng hoặc vào phòng khác.";
                return false;
            }

            newRoom = new GameRoom(
                _roomIdFactory(),
                original.BlackPlayerId,
                original.RedPlayerId,
                original.RuleProfileId,
                original.TimeProfile,
                nowUtc,
                BoardState.CreateInitialBoard(SideColor.Red));
            newRoom.Start();
            _players.EnterRoom(newRoom.RedPlayerId, newRoom.RoomId);
            _players.EnterRoom(newRoom.BlackPlayerId, newRoom.RoomId);
            _roomsById.Add(newRoom.RoomId, newRoom);
            RoomCreated?.Invoke(newRoom);
            return true;
        }
    }

    public bool TryCancelRematch(
        string originalRoomId,
        string playerId,
        out RematchOffer offer,
        out string error)
    {
        offer = null!;
        error = string.Empty;
        lock (_gate)
        {
            if (!_rematchesByOriginalRoomId.TryGetValue(originalRoomId, out offer!))
            {
                error = "Không còn yêu cầu đấu lại nào đang chờ.";
                return false;
            }
            if (offer.RequesterPlayerId != playerId)
            {
                error = "Chỉ người gửi yêu cầu mới có thể hủy.";
                return false;
            }
            _rematchesByOriginalRoomId.Remove(originalRoomId);
            ReleaseRematchPlayers(offer);
            return true;
        }
    }

    public void RemoveConnectionFromSpectators(string connectionId)
    {
        lock (_gate)
        {
            foreach (var room in _roomsById.Values)
                room.RemoveSpectator(connectionId);
        }
    }

    public void PruneTerminalState(DateTimeOffset nowUtc, TimeSpan retention)
    {
        lock (_gate)
        {
            foreach (var id in _roomsById.Where(pair => pair.Value.IsTerminal &&
                         pair.Value.Result is { } result && nowUtc - result.EndedAtUtc >= retention)
                     .Select(pair => pair.Key).ToArray())
                _roomsById.Remove(id);
            foreach (var id in _challengesById.Where(pair => !pair.Value.IsPending &&
                         nowUtc - pair.Value.ExpiresAtUtc >= retention)
                     .Select(pair => pair.Key).ToArray())
                _challengesById.Remove(id);
            foreach (var pair in _rematchesByOriginalRoomId.Where(pair => nowUtc >= pair.Value.ExpiresAtUtc).ToArray())
            {
                ReleaseRematchPlayers(pair.Value);
                _rematchesByOriginalRoomId.Remove(pair.Key);
            }
        }
    }

    private void ReleaseRematchPlayers(RematchOffer offer)
    {
        _players.ClearChallenge(offer.RequesterPlayerId);
        _players.ClearChallenge(offer.TargetPlayerId);
    }

    private bool TryGetAvailablePlayer(string playerId, [MaybeNullWhen(false)] out PlayerSession session)
    {
        if (_players.TryGetByPlayerId(playerId, out session!) && session.CanReceiveChallenge)
            return true;

        session = null;
        return false;
    }
}

public sealed record RematchOffer(
    string OriginalRoomId,
    string RequesterPlayerId,
    string TargetPlayerId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset ExpiresAtUtc);
