# TV2 Day 6 — Production Server HELLO/HELLO_ACK Integration Evidence

Commit: `feature/tv2-d6` (see git log)
Baseline: `origin/develop = 738870b`

## 1. Root cause

`Code/src/XiangqiOnline.Server/Program.cs` (baseline 738870b) subscribed only to
`TcpServerHost.ClientAccepted` to print a log line. It never wired the accepted
socket into `ConnectionReceiveLoop`, never parsed a `RequestEnvelope`, never
dispatched `HELLO`, and never wrote `HELLO_ACK`. Every production connection was
accepted and then abandoned -> real client sent HELLO and got 0 bytes.

## 2. Fix (production path now wired)

- `Code/src/XiangqiOnline.Server/Networking/MessageRouter.cs` — type -> handler routing.
- `Code/src/XiangqiOnline.Server/Networking/HelloMessageHandler.cs` — HELLO -> HELLO_ACK.
- `Code/src/XiangqiOnline.Server/Networking/ClientConnectionHandler.cs` — per-connection
  receive loop + envelope parsing + response writing over the locked TcpFrameCodec.
- `Code/src/XiangqiOnline.Server/Networking/GameServerHost.cs` — accept -> handler wiring.
- `Code/src/XiangqiOnline.Server/Program.cs` — registers HELLO route, hosts GameServerHost.
- `Code/tests/XiangqiOnline.IntegrationTests/ProductionHelloHandshakeTests.cs` — real TCP E2E.

## 3. Build

```
dotnet build Code/XiangqiOnline.slnx -c Release
Build succeeded. 0 Warning(s) 0 Error(s)
```

## 4. Regression tests

```
dotnet test Code/XiangqiOnline.slnx -c Release
Shared.Tests           27/27 PASS
RuleEngine.Tests      229/229 PASS
IntegrationTests        5/5 PASS   (incl. 2 new production handshake tests)
Server.Tests           63/63 PASS
Protocol.Tests         0 (pre-existing: no tests registered)
```

## 5. Real production probe

Server binary `Code/src/XiangqiOnline.Server/bin/Debug/net10.0/XiangqiOnline.Server.exe`
bound 0.0.0.0:5000. Real TcpClient -> 127.0.0.1:5000:

```
HELLO sent: 222 bytes
Header read: 4 bytes (00-00-01-11)
Response length: 273
{"protocolVersion":"1.0","type":"HELLO_ACK","eventId":"3ca6b9a579ff45e29dbabab917c10080",
 "causationRequestId":"01JPROBE00000000000001","roomId":null,"revision":null,
 "serverSequence":0,"serverTimeUtc":"2026-08-13T07:19:43.8592875+00:00",
 "payload":{"supportedVersion":"1.0"}}
PROBE RESULT: PASS
```

## 6. Final verdict

Production HELLO/HELLO_ACK path is verified on the current develop baseline.
TV5 can re-run its production integration gate.
