using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml.Linq;
using UDM18.Client.Behaviors;
using UDM18.Client.Models;
using UDM18.Client.Protocol;
using UDM18.Client.ViewModels;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Initial board has 32 stable pieces", TestInitialBoard),
    ("Coordinate mapping preserves canonical protocol", TestCoordinateMapping),
    ("ULID identifiers are valid and unique", TestUlids),
    ("TCP framing handles fragmented server messages", TestTcpFraming),
    ("Real TV1 framing completes HELLO and LOGIN handshake", TestRealWireHandshake),
    ("Login waits for HELLO_ACK", TestHandshakeOrder),
    ("Shell functions stay locked until authentication", TestAuthenticationGate),
    ("Every frontend button command resolves to a ViewModel command", TestXamlCommandBindings),
    ("Small windows expose scrollbars instead of clipping actions", TestResponsiveViewport),
    ("Game room keeps a 70/30 board-to-controls layout", TestBoardLayoutRatio),
    ("Password boxes update registration view model", TestPasswordBoxBinding),
    ("Challenge uses 10+0 without increment", TestChallengeProfile),
    ("Outgoing challenge is visible and cancellable", TestOutgoingChallengeCancellation),
    ("Quick chat emoji is sent and displayed", TestQuickChatEmoji),
    ("Free text chat is sent through the room protocol", TestFreeTextChat),
    ("Bot game sends selected difficulty", TestBotGameContract),
    ("Created public room opens board in waiting mode", TestWaitingRoomOpensBoard),
    ("Spectator snapshot opens the board directly", TestSpectatorSnapshotOpensBoard),
    ("Cancelled public room clears board and returns to lobby", TestWaitingRoomCancellation),
    ("Leaving spectator mode returns to lobby", TestSpectatorLeaveNavigation),
    ("Reconnect success reopens the active board", TestReconnectNavigation),
    ("Rejected challenge clears stale incoming challenge", TestChallengeRejectedClearsIncoming),
    ("Cancelled challenge clears target invitation", TestChallengeCancelledClearsIncoming),
    ("Only current-turn pieces can be selected", TestSourceSelection),
    ("Selecting another friendly piece switches selection", TestFriendlyReselection),
    ("Black player role sets own side correctly", TestBlackPlayerOwnSide),
    ("Board changes only after MOVE_COMMITTED", TestAuthoritativeMoveFlow),
    ("Committed capture removes only target", TestCommittedCapture),
    ("Check event triggers alert feedback", TestCheckFeedback),
    ("MOVE_REJECTED preserves board", TestRejectedMove),
    ("Unknown piece delta preserves board revision", TestUnknownPieceDelta),
    ("Revision gap preserves board and requests resync", TestRevisionGap),
    ("Snapshot clears stale move highlights", TestSnapshotClearsHighlights),
    ("History auto-selects and rebuilds legacy matches", TestHistoryReplayFallback),
    ("Older snapshots cannot overwrite current state", TestOldSnapshot),
    ("A new bot room accepts revision zero after an older game", TestNewRoomResetsRevision),
    ("Bot snapshot accepts legacy numeric clock side", TestNumericClockSnapshot),
    ("Malformed events report errors without crashing", TestMalformedEvent),
    ("ERROR_RESPONSE is surfaced without disconnecting", TestErrorResponse),
    ("Rematch requires acceptance and opens a fresh board", TestRematchClientFlow),
    ("Expired rematch clears the waiting state", TestRematchExpiry),
    ("Returning to lobby cancels a pending rematch", TestReturnCancelsRematch)
};

