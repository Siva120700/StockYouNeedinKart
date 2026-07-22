# StockYouNeed — Backend flow

This document describes how the .NET backend is structured and how data moves through **Worker**, **Api**, **Postgres**, and **Angel One SmartAPI**.

Related code:

- [`backend/src/StockYouNeed.Worker`](../backend/src/StockYouNeed.Worker) — background sync
- [`backend/src/StockYouNeed.Api`](../backend/src/StockYouNeed.Api) — GraphQL for the frontend
- [`backend/src/StockYouNeed.Application`](../backend/src/StockYouNeed.Application) — use cases
- [`backend/src/StockYouNeed.Infrastructure`](../backend/src/StockYouNeed.Infrastructure) — Angel client + Postgres
- [`database/001_init.sql`](../database/001_init.sql) / [`database/002_angel_market_data.sql`](../database/002_angel_market_data.sql)

---

## 1. Solution layout

Two runnable processes share the same domain/application/infrastructure libraries.

```mermaid
flowchart TB
  subgraph processes [Runnable processes]
    Api[StockYouNeed.Api<br/>GraphQL]
    Worker[StockYouNeed.Worker<br/>Background jobs]
  end

  subgraph shared [Shared libraries]
    App[StockYouNeed.Application<br/>Services / use cases]
    Domain[StockYouNeed.Domain<br/>Models / enums]
    Infra[StockYouNeed.Infrastructure<br/>Angel + Dapper]
  end

  Api --> App
  Worker --> App
  App --> Domain
  Api --> Infra
  Worker --> Infra
  Infra --> Domain
```

| Project | Role |
|--------|------|
| **Api** | Serves React; reads Postgres; `runAnalysis` may call Angel FULL |
| **Worker** | Daily token sync, 10-day bars, market-hours LTP poll |
| **Application** | `TokenSyncService`, `MarketBarsSyncService`, `LtpPollService`, `AnalysisRunService` |
| **Domain** | DTOs / row models |
| **Infrastructure** | `AngelMarketDataClient`, repositories, SQL migrator |

**Why Api ≠ Worker:** long Angel syncs must not block user GraphQL requests. ~100 users share one market cache in Postgres.

---

## 2. System overview

```mermaid
flowchart LR
  FE[React frontend]
  Api[StockYouNeed.Api]
  Worker[StockYouNeed.Worker]
  PG[(PostgreSQL)]
  Angel[Angel One SmartAPI]

  FE -->|GraphQL queries / mutations| Api
  Api -->|read cache| PG
  Api -->|runAnalysis FULL only| Angel
  Worker -->|LTP / candles / scrip master| Angel
  Worker -->|write cache| PG
  Api -->|write signals / positions| PG
```

**Hard rule:** GraphQL list/read queries never call Angel. Users always hit the shared DB cache.

---

## 3. Startup sequence

Runs when **Api** or **Worker** starts.

```mermaid
sequenceDiagram
  participant Host as Api or Worker
  participant Mig as DatabaseMigrator
  participant DB as PostgreSQL
  participant Seed as UniverseSeedService

  Host->>Mig: MigrateAsync
  Mig->>DB: Apply 001_init.sql if needed
  Mig->>DB: Apply 002_angel_market_data.sql if needed
  Host->>DB: Ensure demo user
  Host->>Seed: SeedAsync Nifty 50 / 100
  Seed->>DB: instruments + universe_memberships
```

---

## 4. Worker — daily sync pipeline

`DailySyncHostedService` runs shortly after start, then again around **08:00 IST** (configurable).

```mermaid
flowchart TD
  start[Worker starts / daily hour IST] --> demo[Ensure demo user]
  demo --> seed[UniverseSeedService<br/>Nifty symbols]
  seed --> tokens[TokenSyncService]
  tokens --> download[Download OpenAPIScripMaster.json]
  download --> match[Match NSE -EQ to instruments]
  match --> map[(angel_instrument_map)]
  map --> bars[MarketBarsSyncService]
  bars --> candles[Angel historical ONE_DAY]
  candles --> mb[(market_bars<br/>last ~10 trading days)]
  mb --> done[Daily sync finished]
```

### Token sync detail

```mermaid
flowchart LR
  U[universe_memberships<br/>nifty_50 / nifty_100] --> I[instruments]
  I --> M[Match by symbol name]
  S[Angel scrip master JSON] --> M
  M --> AIM[(angel_instrument_map<br/>exchange + symbol_token)]
```

Quote / history APIs need Angel **tokens**, not app UUIDs. The map is the bridge.

---

## 5. Worker — LTP poll (market hours)

`LtpPollHostedService` loops during NSE cash hours (approx 09:00–15:35 IST, weekdays).

```mermaid
flowchart TD
  loop[Every few seconds] --> hours{Market hours IST?}
  hours -->|no| sleep[Wait]
  sleep --> loop
  hours -->|yes| load[Load tokens from<br/>angel_instrument_map]
  load --> chunk[Chunk ≤ 50 tokens]
  chunk --> ltp[Angel mode LTP]
  ltp --> upsert[(market_ltp upsert)]
  upsert --> rate[Delay ~1.1s<br/>Angel rate limit]
  rate --> chunk
```

