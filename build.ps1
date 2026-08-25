param(
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path $projectRoot 'src\CopyRecord'
$outputRoot = Join-Path $projectRoot 'dist'
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpfRoot = Join-Path $frameworkRoot 'WPF'
$frameworkCsc = Join-Path $frameworkRoot 'csc.exe'
$dotnetExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

# Find an available Roslyn compiler (fallback when system csc.exe crashes/unavailable)
$roslynCsc = Get-ChildItem "$env:ProgramFiles\dotnet\sdk" -Directory -ErrorAction SilentlyContinue |
    ForEach-Object { Join-Path $_.FullName 'Roslyn\bincore\csc.dll' } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

New-Item -ItemType Directory -Force $outputRoot | Out-Null

$sources = Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | ForEach-Object { $_.FullName }

# Extract embedded icon (Base64 inside AppIcon.cs) and materialize a temp .ico for /win32icon
$tempIcon = Join-Path $env:TEMP 'copyrecord_app.ico'
$iconBase64 = $null
$appIconSource = Join-Path $sourceRoot 'AppIcon.cs'
if (Test-Path -LiteralPath $appIconSource) {
    $iconText = Get-Content -LiteralPath $appIconSource -Raw
    if ($iconText -match 'Append\(@"([A-Za-z0-9+/=]+)"\);') { $iconBase64 = $Matches[1] }
}
if ($iconBase64) {
    [System.IO.File]::WriteAllBytes($tempIcon, [Convert]::FromBase64String($iconBase64))
}

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')),
    ('/out:' + (Join-Path $outputRoot 'CopyRecord.exe')),
    ('/reference:' + (Join-Path $frameworkRoot 'mscorlib.dll')),
    ('/reference:' + (Join-Path $wpfRoot 'PresentationCore.dll')),
    ('/reference:' + (Join-Path $wpfRoot 'PresentationFramework.dll')),
    ('/reference:' + (Join-Path $wpfRoot 'WindowsBase.dll')),
    ('/reference:' + (Join-Path $frameworkRoot 'System.Xaml.dll')),
    ('/reference:' + (Join-Path $frameworkRoot 'System.dll')),
    ('/reference:' + (Join-Path $frameworkRoot 'System.Core.dll')),
    ('/reference:' + (Join-Path $frameworkRoot 'System.Drawing.dll')),
    ('/reference:' + (Join-Path $frameworkRoot 'System.Windows.Forms.dll')),
    ('/reference:' + (Join-Path $frameworkRoot 'System.Runtime.Serialization.dll')),
    ('/reference:' + (Join-Path $frameworkRoot 'System.Xml.dll'))
) + $sources
if ($iconBase64) { $arguments += @('/win32icon:' + $tempIcon) }

# Use a response file to avoid command-line length limits crashing csc.exe
$respFile = Join-Path $env:TEMP 'copyrecord_csc_args.rsp'
[System.IO.File]::WriteAllLines($respFile, $arguments)
try {
    $exitCode = 1
    if (Test-Path -LiteralPath $frameworkCsc) {
        & $frameworkCsc ('@' + $respFile)
        $exitCode = $LASTEXITCODE
    }
    if ($exitCode -ne 0 -and $roslynCsc -and (Test-Path -LiteralPath $dotnetExe)) {
        Write-Host 'System csc.exe unavailable; falling back to Roslyn compiler.'
        & $dotnetExe $roslynCsc ('@' + $respFile)
        $exitCode = $LASTEXITCODE
    }
    if ($exitCode -ne 0) { throw 'CopyRecord build failed.' }
}
finally {
    Remove-Item -LiteralPath $respFile -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $tempIcon -ErrorAction SilentlyContinue
}

Write-Output (Join-Path $outputRoot 'CopyRecord.exe')
if ($Run) {
    Start-Process -FilePath (Join-Path $outputRoot 'CopyRecord.exe')
}
