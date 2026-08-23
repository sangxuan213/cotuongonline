namespace XiangqiOnline.Server.Lobby;

public sealed record WaitingRoom(
    string RoomId,
    string OwnerPlayerId,
    string OwnerDisplayName,
    string TimeProfile,
    DateTimeOffset CreatedAtUtc)
{
    public string? PasswordHash { get; init; }
    public bool IsLocked => !string.IsNullOrWhiteSpace(PasswordHash);
}
