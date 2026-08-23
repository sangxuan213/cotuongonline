using Microsoft.Extensions.Logging;

namespace XiangqiOnline.Persistence.Logging;

/// <summary>
/// Hỗ trợ correlation id cho structured logging. Tạo scope để mọi log trong scope
/// đều gắn CorrelationId.
/// </summary>
public static class CorrelationContext
{
    /// <summary>
    /// Bọc một khối xử lý bằng scope với CorrelationId.
    /// </summary>
    public static IDisposable BeginScope(ILogger logger, string correlationId)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        })!;
    }

    /// <summary>Sinh correlation id mới.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");
}