Frontend / positions MTM read **`market_ltp` only** — never Angel per user.

---

## 6. Api — read path (queries)

All of these hit Postgres only.

```mermaid
flowchart TB
  Q[GraphQL Query]
  Q --> me[me]
  Q --> ltp[ltp]
  Q --> bars[marketBars]
  Q --> univ[universes]
  Q --> sig[signals]
  Q --> pos[openPositions]
  Q --> wl[watchlist]

  me --> users[(users)]
  ltp --> market_ltp[(market_ltp)]
  bars --> market_bars[(market_bars)]
  univ --> instruments[(instruments + memberships)]
  sig --> analysis_signals[(analysis_signals)]
  pos --> positions[(positions)]
  pos --> market_ltp
  wl --> watchlist[(user_watchlist_items)]
```

`openPositions` also refreshes marks from `market_ltp` before returning.

---

## 7. Api — Run analysis path (mutation)

Triggered when the user clicks **Run** in the UI (`runAnalysis`).

```mermaid
flowchart TD
  click[Frontend: Run analysis] --> mut[Mutation runAnalysis]
  mut --> runRow[Create analysis_runs row]
  runRow --> full[Angel mode FULL<br/>chunks of 50]
  full --> ohlc[(market_ohlc<br/>ltp + OHLC + trade_volume)]
  ohlc --> engine[AnalysisRunService<br/>reads market_bars]
  engine --> rules{Breakout + volume rules}
  rules -->|signal| signals[(analysis_signals)]
  rules -->|no signal| skip[Skip instrument]
  signals --> done[Mark run succeeded]
  done --> ui[Frontend refreshes signals]
```

**Why FULL on Run?** Angel OHLC mode has no volume; FULL provides `tradeVolume`. Depth is still ignored (tables reserved for later).

---

## 8. Position / watchlist actions

```mermaid
flowchart LR
  subgraph mutations [GraphQL mutations]
    open[openPositionFromSignal]
    close[closePosition]
    sl[updateStopLoss]
    addWl[addToWatchlist]
    rmWl[removeFromWatchlist]
  end

  open --> positions[(positions)]
  close --> positions
  sl --> positions
  sl --> events[(position_stop_loss_events)]
  addWl --> wl[(user_watchlist_items)]
  rmWl --> wl
```

No Angel order placement yet — this is the paper book. Live execution can reuse the same Angel client later.

---

## 9. End-to-end timeline (typical day)

```mermaid
sequenceDiagram
  participant W as Worker
  participant A as Angel
  participant DB as Postgres
  participant Api as Api
  participant FE as Frontend

  Note over W: ~08:00 IST or on process start
  W->>A: Download scrip master
  W->>DB: Upsert angel_instrument_map
  W->>A: Historical daily candles
  W->>DB: Upsert market_bars last 10 days

  Note over W: Market hours
  loop Every poll interval
    W->>A: Quote mode LTP
    W->>DB: Upsert market_ltp
  end

  FE->>Api: Query ltp / positions
  Api->>DB: SELECT cache
  Api-->>FE: DTOs

  FE->>Api: Mutation runAnalysis
  Api->>A: Quote mode FULL
  Api->>DB: Upsert market_ohlc
  Api->>DB: Insert analysis_signals
  Api-->>FE: Run result
  FE->>Api: Query signals
  Api->>DB: SELECT signals
  Api-->>FE: Signal list
```

---

## 10. Table ownership cheat sheet

| Table | Written by | Read by |
|-------|------------|---------|
| `instruments` / `universe_memberships` | Seed (Worker/Api start) | Worker + Api |
| `angel_instrument_map` | Worker token sync | Worker LTP/bars; Api Run |
| `market_bars` | Worker daily bars | Analysis engine |
| `market_ltp` | Worker LTP poll | Api queries / MTM |
| `market_ohlc` | Api `runAnalysis` | Debugging / future UI |
| `analysis_runs` / `analysis_signals` | Api analysis | Frontend |
| `positions` / watchlist | Api mutations | Frontend |

Reserved / unused for now: `market_quotes_full`, `market_quote_depth`.

---

## 11. Config knobs

See `appsettings.json` on Api and Worker:

| Section | Purpose |
|---------|---------|
| `Database:ConnectionString` | Postgres |
| `Angel:Enabled` | Gate live Angel calls |
| `Angel:ApiKey` / `ClientCode` / `Password` / `TotpSecret` | SmartAPI login |
| `WorkerSchedule:DailySyncHourIst` | Daily pipeline hour |
| `WorkerSchedule:LtpPollIntervalSeconds` | LTP loop cadence |
| `WorkerSchedule:MarketBarsLookbackDays` | History window (default 10) |
| `DevAuth:DemoUserId` | Temporary tenancy until real auth |

---

## 12. Mental model

```text
Worker fills the cache  →  Api serves the cache  →  Run refreshes OHLC+volume & writes signals
Frontend only displays data and fires mutations
```
