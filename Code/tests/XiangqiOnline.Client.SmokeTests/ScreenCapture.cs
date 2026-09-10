using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UDM18.Client;
using UDM18.Client.Protocol;
using UDM18.Client.ViewModels;

namespace XiangqiOnline.Client.SmokeTests;

public static class ScreenCapture
{
    public static void RunCapture(string outputDir)
    {
        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(outputDir);
                if (Application.Current == null)
                {
                    var app = new App();
                    app.InitializeComponent();
                }

                var transport = new TcpProtocolTransport();
                var client = new GameClient(transport);
                var conn = new ConnectionViewModel(client);
                var lobby = new LobbyViewModel(client);
                var gameRoom = new GameRoomViewModel(client);
                lobby.LoadDemoData();
                gameRoom.LoadDemoData();

                // 1. Account Login View
                var accountView1 = new UDM18.Client.Views.AccountView { DataContext = new AccountPageViewModel(conn) };
                var containerAcc1 = WrapInShell(accountView1, 1200, 750);
                RenderToPng(containerAcc1, 1200, 750, Path.Combine(outputDir, "01_Real_Account_Login.png"));

                // 2. Account Register View
                var accountView2 = new UDM18.Client.Views.AccountView { DataContext = new AccountPageViewModel(conn) };
                var tabCtrl2 = FindVisualChild<System.Windows.Controls.TabControl>(accountView2);
                if (tabCtrl2 != null) tabCtrl2.SelectedIndex = 1;
                var containerAcc2 = WrapInShell(accountView2, 1200, 750);
                RenderToPng(containerAcc2, 1200, 750, Path.Combine(outputDir, "02_Real_Register_Screen.png"));

                // 3. Account Forgot Password (OTP) View
                var accountView3 = new UDM18.Client.Views.AccountView { DataContext = new AccountPageViewModel(conn) };
                var tabCtrl3 = FindVisualChild<System.Windows.Controls.TabControl>(accountView3);
                if (tabCtrl3 != null) tabCtrl3.SelectedIndex = 2;
                var containerAcc3 = WrapInShell(accountView3, 1200, 750);
                RenderToPng(containerAcc3, 1200, 750, Path.Combine(outputDir, "03_Real_ForgotPassword_Screen.png"));

                // 4. Lobby View
                var lobbyView = new UDM18.Client.Views.LobbyView { DataContext = lobby };
                var containerLobby = WrapInShell(lobbyView, 1200, 750);
                RenderToPng(containerLobby, 1200, 750, Path.Combine(outputDir, "04_Real_Lobby_Rooms.png"));

                // 5. Game Room View (Full Chess Board)
                var gameView = new UDM18.Client.Views.GameRoomView { DataContext = gameRoom };
                var containerGame = WrapInShell(gameView, 1280, 820);
                RenderToPng(containerGame, 1280, 820, Path.Combine(outputDir, "05_Real_GameRoom_ChessBoard.png"));

                Console.WriteLine("CAPTURE_SUCCESS: Rendered all real UI screens to " + outputDir);
            }
            catch (Exception ex)
            {
                Console.WriteLine("CAPTURE_ERROR: " + ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;
            var desc = FindVisualChild<T>(child);
            if (desc != null) return desc;
        }
        return null;
    }

    private static FrameworkElement WrapInShell(FrameworkElement content, double width, double height)
    {
        var border = new System.Windows.Controls.Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Color.FromRgb(0x15, 0x12, 0x0F)),
            Padding = new Thickness(20),
            Child = content
        };
        border.Measure(new Size(width, height));
        border.Arrange(new Rect(0, 0, width, height));
        border.UpdateLayout();

        // Pump dispatcher
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);

        border.Measure(new Size(width, height));
        border.Arrange(new Rect(0, 0, width, height));
        border.UpdateLayout();
        return border;
    }

    private static void RenderToPng(FrameworkElement element, int width, int height, string filePath)
    {
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }
}
