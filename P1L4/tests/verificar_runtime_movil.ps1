param([string]$EditorData='C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data')
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$taskOutput=Join-Path $taskRoot 'P1L4/seismic/validation/runtime-mobile'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskDotnet=Join-Path $EditorData 'NetCoreRuntime/dotnet.exe'
$taskCompiler=Get-ChildItem (Join-Path $EditorData 'DotNetSdk/sdk') -Filter csc.dll -Recurse | Select-Object -First 1
$taskRefFolder=Get-ChildItem (Join-Path $EditorData 'DotNetSdk/packs/Microsoft.NETCore.App.Ref') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$taskFramework=Get-ChildItem (Join-Path $taskRefFolder.FullName 'ref/net8.0') -Filter '*.dll'
$taskEngine=Get-ChildItem (Join-Path $EditorData 'Managed/UnityEngine') -Filter '*.dll'
foreach($taskPlatform in @('Android','iOS')) {
    $taskProject=Join-Path $taskRoot ('P1L4/'+ $(if($taskPlatform -eq 'iOS'){'unity_visualizador_ios'}else{'unity_visualizador'}))
    $taskAssemblies=Get-ChildItem (Join-Path $taskProject 'Library/ScriptAssemblies') -Filter '*.dll' |
        Where-Object {$_.Name -notmatch 'Assembly-CSharp|Editor|\.Tests|\.Test'}
    $taskSources=Get-ChildItem (Join-Path $taskProject 'Assets/Scripts') -Filter '*.cs'
    $taskArguments=@('-nologo','-target:library','-langversion:9.0','-nostdlib+',
        ('-define:UNITY_'+$taskPlatform.ToUpperInvariant()),('-out:"'+(Join-Path $taskOutput ($taskPlatform+'.dll'))+'"'))
    foreach($taskReference in @($taskFramework)+@($taskEngine)+@($taskAssemblies)) { $taskArguments+='-r:"'+$taskReference.FullName+'"' }
    foreach($taskSource in $taskSources){$taskArguments+='"'+$taskSource.FullName+'"'}
    $taskResponse=Join-Path $taskOutput ($taskPlatform+'.rsp')
    Set-Content -LiteralPath $taskResponse -Value $taskArguments -Encoding utf8
    & $taskDotnet $taskCompiler.FullName ('@'+$taskResponse)
    if($LASTEXITCODE -ne 0){throw "Compilacion de runtime $taskPlatform rechazada"}
    Write-Output "PASS: runtime C# $taskPlatform compilado con su simbolo nativo. No equivale a APK/Xcode ni a prueba fisica."
}
