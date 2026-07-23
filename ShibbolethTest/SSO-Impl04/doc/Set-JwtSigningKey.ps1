# =============================================================================
#  Phase 4: Store the JWT signing key in Windows Credential Manager
#
#  IMPORTANT: Credentials are stored PER USER.
#             This script must run AS THE APPLICATION POOL IDENTITY,
#             otherwise the web application cannot read the value.
#
#             Typical usage:
#               1) runas /user:%COMPUTERNAME%\plmapp cmd.exe
#               2) In the new window:
#                    powershell -ExecutionPolicy Bypass -File .\Set-JwtSigningKey.ps1
#
#  NOTE ON ENCODING: this file is intentionally ASCII-only.
#        Windows PowerShell 5.1 reads a .ps1 without BOM using the system ANSI
#        code page, so non-ASCII text can be misparsed on non-Japanese systems.
#
#  Usage:
#    .\Set-JwtSigningKey.ps1                       (generates a random key)
#    .\Set-JwtSigningKey.ps1 -Key "your-own-key"   (uses the given key)
#    .\Set-JwtSigningKey.ps1 -Show                 (shows current registration)
# =============================================================================

param(
    [string]$Target = "PlmSsoDemo/JwtSigningKey",
    [string]$Key    = "",
    [switch]$Show
)

$ErrorActionPreference = "Stop"

Write-Host "=== JWT signing key -> Windows Credential Manager ===" -ForegroundColor Cyan
Write-Host "Running as : $([Security.Principal.WindowsIdentity]::GetCurrent().Name)"
Write-Host "Target     : $Target"
Write-Host ""

# -----------------------------------------------------------------------------
# Show mode: just list the credential
# -----------------------------------------------------------------------------
if ($Show) {
    Write-Host "[Show] Current registration for this user:"
    cmdkey /list:$Target
    Write-Host ""
    Write-Host "If nothing is listed, the credential is NOT registered for this user."
    exit 0
}

# -----------------------------------------------------------------------------
# Generate a key if not supplied (32 bytes -> Base64, well over the HS256 minimum)
# -----------------------------------------------------------------------------
if ([string]::IsNullOrEmpty($Key)) {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $Key = [Convert]::ToBase64String($bytes)
    Write-Host "[1] Generated a random key (32 bytes -> Base64)."
} else {
    $byteCount = [System.Text.Encoding]::UTF8.GetByteCount($Key)
    if ($byteCount -lt 32) {
        Write-Host "  -> ERROR: the key is $byteCount bytes. HS256 requires 32 bytes or more." -ForegroundColor Red
        exit 1
    }
    Write-Host "[1] Using the supplied key ($byteCount bytes)."
}

# -----------------------------------------------------------------------------
# Store as a generic credential for the CURRENT user
# -----------------------------------------------------------------------------
Write-Host "[2] Storing the credential..."
cmdkey /generic:$Target /user:PlmSsoDemo /pass:$Key | Out-Null

if ($LASTEXITCODE -ne 0) {
    Write-Host "  -> FAILED to store the credential." -ForegroundColor Red
    exit 1
}
Write-Host "  -> Stored."
Write-Host ""

# -----------------------------------------------------------------------------
# Verify
# -----------------------------------------------------------------------------
Write-Host "[3] Verifying..."
cmdkey /list:$Target
Write-Host ""

Write-Host "Done." -ForegroundColor Green
Write-Host ""
Write-Host "NEXT STEPS:"
Write-Host "  1. Make sure the IIS application pool runs as THIS user:"
Write-Host "     $([Security.Principal.WindowsIdentity]::GetCurrent().Name)"
Write-Host "  2. Set the application pool 'Load User Profile' to True."
Write-Host "  3. In Web.config, set:"
Write-Host "       <add key=`"JwtSigningKeyTarget`" value=`"$Target`" />"
Write-Host "     and REMOVE the plain-text JwtSigningKey entry."
Write-Host "  4. Recycle the application pool and open /PLM/api/diag to confirm."
