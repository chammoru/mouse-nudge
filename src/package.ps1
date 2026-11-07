param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$portable = Join-Path $destination 'MouseNudge'
if (Test-Path -LiteralPath $portable) { throw 'The package folder already exists. Choose a new output directory.' }
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $portable
$app = Join-Path $portable 'MouseNudge.exe'
$version = [Reflection.AssemblyName]::GetAssemblyName($app).Version.ToString(3)
$checks = Join-Path $destination 'checks'
New-Item -ItemType Directory -Path $checks -Force | Out-Null
$movementReport = Join-Path $checks 'movement.txt'
$test = Start-Process -FilePath $app -ArgumentList @('--self-test', ('"' + $movementReport + '"')) -WindowStyle Hidden -Wait -PassThru
if ($test.ExitCode -ne 0) { throw "Movement verification failed. See $movementReport" }
$layoutDirectory = Join-Path $checks 'layout'
$test = Start-Process -FilePath $app -ArgumentList @('--layout-test', ('"' + $layoutDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
if ($test.ExitCode -ne 0) { throw "Layout verification failed. See $layoutDirectory/result.txt" }

foreach ($name in @('README.md','BENCHMARK.md','CHANGELOG.md','LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $repository $name) -Destination $portable
}
New-Item -ItemType Directory -Path (Join-Path $portable 'src'), (Join-Path $portable 'assets') | Out-Null
Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Extension -in @('.cs','.ps1','.manifest') } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $portable 'src')
}
Get-ChildItem -LiteralPath (Join-Path $repository 'assets') -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $portable 'assets')
}
$validation = @("Mouse Nudge $version validation", '', 'DPI checks exercise the production layout without changing Windows settings. Physical mixed-monitor transitions require a separate manual check.', '', 'Movement and settings:', (Get-Content -LiteralPath $movementReport), '', 'DPI layout:', (Get-Content -LiteralPath (Join-Path $layoutDirectory 'result.txt')))
$validation | Set-Content -LiteralPath (Join-Path $portable 'VALIDATION.txt') -Encoding UTF8
$archive = Join-Path $destination "MouseNudge-v$version-windows.zip"
Compress-Archive -LiteralPath $portable -DestinationPath $archive
Copy-Item -LiteralPath $app -Destination (Join-Path $destination 'MouseNudge.exe')
$assets = @($archive, (Join-Path $destination 'MouseNudge.exe'))
$assets | ForEach-Object { '{0} *{1}' -f (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($_) } | Set-Content -LiteralPath (Join-Path $destination 'SHA256SUMS.txt') -Encoding ASCII
$changelog = Get-Content -LiteralPath (Join-Path $repository 'CHANGELOG.md') -Raw
$section = [regex]::Match($changelog, ('(?ms)^## ' + [regex]::Escape($version) + '[^\r\n]*\r?\n(.*?)(?=^## |\z)'))
if (-not $section.Success) { throw "No changelog entry for $version" }
$section.Groups[1].Value.Trim() | Set-Content -LiteralPath (Join-Path $destination 'release-notes.md') -Encoding UTF8
Write-Output "Packaged Mouse Nudge $version in $destination"
