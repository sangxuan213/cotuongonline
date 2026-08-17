namespace UDM18.Client.ViewModels;

public sealed class AccountPageViewModel
{
    public AccountPageViewModel(ConnectionViewModel connection) => Connection = connection;
    public ConnectionViewModel Connection { get; }
}
