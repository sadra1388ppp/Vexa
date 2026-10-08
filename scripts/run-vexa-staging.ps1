$ErrorActionPreference = "Stop"

$env:ASPNETCORE_ENVIRONMENT = "Staging"
$env:VEXA_ENVIRONMENT = "Staging"

Write-Host "Starting Vexa Staging..." -ForegroundColor Yellow
Write-Host "Server: http://localhost:5257" -ForegroundColor Gray
Write-Host "Client: VEXA_ENVIRONMENT=Staging" -ForegroundColor Gray
Write-Host ""

dotnet run --project .\Vexa.Server --launch-profile staging