var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS  {test.Name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL  {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"\n{tests.Length - failures}/{tests.Length} smoke tests passed.");
return failures == 0 ? 0 : 1;

static Task TestXamlCommandBindings()
{
    var clientRoot = FindClientSourceRoot();
    var views = new (string File, Type ViewModel)[]
    {
        ("MainWindow.xaml", typeof(ShellViewModel)),
        (Path.Combine("Views", "ConnectionView.xaml"), typeof(ConnectionViewModel)),
        (Path.Combine("Views", "AccountView.xaml"), typeof(AccountPageViewModel)),
        (Path.Combine("Views", "LobbyView.xaml"), typeof(LobbyViewModel)),
        (Path.Combine("Views", "GameRoomView.xaml"), typeof(GameRoomViewModel))
    };
    var bindingPattern = new Regex("Command=\"\\{Binding\\s+([A-Za-z0-9_.]+)", RegexOptions.CultureInvariant);
    var checkedBindings = 0;
    foreach (var (file, rootType) in views)
    {
        var xaml = File.ReadAllText(Path.Combine(clientRoot, file));
        foreach (Match match in bindingPattern.Matches(xaml))
        {
            var path = match.Groups[1].Value;
            var currentType = rootType;
            foreach (var segment in path.Split('.'))
            {
                var property = currentType.GetProperty(segment);
                Check(property is not null, $"{file}: binding '{path}' cannot resolve '{segment}' on {currentType.Name}.");
                currentType = property!.PropertyType;
            }
            Check(typeof(System.Windows.Input.ICommand).IsAssignableFrom(currentType),
                $"{file}: binding '{path}' resolves to {currentType.Name}, not ICommand.");
            checkedBindings++;
        }
    }
    Check(checkedBindings >= 35, $"Only {checkedBindings} command bindings were audited; frontend coverage unexpectedly dropped.");
    return Task.CompletedTask;
}

static Task TestResponsiveViewport()
{
    var xaml = File.ReadAllText(Path.Combine(FindClientSourceRoot(), "MainWindow.xaml"));
    Check(xaml.Contains("HorizontalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal), "Missing horizontal overflow protection.");
    Check(xaml.Contains("VerticalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal), "Missing vertical overflow protection.");
    return Task.CompletedTask;
}

static Task TestBoardLayoutRatio()
{
    var document = XDocument.Load(Path.Combine(FindClientSourceRoot(), "Views", "GameRoomView.xaml"));
    var columnWidths = document.Descendants()
        .Where(element => element.Name.LocalName == "ColumnDefinition")
        .Select(element => element.Attribute("Width")?.Value)
        .ToArray();
    Check(columnWidths.Contains("7*", StringComparer.Ordinal), "The board column is not configured as 70 percent.");
    Check(columnWidths.Contains("3*", StringComparer.Ordinal), "The controls column is not configured as 30 percent.");

    var board = document.Descendants().Single(element => element.Name.LocalName == "BoardControl");
    Check(board.Attribute("Width")?.Value == "720" && board.Attribute("Height")?.Value == "800",
        "The enlarged board keeps the canonical 9:10 aspect ratio.");
    return Task.CompletedTask;
}

static string FindClientSourceRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        var fromRepository = Path.Combine(directory.FullName, "Code", "src", "XiangqiOnline.Client");
        if (Directory.Exists(fromRepository)) return fromRepository;
        var fromCode = Path.Combine(directory.FullName, "src", "XiangqiOnline.Client");
        if (Directory.Exists(fromCode)) return fromCode;
    }
    throw new DirectoryNotFoundException("Cannot locate XiangqiOnline.Client source for the frontend binding audit.");
}

static Task TestInitialBoard()
{
    var pieces = InitialBoard.Create();
    Check(pieces.Count == 32, "Expected 32 pieces.");
    Check(pieces.Select(p => p.PieceId).Distinct().Count() == 32, "Piece IDs must be unique.");
    Check(pieces.All(p => p.Position.IsValid()), "Every piece must be on board.");
    Check(pieces.Any(p => p.PieceId == "RED_GENERAL") && pieces.Any(p => p.PieceId == "BLACK_GENERAL"), "General IDs must match the baseline contract.");
    Check(pieces.All(p => p.PieceId is not "RED_GENERAL_1" and not "BLACK_GENERAL_1"), "Numbered general IDs are forbidden.");
    return Task.CompletedTask;
}

static Task TestCoordinateMapping()
{
    var canonical = new Position(1, 9);
    Check(BoardGeometry.CanonicalToView(canonical, BoardOrientation.RedAtBottom) == canonical, "Red view changed coordinate.");
    var blackView = BoardGeometry.CanonicalToView(canonical, BoardOrientation.BlackAtBottom);
    Check(blackView == new Position(7, 0), "Black rotation incorrect.");
    Check(BoardGeometry.ViewToCanonical(blackView.X, blackView.Y, BoardOrientation.BlackAtBottom) == canonical, "Round trip failed.");
    return Task.CompletedTask;
}

static Task TestUlids()
{
    var ids = Enumerable.Range(0, 100).Select(_ => UlidId.New()).ToArray();
    Check(ids.All(id => id.Length == 26), "ULID length must be 26.");
    Check(ids.Distinct().Count() == ids.Length, "ULIDs must be unique.");
    return Task.CompletedTask;
}

static async Task TestTcpFraming()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    await using var transport = new TcpProtocolTransport();
    var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
    transport.MessageHandler = message => { received.TrySetResult(message); return Task.CompletedTask; };
    var acceptTask = listener.AcceptTcpClientAsync();
    await transport.ConnectAsync("127.0.0.1", port, CancellationToken.None);
    using var server = await acceptTask;
    var stream = server.GetStream();

    await transport.SendAsync(new { protocolVersion = "1.0", type = "PING", payload = new { nonce = "N1" } });
    var header = new byte[4];
    await stream.ReadExactlyAsync(header);
    var unsignedLength = BinaryPrimitives.ReadUInt32BigEndian(header);
    Check(unsignedLength is > 0 and <= TcpProtocolTransport.MaxPayloadBytes, "Invalid outbound frame length.");
    var length = (int)unsignedLength;
    var request = new byte[length];
    await stream.ReadExactlyAsync(request);
    using (var doc = JsonDocument.Parse(request)) Check(doc.RootElement.GetProperty("type").GetString() == "PING", "Outbound JSON incorrect.");

    var eventBytes = Encoding.UTF8.GetBytes("{\"type\":\"PONG\",\"payload\":{\"nonce\":\"N1\"}}");
    BinaryPrimitives.WriteUInt32BigEndian(header, (uint)eventBytes.Length);
    await stream.WriteAsync(header.AsMemory(0, 1));
    await stream.WriteAsync(header.AsMemory(1, 3));
    for (var offset = 0; offset < eventBytes.Length; offset += 3)
        await stream.WriteAsync(eventBytes.AsMemory(offset, Math.Min(3, eventBytes.Length - offset)));
    var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Check(message.GetProperty("type").GetString() == "PONG", "Fragmented event was not decoded.");
    await transport.DisconnectAsync();
    listener.Stop();
}

static async Task TestHandshakeOrder()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    var loginTask = client.ConnectAndLoginAsync("127.0.0.1", 18180, "Tester", CancellationToken.None);
    Check(transport.SentTypes.SequenceEqual(["HELLO"]), "LOGIN_REQUEST was sent before HELLO_ACK.");
    await transport.EmitAsync(Json("""{"type":"HELLO_ACK","payload":{"supportedVersion":"1.0","serverId":"S1"}}"""));
    for (var attempt = 0; attempt < 100 && transport.SentTypes.Count < 2; attempt++)
        await Task.Delay(1);
    Check(transport.SentTypes.SequenceEqual(["HELLO", "LOGIN_REQUEST"]), "LOGIN_REQUEST was not sent after HELLO_ACK.");
    Check(!loginTask.IsCompleted, "ConnectAndLoginAsync completed before LOGIN_RESULT.");
    await transport.EmitAsync(Json("""{"type":"LOGIN_RESULT","payload":{"status":"ACCEPTED","token":"TOKEN","player":{"playerId":"P1","displayName":"Tester"}}}"""));
    await loginTask;
    Check(
        transport.SentTypes.SequenceEqual(["HELLO", "LOGIN_REQUEST", "PLAYER_LIST_REQUEST", "ACTIVE_MATCHES_REQUEST", "WAITING_ROOM_LIST", "HISTORY_LIST_REQUEST"]),
        "Lobby data was not requested after login.");
}

static async Task TestRealWireHandshake()
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    await using var transport = new TcpProtocolTransport();
    var client = new GameClient(transport);
    var acceptTask = listener.AcceptTcpClientAsync(timeout.Token).AsTask();
    var loginTask = client.ConnectAndLoginAsync("127.0.0.1", port, "Tester", timeout.Token);
    using var server = await acceptTask;
    var stream = server.GetStream();

    var helloBytes = await TcpFrameCodec.ReadFrameAsync(stream, timeout.Token);
    Check(helloBytes is not null, "TV1 codec did not receive HELLO.");
    using (var hello = JsonDocument.Parse(helloBytes!))
    {
        Check(hello.RootElement.GetProperty("protocolVersion").GetString() == ProtocolConstants.ProtocolVersion, "HELLO envelope version mismatch.");
        Check(hello.RootElement.GetProperty("type").GetString() == "HELLO", "First real-wire request was not HELLO.");
        Check(hello.RootElement.GetProperty("clientSequence").GetInt64() == 1, "HELLO clientSequence mismatch.");
    }

    var ack = JsonSerializer.SerializeToUtf8Bytes(new ServerEventEnvelope<object>
    {
        Type = "HELLO_ACK",
        EventId = UlidId.New(),
        ServerSequence = 1,
        ServerTimeUtc = DateTimeOffset.UtcNow,
        Payload = new { supportedVersion = ProtocolConstants.ProtocolVersion, serverId = "TV1-TEST" }
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    await TcpFrameCodec.WriteFrameAsync(stream, ack, timeout.Token);
    var loginBytes = await TcpFrameCodec.ReadFrameAsync(stream, timeout.Token);
    Check(loginBytes is not null, "TV1 codec did not receive LOGIN_REQUEST.");
    using (var login = JsonDocument.Parse(loginBytes!))
    {
        Check(login.RootElement.GetProperty("type").GetString() == "LOGIN_REQUEST", "HELLO_ACK did not release LOGIN_REQUEST.");
        Check(login.RootElement.GetProperty("clientSequence").GetInt64() == 2, "LOGIN_REQUEST clientSequence mismatch.");
    }

    var loginResult = JsonSerializer.SerializeToUtf8Bytes(new ServerEventEnvelope<object>
    {
        Type = "LOGIN_RESULT",
        EventId = UlidId.New(),
        ServerSequence = 2,
        ServerTimeUtc = DateTimeOffset.UtcNow,
        Payload = new { status = "ACCEPTED", token = "TOKEN", player = new { playerId = "P1", displayName = "Tester" } }
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    await TcpFrameCodec.WriteFrameAsync(stream, loginResult, timeout.Token);
    await loginTask;

    await transport.DisconnectAsync();
    listener.Stop();
}

static async Task TestChallengeProfile()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    await client.SendChallengeAsync("P2");
    var payload = transport.LastSent!.Value.GetProperty("payload");
    Check(payload.GetProperty("timeProfile").GetString() == "10+0", "Challenge must use the no-increment 10+0 profile.");
}

static async Task TestChallengeRejectedClearsIncoming()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    var lobby = new LobbyViewModel(client);

    await transport.EmitAsync(Json("""{"type":"CHALLENGE_RECEIVED","payload":{"challenge":{"challengeId":"C1","fromPlayerId":"P1","fromDisplayName":"Alice"}}}"""));
    Check(lobby.IncomingChallenge?.ChallengeId == "C1", "Incoming challenge was not created.");
    Check(lobby.AcceptCommand.CanExecute(null), "Accept must be enabled for an incoming challenge.");
    Check(lobby.RejectCommand.CanExecute(null), "Reject must be enabled for an incoming challenge.");

    await transport.EmitAsync(Json("""{"type":"CHALLENGE_REJECTED","payload":{"challengeId":"C1","rejectedByPlayerId":"P1","status":"REJECTED"}}"""));
    Check(lobby.IncomingChallenge is null, "Rejected challenge remained visible.");
    Check(!lobby.AcceptCommand.CanExecute(null), "Accept remained enabled after rejection.");
    Check(!lobby.RejectCommand.CanExecute(null), "Reject remained enabled after rejection.");
}

static async Task TestAuthenticationGate()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    var connection = new ConnectionViewModel(client);
    var lobby = new LobbyViewModel(client);
    var game = new GameRoomViewModel(client);
    var shell = new ShellViewModel(connection, lobby, game);

    Check(shell.CurrentPage is AccountPageViewModel, "Account page is not the initial page.");
    Check(!shell.IsAuthenticated, "Shell started in authenticated state.");
    Check(shell.SidebarWidth.Value == 0, "Sidebar was visible before login.");
    Check(!shell.ShowLobbyCommand.CanExecute(null) && !shell.ShowGameRoomCommand.CanExecute(null), "Protected navigation was enabled before login.");

    await transport.EmitAsync(Json("""{"type":"LOGIN_RESULT","payload":{"status":"ACCEPTED","token":"token","player":{"playerId":"P1","displayName":"Tester"}}}"""));

    Check(shell.IsAuthenticated, "Successful login did not unlock the shell.");
    Check(shell.SidebarWidth.Value == 220, "Sidebar did not appear after login.");
    Check(ReferenceEquals(shell.CurrentPage, lobby), "Successful login did not enter the lobby.");
    Check(shell.ShowLobbyCommand.CanExecute(null) && shell.ShowGameRoomCommand.CanExecute(null), "Protected navigation stayed disabled after login.");

    var mainWindowXaml = File.ReadAllText(Path.Combine(FindClientSourceRoot(), "MainWindow.xaml"));
    var lobbyXaml = File.ReadAllText(Path.Combine(FindClientSourceRoot(), "Views", "LobbyView.xaml"));
    Check(!mainWindowXaml.Contains("Content=\"●   Tài khoản\"", StringComparison.Ordinal), "Account navigation remained visible after authentication.");
    Check(lobbyXaml.Contains("Command=\"{Binding LogoutCommand}\"", StringComparison.Ordinal), "Lobby does not expose the logout action.");
    Check(lobby.LogoutCommand.CanExecute(null), "Logout was disabled for an authenticated connection.");

    lobby.LogoutCommand.Execute(null);
    await Task.Delay(30);

    Check(transport.State == ConnectionState.Disconnected, "Logout did not close the server session.");
    Check(!shell.IsAuthenticated, "Logout left the shell authenticated.");
    Check(shell.SidebarWidth.Value == 0, "Sidebar remained visible after logout.");
    Check(shell.CurrentPage is AccountPageViewModel, "Logout did not return to the account page.");
}

static async Task TestHistoryReplayFallback()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    var lobby = new LobbyViewModel(client);
    var game = new GameRoomViewModel(client);

    await transport.EmitAsync(Json("""{"type":"HISTORY_LIST_RESULT","payload":{"matches":[{"matchId":"M1","roomId":"ROOM-1","status":"FINISHED","resultType":"RED_WIN","endReason":"RESIGNATION","totalMoves":1,"startedAtUtc":"2026-08-17T00:00:00Z","endedAtUtc":"2026-08-17T00:01:00Z","winnerSide":"RED","timeProfile":"10+0","viewerSide":"RED","redDisplayName":"Người kiểm thử","blackDisplayName":"Đối thủ"}]}}"""));
    Check(lobby.MatchHistory.Count == 1, "History list was not loaded.");
    Check(lobby.SelectedHistory?.MatchId == "M1", "The first history item was not selected automatically.");
    Check(lobby.ReplayCommand.CanExecute(null), "Replay remained disabled after history loaded.");
    Check(lobby.SelectedHistory?.ResultLabel == "Bạn thắng" && lobby.SelectedHistory.OpponentName == "Đối thủ", "History did not personalize result and opponent.");

    await transport.EmitAsync(Json("""{"type":"HISTORY_DETAIL_RESULT","payload":{"match":{"matchId":"M1","roomId":"ROOM-1","redPlayerId":"P1","blackPlayerId":"P2","resultType":"RED_WIN"},"positions":[],"moves":[{"revision":1,"side":"RED","pieceId":"RED_PAWN_1","pieceType":"PAWN","capturedPieceId":null,"from":{"x":0,"y":6},"to":{"x":0,"y":5}}]}}"""));
    Check(game.RoomId == "ROOM-1" && game.Revision == 0 && game.IsReplayMode, "Replay did not open at the initial position.");
    Check(game.ReplayNextCommand.CanExecute(null), "Replay next command was disabled at the initial position.");
    game.ReplayNextCommand.Execute(null);
    var pawn = game.Pieces.Single(piece => piece.PieceId == "RED_PAWN_1");
    Check(game.Revision == 1 && pawn.Position == new Position(0, 5), "Replay next did not apply the persisted move.");
    Check(game.CurrentTurn == SideColor.Black && game.IsGameEnded && game.IsSpectator, "Replay state was not read-only and terminal.");
    Check(game.ReplayPreviousCommand.CanExecute(null), "Replay previous command remained disabled after advancing.");
}

