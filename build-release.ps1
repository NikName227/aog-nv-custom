<#
.SYNOPSIS
  Build a complete, versioned AgOpenGPS release (NV custom) in one command.

.DESCRIPTION
  Mimics the upstream GitVersion CI: the version is NOT hardcoded in the projects,
  it is forwarded to MSBuild as properties (like GitVersion's updateProjectFiles).
  Only FileVersion / InformationalVersion are set (drives Application.ProductVersion);
  AssemblyVersion is left at default on purpose so user settings are not reset.

  Builds every app (AgOpenGPS, AgIO, AgDiag, ModSim, GPS_Out, AgOpenGPS.Updater) in
  Release, assembles them into one flat folder (all exes run from StartupPath), and
  copies it into ShareFolder/Relese as release_<Version> (next to the Debug
  test build in ShareFolder/Test/AgOpenGPS_test).

.PARAMETER Version
  Release version, e.g. 6.8.5.2 (default).

.PARAMETER DestRoot
  Parent folder for the release (a "release_<Version>" subfolder is created).

.PARAMETER Sdk
  .NET SDK version to pin while building (must be installed).

.EXAMPLE
  .\build-release.ps1                 # builds 6.8.5.2
  .\build-release.ps1 -Version 6.8.5.3
#>
param(
    [string]$Version  = "6.8.5.3",
    [string]$DestRoot = "S:\Relese",
    [string]$Sdk      = "9.0.201"
)

$ErrorActionPreference = "Stop"

# Fall back to the WSL UNC path when the S: mapping is not reachable
# (e.g. when invoked from WSL interop where mapped drives are unavailable).
if (-not (Test-Path (Split-Path $DestRoot -Qualifier))) {
    $DestRoot = "\\wsl.localhost\Ubuntu\mnt\shared\Relese"
}
$repo = Split-Path -Parent $MyInvocation.MyCommand.Definition   # ...\AgOpenGPS
$src  = Join-Path $repo "SourceCode"
$dest = Join-Path $DestRoot "release_$Version"

# Version forwarded to every project (the "fake GitVersion"). FileVersion +
# InformationalVersion only -> ProductVersion shows the full version; AssemblyVersion
# stays default so Properties.Settings are not relocated/reset.
$vprops = @(
    "-p:FileVersion=$Version",
    "-p:InformationalVersion=$Version",
    "-p:IncludeSourceRevisionInInformationalVersion=false"
)

$apps = @(
    @{ Name = "AgOpenGPS";         Proj = "GPS\AgOpenGPS.csproj";             Out = "GPS\bin\Release\win-x64" },
    @{ Name = "AgIO";              Proj = "AgIO\Source\AgIO.csproj";          Out = "AgIO\Source\bin\Release" },
    @{ Name = "AgDiag";            Proj = "AgDiag\AgDiag.csproj";             Out = "AgDiag\bin\Release" },
    @{ Name = "ModSim";            Proj = "ModSim\Source\ModSim.csproj";      Out = "ModSim\Source\bin\Release" },
    @{ Name = "GPS_Out";           Proj = "GPS_Out\Source\GPS_Out.csproj";    Out = "GPS_Out\Source\bin\Release" },
    @{ Name = "AgOpenGPS.Updater"; Proj = "Updater\AgOpenGPS.Updater.csproj"; Out = "Updater\bin\Release\win-x64" }
)

Write-Host "== AgOpenGPS release $Version ==" -ForegroundColor Cyan

# Read/write exact bytes so temporary edits restore cleanly (no BOM / EOL churn,
# which Set-Content -Encoding utf8 would introduce on Windows PowerShell 5.1).
function Read-Text  ([string]$p) { [System.IO.File]::ReadAllText($p) }
function Write-Text ([string]$p, [string]$t) { [System.IO.File]::WriteAllText($p, $t) }

# --- pin SDK (remember original) ---
$gj    = Join-Path $repo "global.json"
$gjBak = Read-Text $gj
Write-Text $gj ($gjBak -replace '"version":\s*"[^"]+"', "`"version`": `"$Sdk`"")

# --- Updater uses GenerateAssemblyInfo=false, so -p:FileVersion is ignored.
#     Inject the version attributes into its AssemblyInfo.cs for the build, restore after. ---
$updAsm = Join-Path $src "Updater\Properties\AssemblyInfo.cs"
$updBak = Read-Text $updAsm
$attrs  = "[assembly: AssemblyFileVersion(`"$Version`")]`r`n" +
          "[assembly: AssemblyInformationalVersion(`"$Version`")]`r`n"
Write-Text $updAsm ($updBak -replace '(\[assembly: ComVisible\(false\)\])', "$attrs`$1")

try {
    # --- build every app with the version forwarded ---
    foreach ($a in $apps) {
        Write-Host "-- building $($a.Name)" -ForegroundColor Yellow
        & dotnet build (Join-Path $src $a.Proj) -c Release -v minimal --nologo @vprops
        if ($LASTEXITCODE -ne 0) { throw "build failed: $($a.Name)" }
    }

    # --- assemble: GPS first (main exe + shared dlls = win-x64 versions) ---
    if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest | Out-Null }
    Copy-Item (Join-Path $src "GPS\bin\Release\win-x64\*") $dest -Recurse -Force

    # --- then each companion: add missing shared deps, always refresh its own files ---
    foreach ($a in ($apps | Where-Object { $_.Name -ne "AgOpenGPS" })) {
        $o = Join-Path $src $a.Out
        Get-ChildItem $o -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($o.Length).TrimStart('\')
            $t   = Join-Path $dest $rel
            if ((-not (Test-Path $t)) -or ($_.Name -like "$($a.Name)*")) {
                $td = Split-Path $t -Parent
                if (-not (Test-Path $td)) { New-Item -ItemType Directory -Path $td -Force | Out-Null }
                Copy-Item $_.FullName $t -Force
            }
        }
    }
}
finally {
    # --- always restore the source files we touched (byte-exact) ---
    Write-Text $gj     $gjBak
    Write-Text $updAsm $updBak
}

# --- report ---
Write-Host "`n== release at $dest ==" -ForegroundColor Cyan
Get-ChildItem $dest -Recurse -Filter *.exe |
    ForEach-Object { "{0,-26} {1}" -f $_.Name, $_.VersionInfo.ProductVersion }
Write-Host ("files: {0}" -f (Get-ChildItem $dest -Recurse -File).Count)
