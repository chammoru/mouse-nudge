param([string]$OutputDirectory, [switch]$Preview, [ValidateSet('anycpu','x86','x64')][string]$Platform='anycpu')
$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
$destination = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $source 'build' } else { [IO.Path]::GetFullPath($OutputDirectory) }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
& $compiler /nologo /target:exe /reference:System.Drawing.dll /out:"$source\MakeIcon.exe" "$source\MakeIcon.cs"
if ($LASTEXITCODE -ne 0) { throw 'Icon compiler failed.' }
& "$source\MakeIcon.exe" $destination
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
$executableName = if ($Preview) { 'MouseNudgePreview.exe' } else { 'MouseNudge.exe' }
$compilerArgs = @('/nologo', '/target:winexe', "/platform:$Platform", '/optimize+', '/utf8output', '/reference:System.dll', '/reference:System.Xml.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', "/win32icon:$destination\MouseNudge.ico", "/win32manifest:$source\app.manifest", "/resource:$destination\MouseNudge.ico,MouseNudge.ico", "/resource:$destination\MouseNudge.png,MouseNudge.png", "/out:$destination\$executableName", "$source\MouseNudge.cs", "$source\Lifecycle.cs", "$source\Verification.cs", "$source\DpiLayout.cs", "$source\LayoutVerification.cs")
if ($Preview) { $compilerArgs += '/define:UI_PREVIEW' }
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Application compiler failed.' }
Write-Output "Built $destination\$executableName"
