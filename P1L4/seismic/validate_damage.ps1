$ErrorActionPreference = 'Stop'
$seismicRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$seismicChecks = Join-Path $PSScriptRoot 'validation/DamageChecks'
New-Item -ItemType Directory -Force -Path $seismicChecks | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot 'validation/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_NOLOGO = '1'
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><WarningLevel>0</WarningLevel></PropertyGroup>
  <ItemGroup>
    <Compile Include="../../../unity_visualizador/Assets/Scripts/FrameForces.cs" />
    <Compile Include="../../../unity_visualizador/Assets/Scripts/SeismicDamageEvaluator.cs" />
    <Compile Include="../../../tests/SeismicDamageChecks.cs" />
  </ItemGroup>
</Project>
'@ | Set-Content -Encoding utf8 -LiteralPath (Join-Path $seismicChecks 'DamageChecks.csproj')
'<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -Encoding utf8 -LiteralPath (Join-Path $seismicChecks 'NuGet.Config')
dotnet restore (Join-Path $seismicChecks 'DamageChecks.csproj') --configfile (Join-Path $seismicChecks 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo preparar el proyecto de chequeos.' }
dotnet run --no-restore --project (Join-Path $seismicChecks 'DamageChecks.csproj') -- $seismicRoot
if ($LASTEXITCODE -ne 0) { throw 'Fallaron los chequeos de fisuración/capacidad.' }