static Task TestPasswordBoxBinding()
{
    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var thread = new Thread(() =>
    {
        try
        {
            var viewModel = new ConnectionViewModel(new GameClient(new FakeTransport()));
            var password = new PasswordBox();
            var confirmation = new PasswordBox();
            BindingOperations.SetBinding(password, PasswordBoxAssistant.BoundPasswordProperty,
                new Binding(nameof(ConnectionViewModel.Password)) { Source = viewModel, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            BindingOperations.SetBinding(confirmation, PasswordBoxAssistant.BoundPasswordProperty,
                new Binding(nameof(ConnectionViewModel.ConfirmPassword)) { Source = viewModel, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            password.Password = "12345678";
            confirmation.Password = "12345678";
            Check(viewModel.Password == "12345678", "Main password did not reach the view model.");
            Check(viewModel.ConfirmPassword == "12345678", "Confirmation password did not reach the view model.");
            Check(BindingOperations.IsDataBound(password, PasswordBoxAssistant.BoundPasswordProperty), "Main password binding was replaced while typing.");
            completion.SetResult();
        }
        catch (Exception ex) { completion.SetException(ex); }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    return completion.Task;
}

static async Task TestOutgoingChallengeCancellation()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    var lobby = new LobbyViewModel(client);

    await transport.EmitAsync(Json("""{"type":"CHALLENGE_SENT","payload":{"challengeId":"C-OUT","targetPlayerId":"P2","targetDisplayName":"Bob"}}"""));
    Check(lobby.HasOutgoingChallenge, "Outgoing challenge was not shown.");
    Check(lobby.OutgoingChallengeText.Contains("Bob", StringComparison.Ordinal), "Target name was not shown.");
    Check(lobby.CancelChallengeCommand.CanExecute(null), "Cancel must be enabled while waiting.");

    lobby.CancelChallengeCommand.Execute(null);
    Check(transport.LastSent?.GetProperty("type").GetString() == "CHALLENGE_CANCEL", "Cancel request was not sent.");
    Check(transport.LastSent?.GetProperty("payload").GetProperty("challengeId").GetString() == "C-OUT", "Wrong challenge was cancelled.");

    await transport.EmitAsync(Json("""{"type":"CHALLENGE_CANCELLED","payload":{"challengeId":"C-OUT","status":"CANCELLED"}}"""));
    Check(!lobby.HasOutgoingChallenge, "Cancelled outgoing challenge remained visible.");
    Check(!lobby.CancelChallengeCommand.CanExecute(null), "Cancel remained enabled after confirmation.");
}

static async Task TestQuickChatEmoji()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    var game = new GameRoomViewModel(client);
    await transport.EmitAsync(RoomCreated("ROOM-CHAT"));

    Check(game.SendQuickChatCommand.CanExecute("GOOD_MOVE"), "Quick chat must be enabled inside a room.");
    game.SendQuickChatCommand.Execute("GOOD_MOVE");
    Check(transport.LastSent?.GetProperty("type").GetString() == "QUICK_CHAT_SEND", "Quick chat request was not sent.");
    Check(transport.LastSent?.GetProperty("roomId").GetString() == "ROOM-CHAT", "Quick chat used the wrong room.");
    Check(transport.LastSent?.GetProperty("payload").GetProperty("code").GetString() == "GOOD_MOVE", "Wrong emoji code was sent.");

    await transport.EmitAsync(Json("""{"type":"QUICK_CHAT_RECEIVED","payload":{"messageId":"M1","roomId":"ROOM-CHAT","senderPlayerId":"P2","senderDisplayName":"Bob","code":"GOOD_MOVE","text":"Nước hay!","isSpectator":false,"sentAtUtc":"2026-08-15T10:00:00Z"}}"""));
    Check(game.ChatMessages.Count == 1, "Received quick chat was not displayed.");
    Check(game.ChatMessages[0].IconPath.EndsWith("clapping-hands.png", StringComparison.Ordinal), "Wrong emoji image was mapped.");
    Check(game.ChatMessages[0].SenderDisplayName == "Bob", "Sender name was not displayed.");
}

static async Task TestFreeTextChat()
{
    var (transport, game) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-TEXT"));
    await transport.EmitAsync(Snapshot("ROOM-TEXT", 0));
    game.ChatInput = "Chào đối thủ!";
    Check(game.SendChatCommand.CanExecute(null), "Text chat command should be enabled.");
    game.SendChatCommand.Execute(null);
    await Task.Delay(40);
    Check(transport.LastSent?.GetProperty("type").GetString() == "QUICK_CHAT_SEND", "Text chat request was not sent.");
    Check(transport.LastSent?.GetProperty("payload").GetProperty("text").GetString() == "Chào đối thủ!", "Wrong text chat payload.");
}

static async Task TestWaitingRoomOpensBoard()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    var lobby = new LobbyViewModel(client);
    var game = new GameRoomViewModel(client);
    var opened = false;
    lobby.OpenGameRequested += () => opened = true;

    await transport.EmitAsync(Json("""{"type":"WAITING_ROOM_CREATED","payload":{"roomId":"ROOM-WAIT","ownerPlayerId":"P1","ownerDisplayName":"Tester","timeProfile":"10+0","createdAtUtc":"2026-08-15T10:00:00Z"}}"""));

    Check(opened, "Lobby did not navigate to the board after room creation.");
    Check(game.RoomId == "ROOM-WAIT", "Waiting board did not retain the room id.");
    Check(game.IsWaitingForOpponent, "Board was not placed in waiting mode.");
    Check(game.Pieces.Count == 32, "Waiting board did not show the initial 32 pieces.");
    Check(game.RedRemainingMs == 600_000 && game.BlackRemainingMs == 600_000, "Waiting clocks were not initialized to 10+0.");
    Check(!game.CanMove, "Owner could move before an opponent joined.");
}

static async Task TestSpectatorSnapshotOpensBoard()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    var lobby = new LobbyViewModel(client);
    var game = new GameRoomViewModel(client);
    var opened = false;
    lobby.OpenGameRequested += () => opened = true;

    await transport.EmitAsync(Json("""{"type":"GAME_STATE_SNAPSHOT","payload":{"roomId":"ROOM-WATCH","revision":4,"currentTurn":"RED","status":"PLAYING","viewerRole":"SPECTATOR","pieces":[{"pieceId":"RED_GENERAL","side":"RED","type":"GENERAL","x":4,"y":9,"captured":false},{"pieceId":"BLACK_GENERAL","side":"BLACK","type":"GENERAL","x":4,"y":0,"captured":false}],"spectatorCount":2}}"""));

    Check(opened, "Spectator snapshot did not request direct board navigation.");
    Check(game.RoomId == "ROOM-WATCH" && game.IsSpectator, "Board did not enter spectator mode.");
    Check(!game.CanMove, "Spectator was allowed to move pieces.");
}

static async Task TestWaitingRoomCancellation()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    var game = new GameRoomViewModel(client);
    var returned = false;
    game.ReturnToLobbyRequested += () => returned = true;

    await transport.EmitAsync(Json("""{"type":"WAITING_ROOM_CREATED","payload":{"roomId":"ROOM-CANCEL","ownerPlayerId":"P1","ownerDisplayName":"Tester","timeProfile":"10+0","createdAtUtc":"2026-08-15T10:00:00Z"}}"""));
    Check(game.CancelWaitingRoomCommand.CanExecute(null), "Cancel command was disabled for the room owner.");
    await transport.EmitAsync(Json("""{"type":"WAITING_ROOM_CANCELLED","payload":{"roomId":"ROOM-CANCEL"}}"""));

    Check(returned, "Cancellation did not request navigation back to the lobby.");
    Check(game.RoomId is null && game.Pieces.Count == 0, "Cancelled waiting room state was retained.");
    Check(!game.IsWaitingForOpponent, "Waiting overlay remained active after cancellation.");
}

static async Task TestSpectatorLeaveNavigation()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    var connection = new ConnectionViewModel(client);
    var lobby = new LobbyViewModel(client);
    var game = new GameRoomViewModel(client);
    var shell = new ShellViewModel(connection, lobby, game);
    shell.ShowGameRoomCommand.Execute(null);

    await transport.EmitAsync(Json("""{"type":"SPECTATOR_LEFT","payload":{"roomId":"ROOM-WATCH"}}"""));

    Check(ReferenceEquals(shell.CurrentPage, lobby), "Spectator remained on an empty board after leaving.");
    Check(game.RoomId is null && game.Pieces.Count == 0, "Spectator room state was not cleared.");
}

