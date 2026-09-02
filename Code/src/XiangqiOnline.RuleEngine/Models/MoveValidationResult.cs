using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Models;

/// <summary>
/// Kết quả kiểm tra tính hợp lệ của một nước đi.
/// </summary>
public record MoveValidationResult(
    bool IsValid,
    string ErrorCode,
    string Message,
    bool IsCheck = false,
    bool IsCheckmate = false
)
{
    public static MoveValidationResult Success(bool isCheck = false, bool isCheckmate = false)
        => new(true, ErrorCodes.OK, "Nước đi hợp lệ.", isCheck, isCheckmate);

    public static MoveValidationResult Fail(string errorCode, string message)
        => new(false, errorCode, message);
}
