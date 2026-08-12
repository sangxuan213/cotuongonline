namespace XiangqiOnline.Shared.Models;

/// <summary>
/// Danh mục mã lỗi chuẩn UDM18 phục vụ kiểm tra hợp lệ nước đi.
/// Không dùng tiền tố "ERR_".
/// </summary>
public static class ErrorCodes
{
    public const string OK = "OK";

    public const string NOT_YOUR_TURN = "NOT_YOUR_TURN";
    public const string NO_PIECE_AT_SOURCE = "NO_PIECE_AT_SOURCE";
    public const string NOT_YOUR_PIECE = "NOT_YOUR_PIECE";
    public const string ALLY_AT_DESTINATION = "ALLY_AT_DESTINATION";
    public const string OUT_OF_BOARD = "OUT_OF_BOARD";
    public const string INVALID_GEOMETRY = "INVALID_GEOMETRY";
    public const string PATH_BLOCKED = "PATH_BLOCKED";
    public const string HORSE_LEG_BLOCKED = "HORSE_LEG_BLOCKED";
    public const string ELEPHANT_EYE_BLOCKED = "ELEPHANT_EYE_BLOCKED";
    public const string ELEPHANT_CROSSES_RIVER = "ELEPHANT_CROSSES_RIVER";
    public const string OUTSIDE_PALACE = "OUTSIDE_PALACE";
    public const string CANNON_SCREEN_INVALID = "CANNON_SCREEN_INVALID";
    public const string PAWN_RETREATS = "PAWN_RETREATS";

    // Contract codes (implemented by TV4 in check/self-check/game state phase)
    public const string GENERALS_FACING = "GENERALS_FACING";
    public const string SELF_CHECK = "SELF_CHECK";
    public const string CHECK_NOT_RESOLVED = "CHECK_NOT_RESOLVED";
    public const string GAME_NOT_ACTIVE = "GAME_NOT_ACTIVE";
    public const string TIME_EXPIRED = "TIME_EXPIRED";
    public const string INTERNAL_SERVER_ERROR = "INTERNAL_SERVER_ERROR";
    public const string DISPLAY_NAME_INVALID = "DISPLAY_NAME_INVALID";
    public const string DISPLAY_NAME_TAKEN = "DISPLAY_NAME_TAKEN";
    public const string INVALID_SESSION = "INVALID_SESSION";
    public const string DUPLICATE_SESSION = "DUPLICATE_SESSION";
    public const string PLAYER_NOT_AVAILABLE = "PLAYER_NOT_AVAILABLE";
    public const string CHALLENGE_NOT_FOUND = "CHALLENGE_NOT_FOUND";
    public const string CHALLENGE_NOT_PENDING = "CHALLENGE_NOT_PENDING";
}
