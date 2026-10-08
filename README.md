# Vexa — MariaDB / EF Core

Vexa contains an ASP.NET Core API and SignalR server, a Windows WPF client, and a small web/PWA client. This version uses MariaDB through Pomelo and EF Core, with a database-first workflow for updating entities.

## Start here

1. Install the .NET 10 SDK and MariaDB. This conversion was tested with MariaDB 11.8.3.
2. Follow [MariaDB setup and scaffolding](docs/MARIADB.md) to create the database and configure the connection and JWT key.
3. Supply the license required by the existing ImageSharp 4.1.1 package before building the server. See [validation results](docs/VALIDATION.md).
4. Start the server from this directory:

   ```powershell
   dotnet run --project Vexa.Server --launch-profile http
   ```

5. On Windows, open `Vexa.slnx` in a .NET 10 capable Visual Studio, or run:

   ```powershell
   dotnet run --project Vexa.Client
   ```

The desktop client targets `http://localhost:5256`. The HTTP launch profile also exposes Swagger at `http://localhost:5256/swagger`. The bundled web client is an older prototype; its existing API mismatches are listed in the project review.

## Development and Staging

The `staging` branch provides an isolated local test setup. The environments use different ports and the WPF client shows the active environment in its login and main headers.

Development:

```powershell
.\scripts\run-vexa-development.ps1
```

Client in Development:

```powershell
dotnet run --project .\Vexa.Client --launch-profile development
```

Staging:

```powershell
.\scripts\run-vexa-staging.ps1
```

Client in Staging:

```powershell
.\scripts\run-vexa-client-staging.ps1
```

Staging uses `ASPNETCORE_ENVIRONMENT=Staging`, server port `5257`, and client variable `VEXA_ENVIRONMENT=Staging`. Development uses port `5256`. See [Staging documentation](docs/STAGING.md) for secret configuration and the final database step.

## Update entities from MariaDB

Set `ConnectionStrings__DefaultConnection` in your terminal, make the intended schema change in MariaDB, then run:

Windows PowerShell:

```powershell
.\scripts\scaffold-db.ps1
```

Linux/macOS:

```bash
bash scripts/scaffold-db.sh
```

These scripts use `dotnet ef dbcontext scaffold` with `Pomelo.EntityFrameworkCore.MySql`. They regenerate `Entities/Generated` and `Data/Generated/AppDbContext.cs`. Custom constructors and domain enums are kept in separate partial-class files.

Scaffolding reads the database; it does not change its schema or import PostgreSQL data. Existing data needs a deliberate migration; [the migration guide](docs/MARIADB.md#existing-postgresql-or-partially-converted-mariadb-data) explains the required mappings.

## What changed

- Shared MariaDB configuration for runtime and EF design tools, with UTC dates.
- Database-generated `BIGINT` user IDs and matching foreign keys.
- EF LINQ, `EF.Functions.Like`, `SaveChangesAsync`, `ExecuteUpdateAsync`, and transactional `ExecuteDeleteAsync` for persistence.
- Removal of the raw SQL startup schema repair and obsolete migration placeholders.
- One EF save for a new conversation and its members.
- An independent Pomelo scaffolding project that can run even when the server's previous model does not build.
- A fresh-database SQL schema, explicit EF initialization command, and repeatable MariaDB integration checks.

| Component | Version |
| --- | --- |
| Target framework | .NET 10 |
| EF Core and local dotnet-ef tool | 9.0.17 |
| Pomelo provider | 9.0.0 |
| Database used for validation | MariaDB 11.8.3 |

The EF/Pomelo versions stay on the compatible 9.x line. See [Pomelo's release documentation](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/releases).

Read [the project review](docs/PROJECT_REVIEW.md) for architecture and remaining pre-existing issues, and [validation results](docs/VALIDATION.md) for exactly what was checked.
