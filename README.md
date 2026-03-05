# dsql-efcore-example

A proof-of-concept project that tests **EF Core with Amazon Aurora DSQL** — exploring compatibility, limitations, and workarounds when using the Npgsql/PostgreSQL provider against DSQL.

## Background

Aurora DSQL is a serverless, distributed SQL database from AWS that speaks the PostgreSQL wire protocol but differs in important ways:

- **No foreign keys** — DSQL does not support foreign key constraints.
- **Async indexes** — Indexes are created asynchronously (`CREATE INDEX ASYNC`), which is incompatible with EF Core's default migration SQL.
- **No `DISCARD ALL`** — Connection reset commands are unsupported; `NoResetOnClose` must be enabled on Npgsql.
- **No serial/identity columns** — Auto-increment primary keys are not available.

## Key Findings

| Area | Result |
|---|---|
| **EF Core Migrations** | Not usable. `CREATE INDEX` syntax is incompatible with DSQL. All index/constraint definitions were removed from EF Core model configuration — only `[Key]` attributes remain. |
| **DbUp Migrations** | Works with a custom DSQL provider. The default PostgreSQL provider fails on schema version table creation (serial column), so `DsqlTableJournal` and `DsqlExtensions` were implemented. |
| **EF Core CRUD** | Works normally via Npgsql once migrations are handled externally. |
| **IAM Auth Tokens** | `DsqlAuthTokenProvider` generates and caches tokens automatically with configurable refresh intervals. |

## Project Structure

```
GameBackend.sln
├── GameBackend.AppHost        # .NET Aspire orchestrator — provisions DSQL via AWS CDK
├── GameBackend                # ASP.NET Core Minimal API (the backend server)
├── GameBackend.Migrations     # DbUp-based SQL migrations with custom DSQL provider
└── GameBackend.ServiceDefaults # Aspire service defaults (OpenTelemetry, health checks)
```

### GameBackend (API Server)

Minimal API with the following endpoints:

| Method | Path | Description |
|---|---|---|
| `POST` | `/efcore/players` | Create a player |
| `GET` | `/efcore/players` | List all players |
| `GET` | `/efcore/players/{id}` | Get a player by ID |
| `GET` | `/efcore/players/{id}/profile` | Get player profile with stats |
| `POST` | `/efcore/players/{id}/match-results` | Submit a match result (updates stats & Elo rating) |

Models:
- **Player** — `id` (UUIDv7), `name`, `created_at`, `updated_at`
- **PlayerStat** — match statistics, win/loss/draw, KDA, Elo rating (K=32, simplified)

### GameBackend.Migrations

Uses [DbUp](https://dbup.readthedocs.io/) with a custom DSQL provider:

- `DsqlExtensions` — Registers the DSQL-compatible upgrade engine (replaces serial-based journal).
- `DsqlTableJournal` — Overrides schema version table DDL and insert logic to avoid serial columns.
- SQL scripts in `Migrations/` are embedded resources, executed in alphabetical order.

### GameBackend.AppHost

.NET Aspire AppHost that:

1. Provisions a DSQL cluster via AWS CDK (`CfnCluster`).
2. Runs migrations before starting the backend (`WaitForCompletion`).
3. Passes the cluster endpoint and AWS credentials to both projects.

Since DSQL is serverless, the development cluster incurs near-zero cost when idle.

### DSQL Authentication

`DsqlAuthTokenProvider` handles IAM-based password authentication:

- Generates auth tokens via `DSQLAuthTokenGenerator`.
- Caches tokens with configurable expiry (default 15 min) and refresh buffer.
- Integrates with Npgsql's `UsePeriodicPasswordProvider` for automatic rotation.
- Resolves AWS credentials from profile (`AWS_PROFILE`) or default credential chain.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [AWS CLI](https://aws.amazon.com/cli/) configured with valid credentials
- [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling)
- [k6](https://k6.io/) (for load testing)

## Getting Started

### Run with Aspire (recommended)

```bash
dotnet run --project GameBackend.AppHost
```

This will:
1. Provision a DSQL cluster in your AWS account.
2. Run database migrations.
3. Start the API server with Swagger UI available at the displayed URL.

### Configuration

DSQL options can be set via `appsettings.json` or environment variables:

```json
{
  "Dsql": {
    "ClusterEndpoint": "<cluster>.dsql.<region>.on.aws",
    "Region": "us-east-1",
    "TokenExpiryMinutes": 15,
    "TokenRefreshBufferMinutes": 2,
    "MaxPoolSize": 100
  }
}
```

AWS settings for the AppHost:

```json
{
  "AWS": {
    "Region": "us-east-1",
    "Profile": "your-profile"
  }
}
```

## Load Testing

Load tests use [k6](https://k6.io/) and are located in `k6/load-test.js`.

```bash
k6 run k6/load-test.js
```

To target a specific server:

```bash
k6 run -e BASE_URL=http://localhost:5074 k6/load-test.js
```

The test runs two scenarios:
- **Smoke** — 5 VUs for 30 seconds
- **Load** — Ramps from 0 to 50 VUs over 3.5 minutes

Each iteration creates a player, lists players, fetches a player, submits 3 match results, and retrieves the player profile.

### Test Results

Tested against a DSQL cluster with 50 concurrent virtual users:

```
Thresholds:
  ✓ error rate < 0.1%     (actual: 0.00%)
  ✓ p(95) < 500ms         (actual: 95.97ms)
  ✓ p(99) < 1000ms        (actual: 110.82ms)

Endpoint Latency (avg):
  create player .... 18.95ms
  get player ....... 8.96ms
  list players ..... 87.86ms
  submit match ..... 32.83ms
  get profile ...... 95.74ms

Summary:
  36,106 requests | 0% failure | 146 req/s | 2,579 iterations
```

## Tech Stack

- .NET 10 / ASP.NET Core Minimal API
- Entity Framework Core + Npgsql (PostgreSQL provider)
- Amazon Aurora DSQL
- DbUp (database migrations)
- .NET Aspire + AWS CDK (orchestration & infrastructure)
- OpenTelemetry (observability)
- k6 (load testing)