static async Task TestReconnectNavigation()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    var connection = new ConnectionViewModel(client);
    var lobby = new LobbyViewModel(client);
    var game = new GameRoomViewModel(client);
    var shell = new ShellViewModel(connection, lobby, game);

    await transport.EmitAsync(Json("""{"type":"RECONNECT_ACCEPTED","payload":{"playerId":"P1","roomId":"ROOM-ACTIVE"}}"""));

    Check(ReferenceEquals(shell.CurrentPage, game), "Successful reconnect did not reopen the active board.");
    Check(connection.Status.Contains("kết nối lại", StringComparison.OrdinalIgnoreCase), "Reconnect status was not shown to the player.");
}

static async Task TestChallengeCancelledClearsIncoming()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    var lobby = new LobbyViewModel(client);
    await transport.EmitAsync(Json("""{"type":"CHALLENGE_RECEIVED","payload":{"challenge":{"challengeId":"C2","fromPlayerId":"P2","fromDisplayName":"Bob"}}}"""));
    await transport.EmitAsync(Json("""{"type":"CHALLENGE_CANCELLED","payload":{"challengeId":"C2","status":"CANCELLED"}}"""));
    Check(lobby.IncomingChallenge is null, "Cancelled invitation remained visible for the target.");
    Check(!lobby.AcceptCommand.CanExecute(null), "Accept remained enabled after cancellation.");
}

