namespace UDM18.Client.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private object _currentPage;
    private bool _isAuthenticated;
    public ShellViewModel(ConnectionViewModel connection, LobbyViewModel lobby, GameRoomViewModel gameRoom, bool demoMode = false)
    {
        Connection = connection;
        Account = new AccountPageViewModel(connection);
        Lobby = lobby;
        GameRoom = gameRoom;
        IsDemoMode = demoMode;
        _isAuthenticated = demoMode;
        _currentPage = demoMode ? gameRoom : Account;
        ShowAccountCommand = new RelayCommand(() => CurrentPage = Account);
        ShowConnectionCommand = new RelayCommand(() => CurrentPage = Connection, () => IsAuthenticated);
        ShowLobbyCommand = new RelayCommand(() => CurrentPage = Lobby, () => IsAuthenticated);
        ShowGameRoomCommand = new RelayCommand(() => CurrentPage = GameRoom, () => IsAuthenticated);
        connection.LoginSucceeded += () =>
        {
            IsAuthenticated = true;
            CurrentPage = Lobby;
        };
        connection.ReconnectSucceeded += () => CurrentPage = GameRoom;
        lobby.OpenGameRequested += () => CurrentPage = GameRoom;
        lobby.LogoutRequested += () =>
        {
            gameRoom.ClearForLogout();
            IsAuthenticated = false;
            CurrentPage = Account;
        };
        gameRoom.ReturnToLobbyRequested += () => CurrentPage = Lobby;
    }

    public ConnectionViewModel Connection { get; }
    public AccountPageViewModel Account { get; }
    public LobbyViewModel Lobby { get; }
    public GameRoomViewModel GameRoom { get; }
    public bool IsDemoMode { get; }
    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set
        {
            if (!Set(ref _isAuthenticated, value)) return;
            Raise(nameof(SidebarWidth));
            ShowConnectionCommand.NotifyCanExecuteChanged();
            ShowLobbyCommand.NotifyCanExecuteChanged();
            ShowGameRoomCommand.NotifyCanExecuteChanged();
        }
    }
    public System.Windows.GridLength SidebarWidth => IsAuthenticated
        ? new System.Windows.GridLength(220)
        : new System.Windows.GridLength(0);
    public object CurrentPage { get => _currentPage; private set => Set(ref _currentPage, value); }
    public RelayCommand ShowConnectionCommand { get; }
    public RelayCommand ShowAccountCommand { get; }
    public RelayCommand ShowLobbyCommand { get; }
    public RelayCommand ShowGameRoomCommand { get; }
}
