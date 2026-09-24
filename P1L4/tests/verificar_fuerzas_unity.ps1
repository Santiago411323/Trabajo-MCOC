param(
    [Parameter(Mandatory=$true)][string]$Python,
    [string]$EditorData = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data'
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('mcoc-force-checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$referencePath = Join-Path $scratch 'reference.json'
$previousMpl = $env:MPLCONFIGDIR
try {
    $env:MPLCONFIGDIR = Join-Path $scratch 'matplotlib'
    # OpenSees escribe warnings ("WARNING no response...") en stderr. Con
    # ErrorActionPreference='Stop', PowerShell 5.1 los convierte en error terminante
    # (aun con 2>$null), asi que se relaja la variable alrededor de la llamada.
    $previousEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $Python (Join-Path $PSScriptRoot 'referencia_opensees.py') $referencePath 2>$null
    $refExit = $LASTEXITCODE
    $ErrorActionPreference = $previousEap
    if ($refExit -ne 0) { throw 'OpenSees reference failed' }
} finally {
    $env:MPLCONFIGDIR = $previousMpl
}
$runtime = Join-Path $EditorData 'NetCoreRuntime/dotnet.exe'
$compiler = Get-ChildItem (Join-Path $EditorData 'DotNetSdk/sdk') -Filter csc.dll -Recurse | Select-Object -First 1
$refFolder = Get-ChildItem (Join-Path $EditorData 'DotNetSdk/packs/Microsoft.NETCore.App.Ref') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$refs = Get-ChildItem (Join-Path $refFolder.FullName 'ref/net8.0') -Filter '*.dll'
$coreModule = Join-Path $EditorData 'Managed/UnityEngine/UnityEngine.CoreModule.dll'
$output = Join-Path $scratch 'UnityForceChecks.dll'
$arguments = @('-nologo','-target:exe','-langversion:9.0','-nostdlib+',('-out:"' + $output + '"'))
foreach ($reference in $refs) { $arguments += '-r:"' + $reference.FullName + '"' }
$arguments += '-r:"' + $coreModule + '"'
foreach ($file in @('FrameForces.cs','StructureData.cs','UnityData.cs')) {
    $arguments += '"' + (Join-Path $projectRoot ('P1L4/unity_visualizador/Assets/Scripts/' + $file)) + '"'
}
$arguments += '"' + (Join-Path $PSScriptRoot 'UnityForceChecks.cs') + '"'
$responseFile = Join-Path $scratch 'checks.rsp'
Set-Content -LiteralPath $responseFile -Value $arguments -Encoding utf8
& $runtime $compiler.FullName ('@' + $responseFile)
if ($LASTEXITCODE -ne 0) { throw 'C# checks compilation failed' }
Copy-Item -LiteralPath $coreModule -Destination $scratch
$runtimeConfig = @{ runtimeOptions = @{ tfm='net8.0'; framework=@{ name='Microsoft.NETCore.App'; version='8.0.21' } } }
$runtimeConfig | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $scratch 'UnityForceChecks.runtimeconfig.json') -Encoding utf8
& $runtime $output (Join-Path $projectRoot 'P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json') $referencePath
if ($LASTEXITCODE -ne 0) { throw 'Numerical checks failed' }
Write-Output "Evidence: $scratch"