static async Task TestSourceSelection()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 1));
    vm.CoordinateClickedCommand.Execute(new Position(4, 4));
    Check(vm.Selected is null, "Empty source square was selected.");
    vm.CoordinateClickedCommand.Execute(new Position(0, 0));
    Check(vm.Selected is null, "Opponent piece was selected.");
    vm.CoordinateClickedCommand.Execute(new Position(1, 9));
    Check(vm.Selected == new Position(1, 9), "Current-turn piece was not selected.");
}

static async Task TestBlackPlayerOwnSide()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-BLACK"));
    await transport.EmitAsync(Snapshot("ROOM-BLACK", 0, "BLACK", "PLAYER_BLACK"));
    Check(vm.CanMove, "Black player was treated as having no own side.");
    Check(vm.Orientation == BoardOrientation.BlackAtBottom, "Black player's board was not rotated automatically.");
    Check(vm.OwnSideLabel.Contains("ĐEN", StringComparison.Ordinal), "Black side badge is missing.");
    vm.CoordinateClickedCommand.Execute(new Position(0, 0));
    Check(vm.Selected == new Position(0, 0), "Black player could not select a black piece on their turn.");
}

static async Task TestAuthoritativeMoveFlow()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 1));
    var before = vm.Pieces.Single(p => p.PieceId == "RED_HORSE_1").Position;
    vm.CoordinateClickedCommand.Execute(before);
    vm.CoordinateClickedCommand.Execute(new Position(2, 7));
    Check(vm.Pieces.Single(p => p.PieceId == "RED_HORSE_1").Position == before, "Client mutated board before commit.");
    Check(transport.LastSent?.GetProperty("type").GetString() == "MOVE_REQUEST", "MOVE_REQUEST was not sent.");
    Check(vm.IsMovePending, "Pending guard was not enabled.");
    await transport.EmitAsync(MoveCommitted(2));
    Check(vm.Pieces.Single(p => p.PieceId == "RED_HORSE_1").Position == new Position(2, 7), "Committed move not applied.");
    Check(vm.Revision == 2 && !vm.IsMovePending, "Revision/pending state incorrect.");
    Check(vm.CurrentTurn == SideColor.Black, "Current turn was not synchronized from the committed event.");
}

