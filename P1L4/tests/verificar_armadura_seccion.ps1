param([string]$EditorData='C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data')
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$taskOutput=Join-Path ([IO.Path]::GetTempPath()) ('mcoc-rebar-check-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskOutput | Out-Null
$taskRuntime=Join-Path $EditorData 'NetCoreRuntime/dotnet.exe'
$taskCompiler=Get-ChildItem (Join-Path $EditorData 'DotNetSdk/sdk') -Filter csc.dll -Recurse | Select-Object -First 1
$taskRefFolder=Get-ChildItem (Join-Path $EditorData 'DotNetSdk/packs/Microsoft.NETCore.App.Ref') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$taskReferences=Get-ChildItem (Join-Path $taskRefFolder.FullName 'ref/net8.0') -Filter '*.dll'
$taskCore=Join-Path $EditorData 'Managed/UnityEngine/UnityEngine.CoreModule.dll'
$taskBinary=Join-Path $taskOutput 'ReinforcementChecks.dll'
$taskArgs=@('-nologo','-target:exe','-langversion:9.0','-nostdlib+',('-out:"'+$taskBinary+'"'))
foreach($reference in $taskReferences){ $taskArgs+='-r:"'+$reference.FullName+'"' }
$taskArgs+='-r:"'+$taskCore+'"'
foreach($source in @('StructureData.cs','ReinforcementAssessment.cs','MemberMaterialPlayback.cs','MemberPreviewKinematics.cs','FrameForces.cs','UnityData.cs','DemandRadarRanking.cs','LrfdScenario.cs','LrfdDataset.cs','LrfdDesignCapacity.cs')){
    $taskArgs+='"'+(Join-Path $taskRoot ('P1L4/unity_visualizador/Assets/Scripts/'+$source))+'"'
}
$taskArgs+='"'+(Join-Path $PSScriptRoot 'ReinforcementChecks.cs')+'"'
$taskRsp=Join-Path $taskOutput 'checks.rsp'
Set-Content -LiteralPath $taskRsp -Value $taskArgs -Encoding utf8
& $taskRuntime $taskCompiler.FullName ('@'+$taskRsp)
if($LASTEXITCODE -ne 0){throw 'Falló compilación de verificaciones'}
Copy-Item -LiteralPath $taskCore -Destination $taskOutput
@{runtimeOptions=@{tfm='net8.0';framework=@{name='Microsoft.NETCore.App';version='8.0.21'}}} |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $taskOutput 'ReinforcementChecks.runtimeconfig.json') -Encoding utf8
& $taskRuntime $taskBinary (Join-Path $taskRoot 'P1L4/desktop_model/estructura_p1l4_desktop.json') (Join-Path $taskRoot 'P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json')
if($LASTEXITCODE -ne 0){throw 'Falló verificación numérica'}

# Compilar todo el runtime de escritorio, incluida la integración del panel.
$taskProject=Join-Path $taskRoot 'P1L4/unity_visualizador'
$taskEngine=Get-ChildItem (Join-Path $EditorData 'Managed/UnityEngine') -Filter '*.dll'
$taskPackages=Get-ChildItem (Join-Path $taskProject 'Library/ScriptAssemblies') -Filter '*.dll' |
    Where-Object {$_.Name -notmatch 'Assembly-CSharp|Editor|\.Tests|\.Test'}
$taskSources=Get-ChildItem (Join-Path $taskProject 'Assets/Scripts') -Filter '*.cs'
$taskArgs=@('-nologo','-target:library','-langversion:9.0','-nostdlib+',
    '-define:UNITY_STANDALONE_WIN','-nowarn:0618,0162,0649',('-out:"'+(Join-Path $taskOutput 'DesktopRuntime.dll')+'"'))
foreach($reference in @($taskReferences)+@($taskEngine)+@($taskPackages)){$taskArgs+='-r:"'+$reference.FullName+'"'}
foreach($source in $taskSources){$taskArgs+='"'+$source.FullName+'"'}
$taskRsp=Join-Path $taskOutput 'desktop.rsp'
Set-Content -LiteralPath $taskRsp -Value $taskArgs -Encoding utf8
& $taskRuntime $taskCompiler.FullName ('@'+$taskRsp)
if($LASTEXITCODE -ne 0){throw 'Falló compilación del runtime de escritorio'}
Write-Output 'PASS: runtime completo de escritorio, incluido panel de armadura, compilado.'
Write-Output "Evidencia: $taskOutput"
