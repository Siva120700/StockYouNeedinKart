# StockYouNeed

Separate **backend** (Angel sync + analysis) and **thin frontend** (display + actions only).

**Backend architecture & flow diagrams:** [docs/BACKEND_FLOW.md](docs/BACKEND_FLOW.md)

**Interview system design (UML + how to answer):** [docs/INTERVIEW_SYSTEM_DESIGN.md](docs/INTERVIEW_SYSTEM_DESIGN.md)

## Layout

```
database/          PostgreSQL schema (001 + 002)
backend/           .NET 8 solution
  src/StockYouNeed.Api       GraphQL API (reads Postgres only)
  src/StockYouNeed.Worker    Daily token sync, 10-day bars, LTP poller
  src/StockYouNeed.Application
  src/StockYouNeed.Domain
  src/StockYouNeed.Infrastructure
frontend/          React + Vite
  src/zen_components/  ZenPrimaryLayout, ZenTable, column/field factories
  src/pages/           LTP / Signals / Positions
```

## Quick start

### 1. Postgres

```bash
docker compose up -d
```

### 2. Angel credentials (Worker)

Copy the example and fill in your SmartAPI details (never commit real keys):

```powershell
copy backend\src\StockYouNeed.Worker\appsettings.Development.local.json.example backend\src\StockYouNeed.Worker\appsettings.Development.local.json
copy backend\src\StockYouNeed.Worker\appsettings.Development.local.json backend\src\StockYouNeed.Api\appsettings.Development.local.json
```

Edit both `appsettings.Development.local.json` files (`*.local.json` is gitignored):

```json
{
  "Angel": {
    "Enabled": true,
    "ApiKey": "...",
    "ClientCode": "...",
    "Password": "...",
    "TotpSecret": "..."
  }
}
```

Get these from Angel SmartAPI portal (API key + Enable TOTP secret). Ensure your **Primary Static IP** in SmartAPI matches your machine’s public IP.

### 3. Run (recommended)

**Terminal 1 — frontend + GraphQL Api:**

```bash
cd frontend
npm install
npm run dev
```

**Terminal 2 — Worker (optional, for Angel sync / LTP):**

```bash
npm run dev:worker
```

Or press **F5** with launch config **Worker** to debug the worker (does **not** start Api).

Open `http://localhost:5173`. GraphQL Api: `http://localhost:5080/graphql`.

| Script | What it starts |
|--------|----------------|
| `npm run dev` | **Api + frontend** (main dev command) |
| `npm run dev:web` | Frontend only |
| `npm run dev:api` | GraphQL Api only |
| `npm run dev:worker` | Background worker only |
| `npm run dev:all` | Api + Worker + frontend |

**F5 (Run and Debug)** starts **Worker only**. Api is always started by `npm run dev`, not by F5.

Worker on start: seed Nifty symbols → (if Angel enabled) download scrip master → map tokens → pull ~10 daily bars. During market hours it polls LTP into `market_ltp`.

### Build error CS2012 (file locked)

Api/Worker is still running and blocks DLL overwrite. Fix:

```powershell
powershell -File scripts/stop-backend.ps1
dotnet build backend/StockYouNeed.sln
```

Or **Shift+F5** before rebuilding Worker. `npm run dev` owns the Api — F5 only debugs Worker and will not stop the Api.

## Architecture rule

| Layer | Owns |
|-------|------|
| Worker | Angel download, token map, 10-day `market_bars`, LTP cache |
| Api | GraphQL reads from DB; `runAnalysis` → FULL → `market_ohlc` + signals |
| Frontend | Show DTO rows; call mutations (`Run`, Open, Close). No Angel keys, no strategy math |

~100 users scale by **sharing** the market cache — users never call Angel directly.

## Dev user

Until real auth is wired, Api uses demo user  
`11111111-1111-1111-1111-111111111111`  
(override with header `X-User-Id`).
