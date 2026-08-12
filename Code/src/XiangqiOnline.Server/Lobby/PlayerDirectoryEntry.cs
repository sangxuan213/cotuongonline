namespace XiangqiOnline.Server.Lobby;

public sealed record PlayerDirectoryEntry(
    string PlayerId,
    string DisplayName,
    PlayerStatus Status,
    PlayerSessionConnectionState ConnectionState);
