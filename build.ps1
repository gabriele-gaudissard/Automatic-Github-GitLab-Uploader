param([switch]$RunTests, [string]$Version)

$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'The .NET Framework C# compiler is missing. Install .NET Framework 4.8.'
}
$sourcePath = Join-Path $PSScriptRoot 'src'
if ($Version) {
    $releaseVersion = [version]$Version
    if ($releaseVersion.Build -lt 0) { throw 'Use a version such as 1.0.1 or 1.0.1.0.' }
    if ($releaseVersion.Revision -lt 0) { $releaseVersion = [version]::new($releaseVersion.Major, $releaseVersion.Minor, $releaseVersion.Build, 0) }
    $assemblyInfo = 'using System.Reflection;' + [Environment]::NewLine +
        '[assembly: AssemblyTitle("Git Repository Uploader")]' + [Environment]::NewLine +
        '[assembly: AssemblyProduct("Git Repository Uploader")]' + [Environment]::NewLine +
        '[assembly: AssemblyVersion("' + $releaseVersion + '")]' + [Environment]::NewLine +
        '[assembly: AssemblyFileVersion("' + $releaseVersion + '")]' + [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $sourcePath 'AssemblyInfo.cs'), $assemblyInfo, [Text.UTF8Encoding]::new($false))
}
$commonArguments = @('/nologo', '/utf8output', '/optimize+', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll', ('/win32manifest:' + (Join-Path $sourcePath 'app.manifest')))
$sources = @((Join-Path $sourcePath 'GitUploader.cs'), (Join-Path $sourcePath 'UnifiedWindow.cs'), (Join-Path $sourcePath 'WindowsFolderPicker.cs'), (Join-Path $sourcePath 'AutoUpdater.cs'), (Join-Path $sourcePath 'AssemblyInfo.cs'))
$executablePath = Join-Path $PSScriptRoot 'Git Repository Uploader.exe'
& $compilerPath @commonArguments '/target:winexe' ('/out:' + $executablePath) @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Write-Output ('Built: ' + $executablePath)
$updateMetadata = [ordered]@{
    version = [Reflection.AssemblyName]::GetAssemblyName($executablePath).Version.ToString()
    sha256 = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash.ToLowerInvariant()
    sizeBytes = (Get-Item -LiteralPath $executablePath).Length
}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'update.json'), ($updateMetadata | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

if ($RunTests) {
    $testDirectory = Join-Path $PSScriptRoot 'work'
    New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
    $testExecutable = Join-Path $testDirectory 'SmokeTests.exe'
    & $compilerPath @commonArguments '/target:exe' '/main:SmokeTests' ('/out:' + $testExecutable) @sources (Join-Path $PSScriptRoot 'tests\SmokeTests.cs') (Join-Path $PSScriptRoot 'tests\UpdateTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    $testRunDirectory = Join-Path $testDirectory ('tests-' + [Guid]::NewGuid().ToString('N'))
    & $testExecutable $testRunDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
