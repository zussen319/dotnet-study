# =============================================================================
#  Step 6: Acts as the SSO-protected main page (.aspx) and launches SmartClient
#
#  In production this is done by an .aspx page protected by Shibboleth SP:
#    (1) Get a one-time ticket using the SP-provided REMOTE_USER
#    (2) Launch SmartClient with that ticket embedded
#
#  This script reproduces (1) and (2) in PowerShell.
#  NOTE: REMOTE_USER is passed as an HTTP header here. This is a DEMO-ONLY
#        substitute. See README_step6.md for why headers must not be trusted
#        in production.
#
#  NOTE ON ENCODING: this file is intentionally ASCII-only.
#        Windows PowerShell 5.1 reads a .ps1 without BOM using the system ANSI
#        code page, so non-ASCII text can be misparsed on non-Japanese systems.
#
#  Usage:
#    powershell -ExecutionPolicy Bypass -File .\Invoke-SsoLaunch.ps1
#    powershell -ExecutionPolicy Bypass -File .\Invoke-SsoLaunch.ps1 -User 01PLM02@plm-lab.local
# =============================================================================

param(
    [string]$User      = "01PLM01@plm-lab.local",   # stands in for REMOTE_USER
    [string]$BaseUrl   = "http://localhost:5000",
    [string]$ClientDir = ".\JwtDemoClient"          # SmartClient project folder
)

$ErrorActionPreference = "Stop"

Write-Host "=== SSO-authenticated main page (.aspx equivalent) ===" -ForegroundColor Cyan
Write-Host "REMOTE_USER : $User"
Write-Host ""

# -----------------------------------------------------------------------------
# (1) Get a one-time ticket
#     In production the .aspx calls this server-side; the browser never sees it.
# -----------------------------------------------------------------------------
Write-Host "[1] Requesting ticket (GET /sso/ticket)"
try {
    $resp = Invoke-RestMethod -Uri "$BaseUrl/sso/ticket" -Headers @{ "X-Remote-User" = $User }
}
catch {
    Write-Host "  -> FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "     Check that the server is running and the user exists in LDAP."
    exit 1
}

$ticket = $resp.ticket
Write-Host "  -> OK. user = $($resp.user)"
Write-Host "     ticket = $($ticket.Substring(0,16))... (valid $($resp.expires_in)s, single use)"
Write-Host ""

# -----------------------------------------------------------------------------
# (2) Launch SmartClient, passing the ticket as a startup parameter
#     In production this goes into the ClickOnce URL or a downloaded config.
# -----------------------------------------------------------------------------
Write-Host "[2] Launching SmartClient with the ticket (no password prompt)"
Write-Host "------------------------------------------------"
Write-Host ""

if (-not (Test-Path $ClientDir)) {
    Write-Host "SmartClient project not found: $ClientDir" -ForegroundColor Red
    Write-Host "Run this script from the JWT06 folder, or pass -ClientDir <path>."
    exit 1
}

Push-Location $ClientDir
try {
    dotnet run -- $ticket
}
finally {
    Pop-Location
}
