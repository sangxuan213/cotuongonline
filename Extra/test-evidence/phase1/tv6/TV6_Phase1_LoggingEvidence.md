# TV6 Phase 1 — Logging Evidence

## Logging Framework (P1-TV6-D4)

- **`Microsoft.Extensions.Logging`** — abstraction used throughout the persistence layer.
- **Serilog** — sinks configured via `LoggingSetup` for structured output.
- All logging is **structured** (named placeholders), enabling correlation and querying.

## Correlation

`CorrelationContext` provides a correlation id that flows through log entries, allowing a single client request / move commit to be traced end-to-end.

## Secret Redaction

`SecretRedactor` ensures **no tokens / secrets / sensitive values** are written to logs. Any secret-shaped value is redacted before logging.

## No-Crash Guarantee

All logging calls are defensive:
- Logging is wrapped so a logging failure **never throws** into the business flow.
- Business logic (move commit, repository operations) is unaffected by logging errors.

## Integration Coverage

`XiangqiOnline.IntegrationTests/Logging/Tv6LoggingTests` validates:
- Structured logging emits expected fields.
- Correlation id is present and consistent.
- Secrets are redacted (no token/secret in output).
- Logging failure does not crash the business flow.

## Files

- `Code/src/XiangqiOnline.Persistence/Logging/CorrelationContext.cs`
- `Code/src/XiangqiOnline.Persistence/Logging/LoggingSetup.cs`
- `Code/src/XiangqiOnline.Persistence/Logging/SecretRedactor.cs`
- `Code/tests/XiangqiOnline.IntegrationTests/Logging/Tv6LoggingTests.cs`
