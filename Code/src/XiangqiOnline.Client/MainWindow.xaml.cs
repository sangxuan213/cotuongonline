using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace UDM18.Client;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => EnableDarkTitleBar();
    }

    private void EnableDarkTitleBar()
    {
        var enabled = 1;
        var handle = new WindowInteropHelper(this).Handle;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