static async Task TestCommittedCapture()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 1));
    await transport.EmitAsync(Json("""
      {"type":"MOVE_COMMITTED","revision":2,"payload":{"side":"RED","pieceId":"RED_CHARIOT_1","from":{"x":0,"y":9},"to":{"x":0,"y":3},"capturedPieceId":"BLACK_PAWN_1","clocks":{"activeSide":"BLACK"}}}
      """));
    Check(vm.Pieces.All(p => p.PieceId != "BLACK_PAWN_1"), "Captured target remains on board.");
    Check(vm.Pieces.Single(p => p.PieceId == "RED_CHARIOT_1").Position == new Position(0, 3), "Capturing piece did not move.");
    Check(vm.Pieces.Count == 31 && vm.Revision == 2, "Capture state is inconsistent.");
}

static async Task TestBotGameContract()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    await client.StartBotGameAsync("HARD");
    Check(transport.LastSent?.GetProperty("type").GetString() == "BOT_GAME_REQUEST", "Bot request type is incorrect.");
    Check(transport.LastSent?.GetProperty("payload").GetProperty("difficulty").GetString() == "HARD", "Bot difficulty was not sent.");
}

static async Task TestCheckFeedback()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(Snapshot("ROOM-1", 1));
    await transport.EmitAsync(MoveCommitted(2, isCheck: true));
    Check(vm.IsCheckAlert, "Check did not activate the visual alert.");
    Check(vm.CheckBanner == "CHIẾU TƯỚNG", "Check banner is incorrect.");
    Check(vm.Status.Contains("CHIẾU TƯỚNG"), "Check status was not surfaced.");
}

static async Task TestRejectedMove()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 4));
    var before = vm.Pieces.Select(p => (p.PieceId, p.Position)).ToArray();
    vm.CoordinateClickedCommand.Execute(new Position(0, 6));
    vm.CoordinateClickedCommand.Execute(new Position(0, 5));
    await transport.EmitAsync(Json("""
      {"type":"MOVE_REJECTED","payload":{"errorCode":"NOT_YOUR_TURN","message":"Không đúng lượt","revision":4}}
      """));
    Check(before.SequenceEqual(vm.Pieces.Select(p => (p.PieceId, p.Position))), "Rejected move changed board.");
    Check(!vm.IsMovePending && vm.Revision == 4, "Rejected state did not clear pending safely.");
}

static async Task TestUnknownPieceDelta()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 4));
    var before = vm.Pieces.Select(p => (p.PieceId, p.Position)).ToArray();
    await transport.EmitAsync(Json("""
      {"type":"MOVE_COMMITTED","revision":5,"payload":{"pieceId":"MISSING","from":{"x":0,"y":0},"to":{"x":0,"y":1},"capturedPieceId":null}}
      """));
    Check(vm.Revision == 4, "Unknown delta advanced revision.");
    Check(before.SequenceEqual(vm.Pieces.Select(p => (p.PieceId, p.Position))), "Unknown delta changed board.");
    Check(transport.SentTypes.Last() == "RESYNC_REQUEST", "Unknown piece did not request resync.");
}

