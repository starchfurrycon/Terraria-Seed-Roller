[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts'))
if (-not $artifacts.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved artifacts path escaped the repository.'
}

$portableDotnet = Join-Path $repository 'tools\dotnet8\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $portableDotnet) { $portableDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
if (Test-Path -LiteralPath $artifacts) {
    Remove-Item -LiteralPath $artifacts -Recurse -Force
}

$stage = Join-Path $artifacts "TerrariaSeedRoller-$Runtime"
$app = Join-Path $stage 'app'
$cli = Join-Path $stage 'cli'
New-Item -ItemType Directory -Path $app, $cli -Force | Out-Null
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
$singleFile = if ($FrameworkDependent) { 'false' } else { 'true' }

& $dotnet build (Join-Path $repository 'TerrariaSeedRoller.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $dotnet run --project (Join-Path $repository 'tests\TerrariaSeedRoller.Tests') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

$common = @('-c', 'Release', '-r', $Runtime, '--self-contained', $selfContained,
    "-p:PublishSingleFile=$singleFile", '-p:DebugType=None', '-p:DebugSymbols=false')
& $dotnet publish (Join-Path $repository 'src\TerrariaSeedRoller.App\TerrariaSeedRoller.App.csproj') @common -o $app
if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
& $dotnet publish (Join-Path $repository 'src\TerrariaSeedRoller.Cli\TerrariaSeedRoller.Cli.csproj') @common -o $cli
if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }

Copy-Item -LiteralPath (Join-Path $repository 'README.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repository 'CHANGELOG.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repository 'NOTICE.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repository 'docs') -Destination $stage -Recurse
$zip = Join-Path $artifacts "TerrariaSeedRoller-$Runtime.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Release package: $zip"
