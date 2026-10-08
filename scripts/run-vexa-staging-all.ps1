$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

Write-Host "Starting Vexa Staging Server..." -ForegroundColor Yellow

Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-NoProfile",
    "-Command",
    "Set-Location '$root'; & '.\scripts\run-vexa-staging.ps1'"
)

Start-Sleep -Seconds 2

Write-Host "Starting Vexa Staging Client..." -ForegroundColor Cyan

Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-NoProfile",
    "-Command",
    "Set-Location '$root'; & '.\scripts\run-vexa-client-staging.ps1'"
)

Write-Host ""
Write-Host "Vexa Staging Server + Client started." -ForegroundColor Green