static async Task TestRevisionGap()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 4));
    var before = vm.Pieces.Select(p => (p.PieceId, p.Position)).ToArray();
    await transport.EmitAsync(MoveCommitted(7));
    Check(vm.Revision == 4, "Revision gap advanced local revision.");
    Check(before.SequenceEqual(vm.Pieces.Select(p => (p.PieceId, p.Position))), "Revision gap changed board.");
    Check(transport.SentTypes.Last() == "RESYNC_REQUEST", "Revision gap did not request resync.");
}

static async Task TestSnapshotClearsHighlights()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 1));
    await transport.EmitAsync(MoveCommitted(2));
    Check(vm.LastFrom is not null && vm.LastTo is not null, "Committed move was not highlighted.");
    await transport.EmitAsync(Snapshot("ROOM-1", 3));
    Check(vm.LastFrom is null && vm.LastTo is null, "Snapshot retained stale highlights.");
}

static async Task TestOldSnapshot()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 4));
    await transport.EmitAsync(Snapshot("ROOM-1", 3));
    Check(vm.Revision == 4, "Older snapshot overwrote the current revision.");
}

static async Task TestNewRoomResetsRevision()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-OLD"));
    await transport.EmitAsync(Snapshot("ROOM-OLD", 12));
    Check(vm.Revision == 12, "Old room was not loaded.");

    await transport.EmitAsync(RoomCreated("ROOM-BOT-NEW"));
    await transport.EmitAsync(Snapshot("ROOM-BOT-NEW", 0));

    Check(vm.RoomId == "ROOM-BOT-NEW", "New bot room id was not applied.");
    Check(vm.Revision == 0, "Revision zero from the new bot room was rejected as stale.");
    Check(vm.Pieces.Count == 32, "New bot room board was not loaded.");
    Check(vm.CanMove, "Human red player cannot move in the new bot room.");
}

static async Task TestNumericClockSnapshot()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-BOT-NUMERIC"));
    var pieces = InitialBoard.Create().Select(p => new
    {
        pieceId = p.PieceId,
        side = p.Side.ToString().ToUpperInvariant(),
        type = p.Type.ToString().ToUpperInvariant(),
        x = p.Position.X,
        y = p.Position.Y,
        captured = false
    }).ToArray();
    await transport.EmitAsync(JsonSerializer.SerializeToElement(new
    {
        type = "GAME_STATE_SNAPSHOT",
        payload = new
        {
            roomId = "ROOM-BOT-NUMERIC",
            revision = 0,
            currentTurn = "RED",
            status = "PLAYING",
            viewerRole = "PLAYER_RED",
            pieces,
            clocks = new
            {
                redRemainingMs = 600_000,
                blackRemainingMs = 600_000,
                activeSide = 0,
                incrementMs = 5_000,
                serverAnchorUtc = DateTimeOffset.UtcNow,
                isExpired = false
            }
        }
    }));
    Check(vm.Pieces.Count == 32, "Numeric clock enum caused the bot board snapshot to be discarded.");
    Check(vm.RedRemainingMs > 0 && vm.BlackRemainingMs == 600_000, "Bot clocks were not parsed.");
    Check(vm.CanMove, "Bot snapshot did not enable the human red turn.");
}

static async Task TestMalformedEvent()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    string? error = null;
    client.ErrorReceived += message => error = message;
    await transport.EmitAsync(Json("""{"type":"MOVE_COMMITTED","revision":2,"payload":{"pieceId":"RED_HORSE_1"}}"""));
    Check(!string.IsNullOrWhiteSpace(error), "Malformed event was not reported.");
    Check(transport.State == ConnectionState.Connected, "Malformed event closed the connection.");
}

static async Task TestErrorResponse()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    string? error = null;
    client.ErrorReceived += message => error = message;
    await transport.EmitAsync(Json("""{"type":"ERROR_RESPONSE","payload":{"errorCode":"INVALID_REQUEST","message":"Invalid request"}}"""));
    Check(error == "Invalid request", "ERROR_RESPONSE message was not surfaced.");
    Check(transport.State == ConnectionState.Connected, "ERROR_RESPONSE closed the connection.");
}

static async Task TestFriendlyReselection()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-RESELECT"));
    await transport.EmitAsync(Snapshot("ROOM-RESELECT", 0));
    vm.CoordinateClickedCommand.Execute(new Position(0, 6));
    Check(vm.Selected == new Position(0, 6), "First red pawn was not selected.");
    vm.CoordinateClickedCommand.Execute(new Position(2, 6));
    Check(vm.Selected == new Position(2, 6), "Clicking another friendly pawn did not switch selection.");
    Check(!transport.SentTypes.Contains("MOVE_REQUEST"), "Friendly reselection sent an invalid move to Server.");
}

