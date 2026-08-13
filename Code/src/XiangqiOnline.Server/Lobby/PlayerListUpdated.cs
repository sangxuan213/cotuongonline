namespace XiangqiOnline.Server.Lobby;

public sealed record PlayerListUpdated(
    string ChangedPlayerId,
    string Reason,
    IReadOnlyList<PlayerDirectoryEntry> Players);
