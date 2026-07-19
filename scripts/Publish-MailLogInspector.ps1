[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishRoot = "C:\Apps\Mail Log Inspector",
    [string]$ExecutableName = "MailLogInspector.exe"
)

$ErrorActionPreference = "Stop"

function Copy-RepoFileIfExists {
    param(
        [string]$SourcePath,
        [string]$DestinationRoot
    )

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        return
    }

    $destinationPath = Join-Path $DestinationRoot ([System.IO.Path]::GetFileName($SourcePath))
    Copy-Item -LiteralPath $SourcePath -Destination $destinationPath -Force
}

function Copy-SourceTreeWithoutBuildArtifacts {
    param(
        [string]$SourceRoot,
        [string]$DestinationRoot
    )

    Get-ChildItem -LiteralPath $SourceRoot -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($SourceRoot.Length).TrimStart('\')
            $destinationPath = Join-Path $DestinationRoot $relativePath
            $destinationDir = Split-Path -Parent $destinationPath

            if (-not (Test-Path -LiteralPath $destinationDir)) {
                New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
            }

            Copy-Item -LiteralPath $_.FullName -Destination $destinationPath -Force
        }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repoRoot "src"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "MailLogInspector-publish-src"
$tempSourceRoot = Join-Path $tempRoot "src"
$projectPath = Join-Path $tempSourceRoot "MailLogInspector.App\MailLogInspector.App.csproj"
$stagingPath = Join-Path $repoRoot "artifacts\publish\$Runtime"

Write-Host "Publishing Mail Log Inspector..."
Write-Host "Source  : $sourceRoot"
Write-Host "Temp    : $tempRoot"
Write-Host "Project : $projectPath"
Write-Host "Staging : $stagingPath"
Write-Host "Target  : $PublishRoot"
Write-Host "Exe     : $ExecutableName"

if (Test-Path -LiteralPath $stagingPath) {
    Remove-Item -LiteralPath $stagingPath -Recurse -Force
}

if (Test-Path -LiteralPath $tempRoot) {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $tempSourceRoot -Force | Out-Null
Copy-RepoFileIfExists -SourcePath (Join-Path $repoRoot 'global.json') -DestinationRoot $tempRoot
Copy-RepoFileIfExists -SourcePath (Join-Path $repoRoot 'NuGet.config') -DestinationRoot $tempRoot
Copy-SourceTreeWithoutBuildArtifacts -SourceRoot $sourceRoot -DestinationRoot $tempSourceRoot

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $stagingPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed before staging output was created."
}

if (-not (Test-Path -LiteralPath $stagingPath)) {
    throw "Publish staging folder was not created: $stagingPath"
}

if (-not (Test-Path -LiteralPath $PublishRoot)) {
    New-Item -ItemType Directory -Path $PublishRoot | Out-Null
}

Get-ChildItem -LiteralPath $stagingPath -Force | ForEach-Object {
    $destinationName = if ($_.Name -ieq "MailLogInspector.exe") { $ExecutableName } else { $_.Name }
    $destinationPath = Join-Path $PublishRoot $destinationName

    if (Test-Path -LiteralPath $destinationPath) {
        Remove-Item -LiteralPath $destinationPath -Recurse -Force
    }

    Copy-Item -LiteralPath $_.FullName -Destination $destinationPath -Recurse -Force
}

foreach ($folderName in @("Incoming", "Incoming\SmtpReports", "Archive", "ArchiveDb")) {
    $folderPath = Join-Path $PublishRoot $folderName
    if (-not (Test-Path -LiteralPath $folderPath)) {
        New-Item -ItemType Directory -Path $folderPath | Out-Null
    }
}

try {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
catch {
    Write-Warning "Kon tijdelijke publish-bron niet volledig opruimen: $($_.Exception.Message)"
}

Write-Host ""
Write-Host "Publish complete."
Write-Host "Executable: $(Join-Path $PublishRoot $ExecutableName)"
