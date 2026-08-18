using System.Text;

namespace XiangqiOnline.Server;

public static class ServerConsoleLog
{
    private static readonly object Gate = new();

    public static void Initialize(string address, int port)
    {
        lock (Gate)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = $"UDM18 • NHẬT KÝ MÁY CHỦ • {address}:{port}";
            if (Console.IsOutputRedirected)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [HỆ THỐNG] UDM18 Xiangqi Server • {address}:{port} • .NET 10");
                return;
            }
            WriteColor(ConsoleColor.DarkRed, "╔══════════════════════════════════════════════════════════════╗\n");
            WriteColor(ConsoleColor.DarkRed, "║");
            WriteColor(ConsoleColor.White, "          UDM18  •  XIANGQI ONLINE SERVER LOG              ");
            WriteColor(ConsoleColor.DarkRed, "║\n");
            WriteColor(ConsoleColor.DarkRed, "╠══════════════════════════════════════════════════════════════╣\n");
            WriteColor(ConsoleColor.DarkRed, "║");
            WriteColor(ConsoleColor.Yellow, $"  Địa chỉ: {address,-15}  Cổng: {port,-5}  .NET 10           ");
            WriteColor(ConsoleColor.DarkRed, "║\n");
            WriteColor(ConsoleColor.DarkRed, "╚══════════════════════════════════════════════════════════════╝\n");
            Console.WriteLine("  Thời gian     Nhóm       Nội dung");
            Console.WriteLine("  ────────────  ─────────  ───────────────────────────────────");
        }
    }

    public static void Info(string category, string message) => Write(ConsoleColor.Cyan, category, message);
    public static void Success(string category, string message) => Write(ConsoleColor.Green, category, message);
    public static void Warning(string category, string message) => Write(ConsoleColor.Yellow, category, message);
    public static void Error(string category, string message) => Write(ConsoleColor.Red, category, message, true);

    private static void Write(ConsoleColor color, string category, string message, bool error = false)
    {
        lock (Gate)
        {
            var output = error ? Console.Error : Console.Out;
            if (Console.IsOutputRedirected || Console.IsErrorRedirected)
            {
                output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {message}");
                return;
            }

            var previous = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            output.Write($"  {DateTime.Now:HH:mm:ss}  ");
            Console.ForegroundColor = color;
            output.Write($"{category.ToUpperInvariant(),-9}  ");
            Console.ForegroundColor = previous;
            output.WriteLine(message);
        }
    }

    private static void WriteColor(ConsoleColor color, string value)
    {
        var previous = Console.ForegroundColor;
        if (!Console.IsOutputRedirected) Console.ForegroundColor = color;
        Console.Write(value);
        if (!Console.IsOutputRedirected) Console.ForegroundColor = previous;
    }
}
