# =============================================================================
#  Phase 5: Check the solution for Visual Studio 2019 / C# 7.3 compatibility
#
#  Scans the solution folder and reports anything that would prevent
#  Visual Studio 2019 + .NET Framework 4.8 from opening or building it.
#
#  Checks performed:
#    1. Solution file format (.slnx cannot be opened by VS2019)
#    2. Target framework of each project
#    3. C# 8.0+ syntax that would fail on C# 7.3
#    4. SDK-style project files (not supported for .NET Framework in VS2019)
#    5. NuGet packages that may require a newer framework
#
#  NOTE ON ENCODING: this file is intentionally ASCII-only.
#        Windows PowerShell 5.1 reads a .ps1 without BOM using the system ANSI
#        code page, so non-ASCII text can be misparsed on non-Japanese systems.
#
#  Usage:
#    powershell -ExecutionPolicy Bypass -File .\Check-Vs2019Compat.ps1
#    powershell -ExecutionPolicy Bypass -File .\Check-Vs2019Compat.ps1 -Path C:\path\to\solution
# =============================================================================

param(
    [string]$Path = "."
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path $Path).Path

Write-Host "=== VS2019 / C# 7.3 compatibility check ===" -ForegroundColor Cyan
Write-Host "Folder: $root"
Write-Host ""

$issues = 0
$warnings = 0

# -----------------------------------------------------------------------------
# 1. Solution file format
# -----------------------------------------------------------------------------
Write-Host "[1] Solution file format"
$slnx = Get-ChildItem -Path $root -Filter *.slnx -Recurse -ErrorAction SilentlyContinue
$sln  = Get-ChildItem -Path $root -Filter *.sln  -Recurse -ErrorAction SilentlyContinue

if ($slnx) {
    foreach ($f in $slnx) {
        Write-Host "  [NG] $($f.Name) - .slnx cannot be opened by VS2019" -ForegroundColor Red
        $issues++
    }
}
if ($sln) {
    foreach ($f in $sln) {
        $head = Get-Content $f.FullName -TotalCount 4 | Out-String
        if ($head -match "Format Version (\d+\.\d+)") {
            Write-Host "  [OK] $($f.Name) - classic format (version $($Matches[1]))" -ForegroundColor Green
        } else {
            Write-Host "  [??] $($f.Name) - could not determine format" -ForegroundColor Yellow
            $warnings++
        }
    }
}
if (-not $slnx -and -not $sln) {
    Write-Host "  [??] no solution file found" -ForegroundColor Yellow
    $warnings++
}
Write-Host ""

# -----------------------------------------------------------------------------
# 2 & 4. Project files: target framework and SDK style
# -----------------------------------------------------------------------------
Write-Host "[2] Project files"
$projects = Get-ChildItem -Path $root -Filter *.csproj -Recurse -ErrorAction SilentlyContinue

foreach ($p in $projects) {
    $content = Get-Content $p.FullName -Raw
    Write-Host "  - $($p.Name)"

    # SDK-style?
    if ($content -match '<Project\s+Sdk=') {
        Write-Host "      [NG] SDK-style project. VS2019 cannot build this for .NET Framework." -ForegroundColor Red
        $issues++
    } else {
        Write-Host "      [OK] classic project format" -ForegroundColor Green
    }

    # Target framework
    if ($content -match '<TargetFrameworkVersion>(.+?)</TargetFrameworkVersion>') {
        $tfv = $Matches[1]
        if ($tfv -eq "v4.8" -or $tfv -eq "v4.8.1") {
            Write-Host "      [OK] TargetFrameworkVersion = $tfv" -ForegroundColor Green
        } else {
            Write-Host "      [!!] TargetFrameworkVersion = $tfv (expected v4.8)" -ForegroundColor Yellow
            $warnings++
        }
    }
    elseif ($content -match '<TargetFramework>(.+?)</TargetFramework>') {
        Write-Host "      [NG] TargetFramework = $($Matches[1]) - .NET (Core) style, not VS2019 compatible" -ForegroundColor Red
        $issues++
    }
    else {
        Write-Host "      [??] target framework not found" -ForegroundColor Yellow
        $warnings++
    }

    # LangVersion
    if ($content -match '<LangVersion>(.+?)</LangVersion>') {
        $lv = $Matches[1]
        if ($lv -match '^(7\.3|7|default)$') {
            Write-Host "      [OK] LangVersion = $lv" -ForegroundColor Green
        } else {
            Write-Host "      [!!] LangVersion = $lv - may allow syntax VS2019 rejects" -ForegroundColor Yellow
            $warnings++
        }
    }
}
Write-Host ""

