$ErrorActionPreference = "Stop"

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:VEXA_ENVIRONMENT = "Development"

Write-Host "Starting Vexa Development..." -ForegroundColor Cyan
Write-Host "Server: http://localhost:5256" -ForegroundColor Gray
Write-Host ""

dotnet run --project .\Vexa.Server --launch-profile http
