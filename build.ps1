param(
    [string]$GameDirectory = '',
    [string]$AnchorChainDirectory = '',
    [switch]$Test
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$taskDotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { throw 'A .NET SDK is required.' }
$taskLocal = Join-Path $PSScriptRoot 'Directory.Build.local.props'
if (Test-Path -LiteralPath $taskLocal) {
    [xml]$taskPaths = Get-Content -LiteralPath $taskLocal
    if (-not $GameDirectory) { $GameDirectory = [string]$taskPaths.Project.PropertyGroup.SeaPowerDir }
    if (-not $AnchorChainDirectory) { $AnchorChainDirectory = [string]$taskPaths.Project.PropertyGroup.AnchorChainDir }
}
if (-not $GameDirectory -or -not (Test-Path -LiteralPath (Join-Path $GameDirectory 'Sea Power.exe'))) {
    throw 'Set SeaPowerDir in Directory.Build.local.props or pass -GameDirectory.'
}
if (-not $AnchorChainDirectory -or -not (Test-Path -LiteralPath (Join-Path $AnchorChainDirectory 'AnchorChain.dll'))) {
    throw 'Set AnchorChainDir in Directory.Build.local.props or pass -AnchorChainDirectory.'
}
$taskGame = (Resolve-Path -LiteralPath $GameDirectory).ProviderPath
$taskAnchorChain = (Resolve-Path -LiteralPath $AnchorChainDirectory).ProviderPath
$taskHome = Join-Path $PSScriptRoot 'artifacts\dotnet-home'
New-Item -ItemType Directory -Force -Path $taskHome | Out-Null

function Invoke-TaskDotnet([string[]]$TaskArguments) {
    $taskStart = New-Object System.Diagnostics.ProcessStartInfo
    $taskStart.FileName = $taskDotnet
    $taskStart.Arguments = ($TaskArguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $taskStart.WorkingDirectory = $PSScriptRoot
    $taskStart.UseShellExecute = $false
    $taskStart.CreateNoWindow = $true
    $taskStart.RedirectStandardOutput = $true
    $taskStart.RedirectStandardError = $true
    $taskStart.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $taskStart.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
    $taskStart.EnvironmentVariables['DOTNET_CLI_HOME'] = $taskHome
    $taskStart.EnvironmentVariables['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $taskStart.EnvironmentVariables['DOTNET_CLI_UI_LANGUAGE'] = 'en'
    $taskStart.EnvironmentVariables['DOTNET_GENERATE_ASPNET_CERTIFICATE'] = 'false'
    $taskStart.EnvironmentVariables['DOTNET_SKIP_FIRST_TIME_EXPERIENCE'] = '1'
    $taskStart.EnvironmentVariables['DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE'] = 'true'
    $taskProcess = [System.Diagnostics.Process]::Start($taskStart)
    $taskOutput = $taskProcess.StandardOutput.ReadToEndAsync()
    $taskErrors = $taskProcess.StandardError.ReadToEndAsync()
    $taskProcess.WaitForExit()
    if ($taskOutput.Result) { Write-Output $taskOutput.Result.TrimEnd() }
    if ($taskErrors.Result) { Write-Output $taskErrors.Result.TrimEnd() }
    if ($taskProcess.ExitCode -ne 0) { throw "dotnet exited with $($taskProcess.ExitCode)." }
}

$taskProperties = @('-p:UseSharedCompilation=false', "-p:SeaPowerDir=$taskGame", "-p:AnchorChainDir=$taskAnchorChain")
$taskProject = Join-Path $PSScriptRoot 'src\SunLensFlareRemover.csproj'
Invoke-TaskDotnet (@('build', $taskProject, '-c', 'Release', '--nologo', '--verbosity', 'minimal') + $taskProperties)
$taskBinary = Join-Path $PSScriptRoot 'src\bin\Release\netstandard2.1\SunLensFlareRemover.dll'
if ($Test) {
    Invoke-TaskDotnet (@('build', (Join-Path $PSScriptRoot 'tests\SunLensFlareRemover.Tests.csproj'), '-c', 'Release', '--nologo', '--verbosity', 'minimal') + $taskProperties)
    Invoke-TaskDotnet @((Join-Path $PSScriptRoot 'tests\bin\Release\net10.0\SunLensFlareRemover.Tests.dll'),
        (Join-Path $taskGame 'Sea Power_Data\Managed\Seapower-Scripts.dll'), $taskBinary)
}
$taskPackage = Join-Path $PSScriptRoot 'dist\SunLensFlareRemover'
New-Item -ItemType Directory -Force -Path $taskPackage | Out-Null
Copy-Item -LiteralPath $taskBinary -Destination $taskPackage -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package\_info.ini') -Destination $taskPackage -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $taskPackage -Force
Write-Output "Package: $taskPackage"
