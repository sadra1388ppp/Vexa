# MariaDB setup and Pomelo scaffolding

Run terminal commands from the directory containing `Vexa.slnx`.

## Versions and configuration

The server remains on .NET 10. EF Core, EF Design, and the local `dotnet-ef` tool use 9.0.17; Pomelo uses 9.0.0. Keep the EF packages and tool on that compatible major version when updating dependencies. [Pomelo 9 release notes](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/releases).

The connection string and JWT signing key in `Vexa.Server/appsettings.json` are intentionally empty. Supply your values through environment variables or your deployment's configuration.

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | MariaDB host, port, database, and credentials |
| `Database__ServerVersion` | Actual MariaDB version, such as `11.8.3`; default is `11.8.0` |
| `Jwt__Key` | Stable random JWT signing key, at least 32 bytes |
| `Owner__Username` | Existing owner policy setting; default remains `BlackRoom` |

The desktop client also recognizes `BlackRoom` for its owner UI. Changing the owner name therefore needs a corresponding client update.

The app and its EF design-time factory use `DatabaseConfiguration.UseVexaDatabase`. This configures `UseMySql` with an explicit `MariaDbServerVersion`. MySqlConnector returns stored timestamps with `DateTimeKind.Utc`; MariaDB `DATETIME(6)` itself does not store a timezone. Continue writing UTC values.

## Create a fresh database

Choose one setup method. Neither method upgrades an existing schema.

### Method A: supplied MariaDB schema

Open the MariaDB client as a schema administrator, from the solution directory:

```text
mariadb --user=root --password
```

Execute:

```sql
CREATE DATABASE novachat
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE novachat;
SOURCE database/mariadb-schema.sql;
```

`SOURCE` is a MariaDB client command. If your client runs elsewhere, give it the full path to the SQL file; a GUI can open and execute that file against the selected database.

For a server and MariaDB on the same host, create an application account, replacing the example password:

```sql
CREATE USER 'novachat'@'localhost'
    IDENTIFIED BY 'REPLACE_WITH_YOUR_DATABASE_PASSWORD';
GRANT SELECT, INSERT, UPDATE, DELETE
    ON novachat.* TO 'novachat'@'localhost';
```

For separate hosts, use the appropriate MariaDB account host and connection settings. Normal application startup only reads the schema, so the runtime account does not need table-alter privileges.

The script creates `Users`, `Chats`, `ChatMembers`, `Messages`, and `Contacts`, with InnoDB foreign keys and indexes. It contains no drops or data import. Existing table names cause a creation error.

### Method B: EF Core initialization

Set the connection below using a schema administrator account with permission to create the target schema. Then run:

```powershell
dotnet run --project Vexa.Server --no-launch-profile -- --initialize-database
```

This invokes EF Core `EnsureCreatedAsync`, validates the mapped tables, and exits without starting HTTP. A JWT key is not needed for this command; the existing ImageSharp build requirement still applies.

After initialization, configure the ordinary runtime account. For an existing database with tables, `EnsureCreatedAsync` does not apply alterations or repair a partial schema. Startup validation checks readability, not every index or constraint.

Do not use `dotnet ef database update` for this database-first workflow. The uploaded migration files were comment-only remnants and have been removed; no runnable migration history is supplied.

## Configure and run

Windows PowerShell:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Server=localhost;Port=3306;Database=novachat;User ID=novachat;Password=YOUR_DATABASE_PASSWORD;'
$env:Database__ServerVersion = '11.8.3'

# Generate a signing key once, then retain it in your private configuration.
$jwtBytes = New-Object byte[] 64
$jwtGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$jwtGenerator.GetBytes($jwtBytes)
$env:Jwt__Key = [Convert]::ToBase64String($jwtBytes)
$jwtGenerator.Dispose()

