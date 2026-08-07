namespace XiangqiOnline.Shared.Models;

/// <summary>
/// Danh mục mã lỗi chuẩn UDM18 phục vụ kiểm tra hợp lệ nước đi.
/// </summary>
public static class ErrorCodes
{
    public const string OK = "OK";

    // Lỗi tọa độ & tham số nước đi
    public const string INVALID_COORDINATE = "ERR_INVALID_COORDINATE";
    public const string NO_PIECE_AT_SOURCE = "ERR_NO_PIECE_AT_SOURCE";
    public const string NOT_YOUR_PIECE = "ERR_NOT_YOUR_PIECE";
    public const string SAME_DESTINATION = "ERR_SAME_DESTINATION";
    public const string DESTINATION_OCCUPIED_BY_FRIEND = "ERR_DESTINATION_OCCUPIED_BY_FRIEND";
    public const string NOT_YOUR_TURN = "ERR_NOT_YOUR_TURN";

    // Lỗi di chuyển theo hình học từng loại quân
    public const string ILLEGAL_PIECE_MOVE = "ERR_ILLEGAL_PIECE_MOVE";
    public const string OUT_OF_PALACE = "ERR_OUT_OF_PALACE";
    public const string CANNOT_CROSS_RIVER = "ERR_CANNOT_CROSS_RIVER";
    public const string ELEPHANT_EYE_BLOCKED = "ERR_ELEPHANT_EYE_BLOCKED";
    public const string HORSE_FOOT_BLOCKED = "ERR_HORSE_FOOT_BLOCKED";
    public const string PATH_BLOCKED = "ERR_PATH_BLOCKED";
    public const string CANNON_MOUNT_INVALID = "ERR_CANNON_MOUNT_INVALID";
    public const string PAWN_CANNOT_MOVE_BACKWARD = "ERR_PAWN_CANNOT_MOVE_BACKWARD";

    // Lỗi an toàn Tướng & trạng thái bàn cờ (TV4 bổ sung sâu ở Phase 2)
    public const string SELF_CHECK = "ERR_SELF_CHECK";
    public const string GENERALS_FACING = "ERR_GENERALS_FACING";
}
