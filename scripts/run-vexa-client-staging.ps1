$ErrorActionPreference = "Stop"

$env:VEXA_ENVIRONMENT = "Staging"

Write-Host "Starting Vexa Client (Staging)..." -ForegroundColor Yellow
Write-Host "Target server: http://localhost:5257" -ForegroundColor Gray
Write-Host ""

dotnet run --project .\Vexa.Client --launch-profile staging
