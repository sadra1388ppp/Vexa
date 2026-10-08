# Vexa Staging Environment

The `staging` branch contains the isolated test configuration.

## Environment layout

| Environment | Server | Client variable |
| --- | --- | --- |
| Development | `http://localhost:5256` | `VEXA_ENVIRONMENT=Development` |
| Staging | `http://localhost:5257` | `VEXA_ENVIRONMENT=Staging` |

The WPF client shows the active environment in its header.

## Start the server

Development:

```powershell
dotnet run --project .\Vexa.Server --launch-profile http
```

Staging:

```powershell
dotnet run --project .\Vexa.Server --launch-profile staging
```

The Staging server uses `ASPNETCORE_ENVIRONMENT=Staging`.

## Start the client

Development:

```powershell
dotnet run --project .\Vexa.Client --launch-profile development
```

Staging:

```powershell
dotnet run --project .\Vexa.Client --launch-profile staging
```

The client resolves its server URL centrally from `VEXA_ENVIRONMENT`. Staging uses port 5257 and Development uses port 5256.

## Staging secrets

Do not commit credentials or signing keys. Supply these through deployment/environment configuration:

```text
ASPNETCORE_ENVIRONMENT=Staging
ConnectionStrings__DefaultConnection=<staging MariaDB connection>
Database__ServerVersion=<actual MariaDB version>
Jwt__Key=<stable random signing key>
FileSecurity__Cloudmersive__ApiKey=<Cloudmersive key>
```

The JWT key must remain stable for the lifetime of the Staging deployment; changing it invalidates existing Staging tokens.

## Database

The Staging database is intentionally not configured in source yet. Create and configure the separate MariaDB database before the first real end-to-end Staging run.

## Deployment checklist

1. Set `ASPNETCORE_ENVIRONMENT=Staging`.
2. Configure the Staging MariaDB connection.
3. Configure a stable Staging JWT key.
4. Configure the Staging Cloudmersive key when executable scanning is enabled.
5. Start the Server with the Staging profile.
6. Start the Client with the Staging profile.
7. Verify `/api/system/environment` returns `Staging`.
8. Verify login, API calls, SignalR, media, and uploads.
9. Never point Staging at the Production database.
