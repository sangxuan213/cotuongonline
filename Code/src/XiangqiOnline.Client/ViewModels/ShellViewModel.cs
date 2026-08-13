namespace UDM18.Client.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private object _currentPage;
    public ShellViewModel(ConnectionViewModel connection, LobbyViewModel lobby, GameRoomViewModel gameRoom, bool demoMode = false)
    {
        Connection = connection;
        Lobby = lobby;
        GameRoom = gameRoom;
        IsDemoMode = demoMode;
        _currentPage = demoMode ? gameRoom : connection;
        ShowConnectionCommand = new RelayCommand(() => CurrentPage = Connection);
        ShowLobbyCommand = new RelayCommand(() => CurrentPage = Lobby);
        ShowGameRoomCommand = new RelayCommand(() => CurrentPage = GameRoom);
    }

    public ConnectionViewModel Connection { get; }
    public LobbyViewModel Lobby { get; }
    public GameRoomViewModel GameRoom { get; }
    public bool IsDemoMode { get; }
    public object CurrentPage { get => _currentPage; private set => Set(ref _currentPage, value); }
    public RelayCommand ShowConnectionCommand { get; }
    public RelayCommand ShowLobbyCommand { get; }
    public RelayCommand ShowGameRoomCommand { get; }
}
