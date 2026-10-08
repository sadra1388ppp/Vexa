@echo off
setlocal

set "ROOT=%~dp0.."

echo Starting Vexa Staging Server...
start "Vexa Staging Server" powershell.exe -NoProfile -ExecutionPolicy Bypass -NoExit -Command "$env:ASPNETCORE_ENVIRONMENT='Staging'; Set-Location '%ROOT%'; dotnet run --project .\Vexa.Server --launch-profile staging"

timeout /t 2 /nobreak >nul

echo Starting Vexa Staging Client...
start "Vexa Staging Client" powershell.exe -NoProfile -ExecutionPolicy Bypass -NoExit -Command "$env:VEXA_ENVIRONMENT='Staging'; Set-Location '%ROOT%'; dotnet run --project .\Vexa.Client --launch-profile staging"

echo.
echo Vexa Staging Server and Client have been started.
endlocal
