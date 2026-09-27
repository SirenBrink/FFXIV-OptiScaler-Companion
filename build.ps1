param(
    [Parameter(Mandatory=$true)][string]$DalamudLibPath,
    [string]$Dotnet = 'dotnet',
    [string]$BridgeFixture = ''
)
$ErrorActionPreference = 'Stop'
$DalamudLibPath = (Resolve-Path -LiteralPath $DalamudLibPath).Path
$env:DOTNET_CLI_HOME = "$PSScriptRoot\.build\dotnet-home"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $Dotnet build "$PSScriptRoot\src\FFXIVOptiScalerCompanion.csproj" -c Release "-p:DalamudLibPath=$DalamudLibPath" --configfile "$PSScriptRoot\NuGet.Config"
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed' }
& $Dotnet restore "$PSScriptRoot\tests\Companion.Tests.csproj" --configfile "$PSScriptRoot\NuGet.Config"
if ($LASTEXITCODE -ne 0) { throw 'Test restore failed' }
$testArgs = @('run','--project',"$PSScriptRoot\tests\Companion.Tests.csproj",'-c','Release','--no-restore')
if ($BridgeFixture) { $testArgs += @('--', (Resolve-Path -LiteralPath $BridgeFixture).Path) }
& $Dotnet @testArgs
if ($LASTEXITCODE -ne 0) { throw 'Bridge tests failed' }
$release = "$PSScriptRoot\release\FFXIVOptiScalerCompanion"
New-Item -ItemType Directory -Path $release -Force | Out-Null
foreach ($name in @('FFXIVOptiScalerCompanion.dll','FFXIVOptiScalerCompanion.json','FFXIVOptiScalerCompanion.deps.json')) {
    Copy-Item -LiteralPath "$PSScriptRoot\src\bin\Release\net10.0-windows\$name" -Destination $release -Force
}
Copy-Item -LiteralPath "$PSScriptRoot\README.md" -Destination $release -Force
$version = (Get-Content -LiteralPath "$release\FFXIVOptiScalerCompanion.json" -Raw | ConvertFrom-Json).AssemblyVersion
Compress-Archive -Path "$release\*" -DestinationPath "$PSScriptRoot\release\FFXIVOptiScalerCompanion-$version.zip" -Force
Write-Output "Built: $release\FFXIVOptiScalerCompanion.dll"