dotnet run --project Vexa.Server --launch-profile http
```

Linux/macOS:

```bash
export ConnectionStrings__DefaultConnection='Server=localhost;Port=3306;Database=novachat;User ID=novachat;Password=YOUR_DATABASE_PASSWORD;'
export Database__ServerVersion='11.8.3'
export Jwt__Key="$(openssl rand -base64 64)"
dotnet run --project Vexa.Server --launch-profile http
```

Keep your chosen JWT key stable across restarts and server instances; regenerating it invalidates previously issued tokens. Do not put actual credentials or the signing key in generated source. Replace the example version with your MariaDB version.

The connection must name a database. The checked-in launch profile uses port 5256; the WPF client already targets that address. Missing connection, JWT configuration, or unreadable schema stops startup with an error.

## Scaffold entities after a database change

1. Apply your reviewed schema change to MariaDB.
2. Set `ConnectionStrings__DefaultConnection` to that database.
3. Run the script for your platform.
4. Review the generated files, adjust any affected service/DTO code, build, and run the database checks on a fresh test database.

Windows:

```powershell
.\scripts\scaffold-db.ps1
```

Linux/macOS:

```bash
bash scripts/scaffold-db.sh
```

The scripts restore the repository-local `dotnet-ef` tool and use this underlying provider command:

```text
dotnet ef dbcontext scaffold Name=ConnectionStrings:DefaultConnection Pomelo.EntityFrameworkCore.MySql
```

The complete command in each script also selects the standalone scaffolding project, the five table names, namespaces, output directories, `--force`, and `--no-onconfiguring`. Use the script to preserve these options.

The scaffolding host reads environment configuration and has no reference to the server project. Consequently, an old entity definition that no longer builds, or the server's ImageSharp license check, does not prevent reverse engineering. No JWT key is needed for scaffolding.

Pomelo writes into a temporary staging directory first. The scripts check that all five expected entity files and the context exist before replacing the generated files. A provider failure or missing required entity leaves the current generated model untouched. Generation staging is removed afterward.

| Location | Ownership |
| --- | --- |
| `Vexa.Server/Entities/Generated/*.cs` | Regenerated by Pomelo |
| `Vexa.Server/Data/Generated/AppDbContext.cs` | Regenerated by Pomelo |
| `Vexa.Server/Entities/EntityDefaults.cs` | Custom partial constructors; preserved |
| `Vexa.Server/Entities/ChatEnums.cs` | Domain enums; preserved |
| `Vexa.Server/Data/DatabaseConfiguration.cs` | Provider and UTC configuration; preserved |
| `Vexa.Server/Data/AppDbContextFactory.cs` | Design-time configuration; preserved |

Add custom entity behavior in separate partial files. For custom context model configuration, implement `OnModelCreatingPartial(ModelBuilder)` in another `partial AppDbContext` file. Do not edit the generated output directly.

MariaDB `INT` columns scaffold as `int`, so `Chat.Type` and `ChatMember.Role` are integers. Services compare them with integer enum constants; API responses cast back to enums to retain the existing `Private`/`Group` and `Member`/`Admin`/`Owner` strings. Keep the stored numeric values: chat types 0/1, roles 0/1/2.

Renaming a column or changing a type can require application edits after scaffolding. Adding another mapped table requires updating both scripts' table selection and expected-file checks. Keep the reference schema SQL synchronized with database changes used for future installations.

### Visual Studio Package Manager Console

The PowerShell script can also be launched from Package Manager Console at the solution directory. The scaffolding project includes `Microsoft.EntityFrameworkCore.Tools` if you prefer the native PMC command:

```powershell
Scaffold-DbContext 'Name=ConnectionStrings:DefaultConnection' Pomelo.EntityFrameworkCore.MySql -Project Vexa.Scaffolding -StartupProject Vexa.Scaffolding -Context AppDbContext -ContextDir .generated/Data -OutputDir .generated/Entities -Namespace Vexa.Server.Entities -ContextNamespace Vexa.Server.Data -Tables Users,Chats,ChatMembers,Messages,Contacts -NoOnConfiguring -Force
```

This direct command only stages output under `tools/Vexa.Scaffolding/.generated`; it does not perform the scripts' checks or copy the result into the server. Prefer the scripts for repeatable updates.

See Microsoft's [reverse engineering documentation](https://learn.microsoft.com/en-us/ef/core/managing-schemas/scaffolding/) and [EF CLI reference](https://learn.microsoft.com/en-us/ef/core/cli/dotnet).

## Existing PostgreSQL or partially converted MariaDB data

This deliverable converts the project and supplies a target schema. No database contents or database dump were supplied, so it does not perform a live data migration.

The uploaded model used CLR `long` user IDs converted into `VARCHAR(255)` columns. This version uses native signed `BIGINT` IDs with `AUTO_INCREMENT`. Do not scaffold the old schema straight into this version: varchar IDs will become strings and the application will no longer match them.

Use a backup and a separate destination database for the data transfer:

1. Inventory the real source columns, foreign keys, row counts, timestamps, and migration state. The old comment-only migration files cannot reconstruct that state.
2. Create the target schema. Preserve existing positive numeric IDs when possible. If IDs are UUIDs, usernames, or other nonnumeric strings, create an explicit old-to-new numeric ID map.
3. Apply the ID mapping to `Users.Id`, all three user references in `Chats`, `ChatMembers.UserId`, `Messages.SenderId`, both contact user references, and tokens inside `Messages.DeletedForUserIds`. Chat IDs and their references must also remain consistent.
4. Resolve collisions under `utf8mb4_unicode_ci`, which is case/accent insensitive. Check unique usernames, emails, phone numbers, contact pairs, and chat-member pairs before import. Empty optional phone values should become NULL where appropriate.
5. Validate string widths against the new schema, especially username 32, display name 50, email 254, bio 160, and password hash 512. Preserve existing password hashes exactly.
6. Import parents before dependents: users, chats, then memberships/messages/contacts. Preserve group types and roles, and keep private participant IDs NULL on group rows. Establish any genuinely missing legacy memberships once as part of the reviewed migration; blindly recreating them can unhide intentionally hidden conversations.
7. Convert timestamp values to UTC before writing `DATETIME(6)`. Transfer booleans as 0/1. Preserve message text, media envelope JSON, and deletion lists. Copy `wwwroot/uploads` separately because media files are not stored in database rows.
8. Ensure every auto-increment counter exceeds its imported maximum ID. Do not copy PostgreSQL's EF migration history as MariaDB migration history. If IDs changed, invalidate existing sessions.
9. Compare counts, foreign-key relationships, hashes, Unicode text, dates, visibility, and media links. Scaffold from the validated MariaDB schema, compile, and verify the app before switching its connection string.

This intentionally avoids an automatic `ALTER COLUMN` or data-drop path that could corrupt IDs or relationships without knowing the source data.
