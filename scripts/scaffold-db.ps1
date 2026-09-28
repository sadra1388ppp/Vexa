$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tools/Vexa.Scaffolding/Vexa.Scaffolding.csproj'
$stage = Join-Path $root 'tools/Vexa.Scaffolding/.generated'
if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__DefaultConnection)) {
    throw 'Set ConnectionStrings__DefaultConnection to the MariaDB database to scaffold.'
}

Push-Location $root
try {
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }
    $arguments = @(
        'ef', 'dbcontext', 'scaffold', 'Name=ConnectionStrings:DefaultConnection',
        'Pomelo.EntityFrameworkCore.MySql', '--project', $project, '--startup-project', $project,
        '--context', 'AppDbContext', '--context-dir', (Join-Path $stage 'Data'),
        '--output-dir', (Join-Path $stage 'Entities'),
        '--namespace', 'Vexa.Server.Entities', '--context-namespace', 'Vexa.Server.Data',
        '--table', 'Users', '--table', 'Chats', '--table', 'ChatMembers',
        '--table', 'Messages', '--table', 'Contacts', '--no-onconfiguring', '--force'
    )
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Pomelo scaffolding failed; current entities were not overwritten.' }
    foreach ($entity in @('User', 'Chat', 'ChatMember', 'Message', 'Contact')) {
        if (-not (Test-Path (Join-Path $stage "Entities/$entity.cs"))) {
            throw "Missing generated entity: $entity. Current entities were not overwritten."
        }
    }
    if (-not (Test-Path (Join-Path $stage 'Data/AppDbContext.cs'))) { throw 'DbContext was not generated.' }
    $entities = Join-Path $root 'Vexa.Server/Entities/Generated'
    $data = Join-Path $root 'Vexa.Server/Data/Generated'
    New-Item -ItemType Directory -Force -Path $entities, $data | Out-Null
    Copy-Item (Join-Path $stage 'Entities/*.cs') $entities -Force
    Copy-Item (Join-Path $stage 'Data/AppDbContext.cs') (Join-Path $data 'AppDbContext.cs') -Force
    Write-Host 'Entities and DbContext updated. Review the generated diff, then build the solution.'
}
finally {
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    Pop-Location
}