# -----------------------------------------------------------------------------
# 3. C# 8.0+ syntax
# -----------------------------------------------------------------------------
Write-Host "[3] C# 8.0+ syntax in source files"

$patterns = @(
    @{ Name = "file-scoped namespace";   Regex = '^\s*namespace\s+[\w\.]+\s*;\s*$' },
    @{ Name = "top-level statements";    Regex = '^\s*var\s+builder\s*=\s*WebApplication' },
    @{ Name = "record type";             Regex = '^\s*(public|internal)?\s*record\s+\w' },
    @{ Name = "switch expression";       Regex = '=>\s*$|\bswitch\s*\{' },
    @{ Name = "range/index operator";    Regex = '\[\s*\.\.|\[\^\d' },
    @{ Name = "nullable reference type"; Regex = '#nullable\s+(enable|disable)' },
    @{ Name = "using declaration";       Regex = '^\s*using\s+var\s+\w' },
    @{ Name = "target-typed new";        Regex = '=\s*new\s*\(\s*\)' }
)

$sources = Get-ChildItem -Path $root -Include *.cs -Recurse -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -notmatch '\\(obj|bin|\.vs|packages)\\' }

$found = $false
foreach ($src in $sources) {
    $lines = Get-Content $src.FullName -ErrorAction SilentlyContinue
    $n = 0
    foreach ($line in $lines) {
        $n++
        foreach ($pat in $patterns) {
            if ($line -match $pat.Regex) {
                $rel = $src.FullName.Substring($root.Length).TrimStart('\')
                Write-Host "  [!!] $rel line ${n}: possible $($pat.Name)" -ForegroundColor Yellow
                Write-Host "       $($line.Trim())"
                $warnings++
                $found = $true
            }
        }
    }
}
if (-not $found) {
    Write-Host "  [OK] no C# 8.0+ syntax detected" -ForegroundColor Green
}
Write-Host ""
Write-Host "  NOTE: some matches may be false positives (for example a lambda"
Write-Host "        ending with '=>'). Check each reported line by hand."
Write-Host ""

# -----------------------------------------------------------------------------
# 5. NuGet packages
# -----------------------------------------------------------------------------
Write-Host "[4] NuGet packages"
$configs = Get-ChildItem -Path $root -Filter packages.config -Recurse -ErrorAction SilentlyContinue

if ($configs) {
    foreach ($c in $configs) {
        Write-Host "  - $($c.Directory.Name)\packages.config"
        [xml]$xml = Get-Content $c.FullName
        foreach ($pkg in $xml.packages.package) {
            $tf = $pkg.targetFramework
            $mark = "[OK]"
            $color = "Green"
            if ($tf -and $tf -notmatch '^net4') {
                $mark = "[!!]"
                $color = "Yellow"
                $warnings++
            }
            Write-Host "      $mark $($pkg.id) $($pkg.version) (targetFramework: $tf)" -ForegroundColor $color
        }
    }
} else {
    Write-Host "  [??] no packages.config found (PackageReference style?)" -ForegroundColor Yellow
    $warnings++
}
Write-Host ""

# -----------------------------------------------------------------------------
# Summary
# -----------------------------------------------------------------------------
Write-Host "=== Summary ===" -ForegroundColor Cyan
if ($issues -eq 0 -and $warnings -eq 0) {
    Write-Host "  No problems found. The solution should open in VS2019." -ForegroundColor Green
} else {
    Write-Host "  Blocking issues : $issues" -ForegroundColor $(if ($issues -gt 0) { "Red" } else { "Green" })
    Write-Host "  Warnings        : $warnings" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Blocking issues must be fixed before VS2019 can open or build."
    Write-Host "  Warnings should be reviewed by hand."
}
