namespace XiangqiOnline.ServerAdmin;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ServerDashboard(args));
    }
}
