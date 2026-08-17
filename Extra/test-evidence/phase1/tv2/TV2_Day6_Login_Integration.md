# TV2 Day 6 — Production LOGIN_REQUEST / PLAYER_LIST_UPDATED Integration Evidence

Commit: `feature/tv2-d6` (see git log)
Baseline: `origin/develop = 738870b`

## 1. Root cause

`Code/src/XiangqiOnline.Server/Program.cs` (baseline 738870b) registered only the
`HELLO` route. A real TV5 client that had completed HELLO -> HELLO_ACK then sent
`LOGIN_REQUEST` and waited for `LOGIN_RESULT`, but the production MessageRouter had
no route for it -> `ERROR_RESPONSE INVALID_MESSAGE_TYPE` -> client login failed.

## 2. Fix (minimal vertical slice, no later-phase features)

- `Code/src/XiangqiOnline.Server/Networking/LoginMessageHandler.cs` — NEW: LOGIN_REQUEST
  -> `PlayerSessionDirectory.Login` -> LOGIN_RESULT `{ status: ACCEPTED, token, player }`,
  exactly the payload shape the client `GameClient.ParseLogin` requires.
- `Code/src/XiangqiOnline.Server/Networking/PlayerListMessageHandler.cs` — NEW:
  PLAYER_LIST_REQUEST -> PLAYER_LIST_UPDATED carrying the directory snapshot, so the
  logged-in player appears in the lobby/player list.
- `Code/src/XiangqiOnline.Server/Networking/LobbyMessageRoutes.cs` — NEW: single wiring
  helper shared by Program.cs and the real-TCP integration tests.
- `Code/src/XiangqiOnline.Server/Networking/ClientConnectionHandler.cs` — carries a
  `ConnectionId` (assigned by GameServerHost) required by `PlayerSessionDirectory.Login`.
- `Code/src/XiangqiOnline.Server/Networking/GameServerHost.cs` — assigns connection id.
- `Code/src/XiangqiOnline.Server/Program.cs` — creates PlayerSessionDirectory and
  registers LOGIN_REQUEST + PLAYER_LIST_REQUEST.
- `Code/tests/XiangqiOnline.IntegrationTests/ProductionHelloHandshakeTests.cs` — real
  TCP E2E: HELLO -> LOGIN -> PLAYER_LIST.

## 3. Build

```
dotnet build Code/XiangqiOnline.slnx -c Release
Build succeeded. 0 Error(s) (5 pre-existing warnings, none new)
```

## 4. Regression tests (full solution, Release)

```
dotnet test Code/XiangqiOnline.slnx -c Release
Shared.Tests           27/27 PASS
RuleEngine.Tests      229/229 PASS
IntegrationTests       32/32 PASS   (incl. new login E2E)
Server.Tests           63/63 PASS
Protocol.Tests         0 (pre-existing: no tests registered)
```

Client smoke tests (17/17 PASS) — unchanged, no client regression.

## 5. Real production probe

Server binary `Code/src/XiangqiOnline.Server/bin/Release/net10.0/XiangqiOnline.Server.dll`
bound 127.0.0.1:5000. Real TcpClient frames (same wire format as TV5
TcpProtocolTransport):

```
ACK type=HELLO_ACK supportedVersion=1.0
LOGIN type=LOGIN_RESULT status=ACCEPTED token=665d053c82d0412681bd1d52678b7eff
      player=665d053c82d0412681bd1d52678b7eff/ProbeTester
PLAYER_LIST type=PLAYER_LIST_UPDATED
      players=[{"playerId":"665d053c82d0412681bd1d52678b7eff",
                "displayName":"ProbeTester","status":"AVAILABLE"}]
PROBE RESULT: OK — player appears in lobby list with status AVAILABLE
PROBE PASS
```

## 6. Final verdict

LOGIN_REQUEST -> LOGIN_RESULT and player-in-lobby-list (PLAYER_LIST_UPDATED) are
verified end-to-end on the current develop baseline through the shipped production
path. TV5 can now re-run its production login integration gate.
