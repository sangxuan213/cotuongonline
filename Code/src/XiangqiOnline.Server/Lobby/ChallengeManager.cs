using System.Diagnostics.CodeAnalysis;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Server.Lobby;

public sealed class ChallengeManager
{
    public const string DefaultRuleProfileId = "UDM18_WXF_PRO_2018";

    private readonly PlayerSessionDirectory _players;
    private readonly Func<string> _challengeIdFactory;
    private readonly Func<string> _roomIdFactory;
    private readonly Dictionary<string, Challenge> _challengesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GameRoom> _roomsById = new(StringComparer.Ordinal);
    private readonly object _gate = new();

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

    private bool TryGetAvailablePlayer(string playerId, [MaybeNullWhen(false)] out PlayerSession session)
    {
        if (_players.TryGetByPlayerId(playerId, out session!) && session.CanReceiveChallenge)
            return true;

        session = null;
        return false;
    }
}