static async Task TestRematchClientFlow()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-OLD"));
    await transport.EmitAsync(Snapshot("ROOM-OLD", 8));
    await transport.EmitAsync(Json("""{"type":"GAME_ENDED","payload":{"finalResult":{"resultType":"RED_WIN","endReason":"CHECKMATE","winnerSide":"RED","explanation":"Mate"}}}"""));
    Check(vm.IsGameEnded, "Finished game did not enable rematch state.");
    Check(vm.RequestRematchCommand.CanExecute(null), "Player cannot request a rematch after game end.");

    vm.RequestRematchCommand.Execute(null);
    await Task.Delay(30);
    Check(transport.SentTypes.Last() == "REMATCH_REQUEST", "Rematch button did not send REMATCH_REQUEST.");
    await transport.EmitAsync(Json("""{"type":"ERROR_RESPONSE","payload":{"errorCode":"REMATCH_NOT_AVAILABLE","message":"Opponent left"}}"""));
    Check(!vm.IsRematchPending, "Rejected rematch left the request button locked.");

    await transport.EmitAsync(Json("""{"type":"REMATCH_OFFERED","payload":{"originalRoomId":"ROOM-OLD","requestedBy":"P-OTHER","targetPlayerId":"P-ME","expiresAtUtc":"2026-08-16T10:00:00Z"}}"""));
    Check(vm.HasIncomingRematchOffer, "Opponent rematch offer was not displayed.");
    Check(vm.AcceptRematchCommand.CanExecute(null), "Incoming rematch cannot be accepted.");
    vm.AcceptRematchCommand.Execute(null);
    await Task.Delay(30);
    Check(transport.SentTypes.Last() == "REMATCH_RESPONSE", "Accept button did not send REMATCH_RESPONSE.");
    Check(transport.LastSent!.Value.GetProperty("payload").GetProperty("accept").GetBoolean(), "Rematch response was not accepted.");

    await transport.EmitAsync(RoomCreated("ROOM-NEW"));
    await transport.EmitAsync(Snapshot("ROOM-NEW", 0, viewerRole: "PLAYER_BLACK"));
    Check(vm.RoomId == "ROOM-NEW" && !vm.IsGameEnded, "Accepted rematch did not reset into the new room.");
    Check(!vm.HasIncomingRematchOffer && !vm.IsRematchPending, "Old rematch state leaked into the new room.");
}

static async Task TestRematchExpiry()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-EXPIRE"));
    await transport.EmitAsync(Snapshot("ROOM-EXPIRE", 3));
    await transport.EmitAsync(Json("""{"type":"GAME_ENDED","payload":{"finalResult":{"resultType":"BLACK_WIN","endReason":"TIMEOUT"}}}"""));
    await transport.EmitAsync(JsonSerializer.SerializeToElement(new
    {
        type = "REMATCH_OFFERED",
        payload = new
        {
            originalRoomId = "ROOM-EXPIRE",
            requestedBy = "P-OTHER",
            targetPlayerId = "P-ME",
            expiresAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(60)
        }
    }));
    Check(vm.HasIncomingRematchOffer, "Rematch offer was not shown before expiry.");
    await Task.Delay(180);
    Check(!vm.HasIncomingRematchOffer && !vm.IsRematchPending, "Expired rematch remained visible or locked.");
    Check(vm.RequestRematchCommand.CanExecute(null), "Player cannot send a new request after expiry.");
}

static async Task TestReturnCancelsRematch()
{
    var (transport, vm) = CreateGame();
    var returned = false;
    vm.ReturnToLobbyRequested += () => returned = true;
    await transport.EmitAsync(RoomCreated("ROOM-LEAVE"));
    await transport.EmitAsync(Snapshot("ROOM-LEAVE", 5));
    await transport.EmitAsync(Json("""{"type":"GAME_ENDED","payload":{"finalResult":{"resultType":"DRAW","endReason":"DRAW_AGREEMENT"}}}"""));
    vm.RequestRematchCommand.Execute(null);
    await Task.Delay(30);
    vm.ReturnToLobbyCommand.Execute(null);
    await Task.Delay(30);
    Check(returned, "Return-to-lobby navigation was blocked by a pending rematch.");
    Check(transport.SentTypes.Last() == "REMATCH_CANCEL", "Leaving did not cancel the pending rematch on Server.");
    Check(!vm.IsRematchPending, "Pending rematch state remained after leaving.");
}

static (FakeTransport, GameRoomViewModel) CreateGame()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    return (transport, new GameRoomViewModel(client));
}

static JsonElement RoomCreated(string roomId) => JsonSerializer.SerializeToElement(new
{
    type = "ROOM_CREATED",
    payload = new { roomId }
});

static JsonElement Snapshot(string roomId, long revision, string currentTurn = "RED", string viewerRole = "PLAYER_RED")
{
    var pieces = InitialBoard.Create().Select(p => new
    {
        pieceId = p.PieceId,
        side = p.Side.ToString().ToUpperInvariant(),
        type = p.Type.ToString().ToUpperInvariant(),
        x = p.Position.X,
        y = p.Position.Y,
        captured = false
    }).ToArray();
    return JsonSerializer.SerializeToElement(new
    {
        type = "GAME_STATE_SNAPSHOT",
        payload = new { roomId, revision, currentTurn, status = "PLAYING", viewerRole, pieces }
    });
}

static JsonElement MoveCommitted(long revision, bool isCheck = false) => JsonSerializer.SerializeToElement(new
{
    type = "MOVE_COMMITTED",
    revision,
    payload = new
    {
        pieceId = "RED_HORSE_1",
        side = "RED",
        from = new { x = 1, y = 9 },
        to = new { x = 2, y = 7 },
        capturedPieceId = (string?)null,
        isCheck,
        isCheckmate = false,
        clocks = new { activeSide = "BLACK" }
    }
});

static JsonElement Json(string json)
{
    using var doc = JsonDocument.Parse(json);
    return doc.RootElement.Clone();
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FakeTransport : IProtocolTransport
{
    public ConnectionState State { get; private set; } = ConnectionState.Connected;
    public event Action<ConnectionState, string?>? StateChanged;
    public Func<JsonElement, Task>? MessageHandler { get; set; }
    public JsonElement? LastSent { get; private set; }
    public List<string> SentTypes { get; } = [];
    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken) { State = ConnectionState.Connected; StateChanged?.Invoke(State, null); return Task.CompletedTask; }
    public Task DisconnectAsync() { State = ConnectionState.Disconnected; StateChanged?.Invoke(State, null); return Task.CompletedTask; }
    public Task SendAsync(object envelope, CancellationToken cancellationToken = default)
    {
        LastSent = JsonSerializer.SerializeToElement(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        SentTypes.Add(LastSent.Value.GetProperty("type").GetString() ?? "");
        return Task.CompletedTask;
    }
    public async Task EmitAsync(JsonElement message) { if (MessageHandler is not null) await MessageHandler(message); }
    public void Abort() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
