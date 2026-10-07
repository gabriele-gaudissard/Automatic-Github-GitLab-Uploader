param([switch]$RunTests)

$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'The .NET Framework C# compiler is missing. Install .NET Framework 4.8.'
}
$sourcePath = Join-Path $PSScriptRoot 'src'
$commonArguments = @('/nologo', '/utf8output', '/optimize+', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', ('/win32manifest:' + (Join-Path $sourcePath 'app.manifest')))
$sources = @((Join-Path $sourcePath 'GitUploader.cs'), (Join-Path $sourcePath 'UnifiedWindow.cs'), (Join-Path $sourcePath 'WindowsFolderPicker.cs'))
$executablePath = Join-Path $PSScriptRoot 'Git Repository Uploader.exe'
& $compilerPath @commonArguments '/target:winexe' ('/out:' + $executablePath) @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Write-Output ('Built: ' + $executablePath)

if ($RunTests) {
    $testDirectory = Join-Path $PSScriptRoot 'work'
    New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
    $testExecutable = Join-Path $testDirectory 'SmokeTests.exe'
    & $compilerPath @commonArguments '/target:exe' '/main:SmokeTests' ('/out:' + $testExecutable) @sources (Join-Path $PSScriptRoot 'tests\SmokeTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    $testRunDirectory = Join-Path $testDirectory ('tests-' + [Guid]::NewGuid().ToString('N'))
    & $testExecutable $testRunDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
