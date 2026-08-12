using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Models;

/// <summary>
/// Kết quả kiểm tra tính hợp lệ của một nước đi.
/// </summary>
public record MoveValidationResult(
    bool IsValid,
    string ErrorCode,
    string Message
)
{
    public static MoveValidationResult Success() 
        => new(true, ErrorCodes.OK, "Nước đi hợp lệ.");

    public static MoveValidationResult Fail(string errorCode, string message) 
        => new(false, errorCode, message);
}
