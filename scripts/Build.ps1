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

$releaseRoot = Join-Path $projectRoot "artifacts/release/$version"
# A fresh staging folder prevents files from an earlier publish entering the ZIP.
$stagingRoot = Join-Path $projectRoot ('artifacts/staging/' + [Guid]::NewGuid().ToString('N'))
$buildRoot = Join-Path $stagingRoot 'build'
$verifyRoot = Join-Path $stagingRoot 'standalone'
$packageRoot = Join-Path $stagingRoot 'package'
New-Item -ItemType Directory -Path $releaseRoot, $packageRoot, $verifyRoot -Force | Out-Null

& dotnet build $projectPath -c Release -p:DebugType=None -p:DebugSymbols=false -o $buildRoot
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

# Test the exact portable artifact in a folder containing only the EXE. This
# catches accidental reliance on a sidecar DLL, runtimeconfig, or .exe.config.
Copy-Item -LiteralPath (Join-Path $buildRoot 'A moment for you.exe') -Destination $verifyRoot
$executable = Join-Path $verifyRoot 'A moment for you.exe'
if ((Get-ChildItem -LiteralPath $verifyRoot -File).Count -ne 1) { throw 'The standalone test directory must contain only the EXE.' }
if ((Get-Item -LiteralPath $executable).Length -ge 1MB) { throw 'The executable exceeds the 1 MiB release size limit.' }
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
Write-Output ("Standalone EXE size: {0:N1} KiB" -f ((Get-Item -LiteralPath $executable).Length / 1KB))
if ($env:GITHUB_OUTPUT) {
    "release-directory=$releaseRoot" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
}
Write-Output "Release $version is ready in $releaseRoot"
