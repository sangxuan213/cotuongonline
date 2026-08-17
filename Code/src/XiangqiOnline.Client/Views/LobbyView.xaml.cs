using System.Windows.Controls;
using System.Windows;
using UDM18.Client.Models;
using UDM18.Client.ViewModels;
namespace UDM18.Client.Views;

public partial class LobbyView : UserControl
{
    public LobbyView() => InitializeComponent();

    private void WatchMatchButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LobbyViewModel viewModel || sender is not Button { Tag: ActiveMatchSummary match }) return;
        if (viewModel.WatchMatchCommand.CanExecute(match)) viewModel.WatchMatchCommand.Execute(match);
    }
}
