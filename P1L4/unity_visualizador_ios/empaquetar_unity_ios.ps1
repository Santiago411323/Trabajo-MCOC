param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$iosProjectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path $iosProjectRoot -Parent) 'entregables_ios'
}
$iosOutputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if ($iosOutputRoot -eq $iosProjectRoot -or $iosOutputRoot.StartsWith($iosProjectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'La carpeta de ZIP debe quedar fuera del proyecto Unity.'
}
$iosPackageFiles = @()
foreach ($iosFolderName in @('Assets', 'Packages', 'ProjectSettings')) {
    $iosFolderPath = Join-Path $iosProjectRoot $iosFolderName
    if (-not (Test-Path -LiteralPath $iosFolderPath -PathType Container)) { throw "Falta $iosFolderName" }
    $iosPackageFiles += @(Get-ChildItem -LiteralPath $iosFolderPath -File -Recurse)
}
$iosPackageFiles += @(Get-ChildItem -LiteralPath $iosProjectRoot -File | Where-Object { $_.Extension -in @('.md', '.ps1') })
New-Item -ItemType Directory -Path $iosOutputRoot -Force | Out-Null
$iosStamp = [DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss_fff') + '_' + [guid]::NewGuid().ToString('N').Substring(0, 6)
$iosZipPath = Join-Path $iosOutputRoot "MCOC_iOS_Unity_$iosStamp.zip"
$iosPartialPath = $iosZipPath + '.partial'
try {
    $iosArchive = [IO.Compression.ZipFile]::Open($iosPartialPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($iosFile in $iosPackageFiles) {
            $iosRelative = $iosFile.FullName.Substring($iosProjectRoot.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($iosArchive, $iosFile.FullName,
                'unity_visualizador_ios/' + $iosRelative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $iosArchive.Dispose() }
    $iosCheck = [IO.Compression.ZipFile]::OpenRead($iosPartialPath)
    try {
        foreach ($iosRequired in @('Assets/Scenes/StructuralARScene.unity', 'Packages/manifest.json', 'ProjectSettings/ProjectVersion.txt', 'README_IOS.md')) {
            if ($null -eq $iosCheck.GetEntry('unity_visualizador_ios/' + $iosRequired)) { throw "ZIP incompleto: $iosRequired" }
        }
    } finally { $iosCheck.Dispose() }
    Move-Item -LiteralPath $iosPartialPath -Destination $iosZipPath
    Write-Output "ZIP de Unity creado: $iosZipPath"
} catch {
    if (Test-Path -LiteralPath $iosPartialPath) { Remove-Item -LiteralPath $iosPartialPath }
    throw
}
