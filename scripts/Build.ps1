[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'MomentForYou.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'The project version must have the form 1.0.0.'
}
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version") {
    throw "Tag $env:GITHUB_REF_NAME does not match project version v$version."
}

$releaseRoot = Join-Path $projectRoot 'artifacts/release'
# A fresh staging folder prevents files from an earlier publish entering the ZIP.
$stagingRoot = Join-Path $projectRoot ('artifacts/staging/' + [Guid]::NewGuid().ToString('N'))
$publishRoot = Join-Path $stagingRoot 'publish'
$packageRoot = Join-Path $stagingRoot 'package'
New-Item -ItemType Directory -Path $releaseRoot, $packageRoot -Force | Out-Null

& dotnet publish $projectPath -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$executable = Join-Path $publishRoot 'A moment for you.exe'
$testProcess = Start-Process -FilePath $executable -ArgumentList '--self-test' `
    -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
if (-not $testProcess.WaitForExit(120000)) {
    Stop-Process -Id $testProcess.Id -ErrorAction SilentlyContinue
    throw 'The packaged app self-test timed out after two minutes.'
}
$testProcess.Refresh()
Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts/self-test-results.txt')
if ($testProcess.ExitCode -ne 0) { throw "Packaged app self-tests failed with exit code $($testProcess.ExitCode)." }

# GitHub converts spaces in uploaded asset names to dots. Use that public name
# explicitly so the published checksum file matches the downloaded executable.
$downloadExecutable = Join-Path $releaseRoot 'A.moment.for.you.exe'
Copy-Item -LiteralPath $executable -Destination $downloadExecutable -Force
Copy-Item -LiteralPath $executable -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageRoot
$packageImages = Join-Path $packageRoot 'docs/images'
New-Item -ItemType Directory -Path $packageImages -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/images/break-screen.png'), `
    (Join-Path $projectRoot 'docs/images/settings.png') -Destination $packageImages
$notes = Join-Path $projectRoot "docs/releases/v$version.md"
if (-not (Test-Path -LiteralPath $notes)) { throw "Release notes are missing: $notes" }
Copy-Item -LiteralPath $notes -Destination (Join-Path $packageRoot 'Release notes.md')

$zipPath = Join-Path $releaseRoot "A-moment-for-you-$version-win-x64.zip"
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath -Force
$checksumLines = foreach ($file in @($downloadExecutable, $zipPath)) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    '{0}  {1}' -f $hash, [System.IO.Path]::GetFileName($file)
}
$checksumLines | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding utf8NoBOM
Write-Output "Release $version is ready in $releaseRoot"
