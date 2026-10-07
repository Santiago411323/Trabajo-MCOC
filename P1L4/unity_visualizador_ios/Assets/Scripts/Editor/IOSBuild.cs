using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

// This file belongs only to the independent iOS project.
public static class IOSBuild
{
    private const string LoaderType = "UnityEngine.XR.ARKit.ARKitLoader";
    private const string SettingsPath = "Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset";
    public static string LastPackagedBuildPath { get; private set; }

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.update -= ConfigureWhenReady;
        EditorApplication.update += ConfigureWhenReady;
    }

    private static void ConfigureWhenReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= ConfigureWhenReady;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        try { ConfigureIOS(); }
        catch (Exception error) { Debug.LogWarning("[MCOC iOS] " + error.Message); }
    }

    [MenuItem("MCOC/iPhone/Configurar ARKit")]
    public static void ConfigureIOS()
    {
        PlayerSettings.iOS.cameraUsageDescription =
            "La camara permite colocar elementos estructurales y consultar sus resultados en realidad aumentada.";
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.iOS.buildNumber = "17";
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { GraphicsDeviceType.Metal });
        string identifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
        if (string.IsNullOrEmpty(identifier) || identifier.StartsWith("com.DefaultCompany."))
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.mcoc.structuralar.ios");
        EditorBuildSettings.scenes = new[] {
            new EditorBuildSettingsScene(StructuralARSceneSetup.ScenePath, true)
        };

        XRGeneralSettingsPerBuildTarget settings =
            AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(SettingsPath);
        if (settings == null) throw new InvalidOperationException("Faltan los settings XR de la copia iOS.");
        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
        if (!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.iOS))
            settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.iOS);
        XRGeneralSettings general = settings.SettingsForBuildTarget(BuildTargetGroup.iOS);
        if (general == null || general.Manager == null)
            throw new InvalidOperationException("No se pudieron crear los settings XR para iOS.");
        bool assigned = general.Manager.activeLoaders.Any(loader =>
            loader != null && loader.GetType().FullName == LoaderType);
        if (!assigned && !XRPackageMetadataStore.AssignLoader(general.Manager, LoaderType, BuildTargetGroup.iOS))
            throw new InvalidOperationException(
                "ARKit no pudo activarse. Resuelve ARKit 6.6.2 e instala iOS Build Support en Unity Hub.");
        if (!general.Manager.activeLoaders.Any(loader => loader is Google.XR.Cardboard.XRLoader) &&
            !XRPackageMetadataStore.AssignLoader(general.Manager, "Google.XR.Cardboard.XRLoader", BuildTargetGroup.iOS))
            throw new InvalidOperationException("No se pudo registrar el proveedor Cardboard iOS.");
        // ARKit remains first at startup. Cardboard is started explicitly after AR is stopped.
        general.Manager.TrySetLoaders(general.Manager.activeLoaders.OrderBy(loader => loader.GetType().FullName == LoaderType ? 0 : 1).ToList());
        general.InitManagerOnStart = true;
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(general);
        EditorUtility.SetDirty(general.Manager);
        AssetDatabase.SaveAssets();
    }

    public static void ValidateIOS()
    {
        XRGeneralSettings general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.iOS);
        if (general == null || general.Manager == null || !general.InitManagerOnStart ||
            !general.Manager.activeLoaders.Any(loader => loader != null && loader.GetType().FullName == LoaderType))
            throw new BuildFailedException("ARKit y su inicio automatico deben estar habilitados para iOS.");
        if (string.IsNullOrWhiteSpace(PlayerSettings.iOS.cameraUsageDescription))
            throw new BuildFailedException("Falta la descripcion de uso de la camara.");
        if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.iOS) != ScriptingImplementation.IL2CPP)
            throw new BuildFailedException("iOS requiere IL2CPP en esta copia.");
        if (!EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == StructuralARSceneSetup.ScenePath))
            throw new BuildFailedException("Falta StructuralARScene en las escenas de compilacion.");
    }

    [MenuItem("MCOC/iPhone/Exportar Xcode y ZIP")]
    public static void ExportFromMenu()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
        {
            EditorUtility.DisplayDialog("Falta iOS Build Support",
                "Instala iOS Build Support para esta version desde Unity Hub. No se ha generado un proyecto Xcode.", "Aceptar");
            return;
        }
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS))
                throw new BuildFailedException("No se pudo activar la plataforma iOS.");
            // Platform switching reloads editor assemblies. Let it finish before exporting.
            EditorUtility.DisplayDialog("Plataforma iOS seleccionada",
                "Espera a que termine la importacion y vuelve a elegir MCOC > iPhone > Exportar Xcode y ZIP.", "Aceptar");
            return;
        }
        Export();
    }

    public static void ExportBatch()
    {
        try { Export(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void RepackageBatch()
    {
        try
        {
            string source = Environment.GetEnvironmentVariable("MCOC_XCODE_PATH");
            if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException("Falta MCOC_XCODE_PATH.");
            PackageXcode(source);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void ValidateBatch()
    {
        try
        {
            ConfigureIOS();
            StructuralARValidation.ValidateOrThrow();
            StructuralARSectorValidation.Validate();
            ValidateArchive();
            Debug.Log("[MCOC iOS] Configuracion y datos: PASS. No equivale a una prueba fisica en iPhone.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void Export()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS) ||
            EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            throw new BuildFailedException("Instala iOS Build Support y selecciona iOS antes de exportar.");
        ConfigureIOS();
        ValidateIOS();
        StructuralARValidation.ValidateOrThrow();
        StructuralARSectorValidation.Validate();
        // ARKit's generated image catalog exceeds Windows' legacy path limit in a dated project subfolder.
        string folder = Path.GetFullPath(Path.Combine(ProjectRoot, "..", "..", "xc"));
        if (Directory.Exists(folder) && !Directory.Exists(Path.Combine(folder,"Unity-iPhone.xcodeproj")))
            throw new BuildFailedException("La carpeta xc ya existe y no es una exportacion Xcode; se conserva sin cambios.");
        LastPackagedBuildPath = null;
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { StructuralARSceneSetup.ScenePath },
            locationPathName = folder,
            target = BuildTarget.iOS,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException("La exportacion iOS fallo; no se entrega un ZIP como valido.");
        // Unity can log post-build exceptions while still reporting build success.
        if (string.IsNullOrEmpty(LastPackagedBuildPath) || !File.Exists(LastPackagedBuildPath))
            throw new BuildFailedException("Xcode se exporto, pero no se genero su ZIP. Revisa el error de empaquetado.");
        // The post-build callback also covers exports made through Unity's Build window.
    }

    public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string Stamp() => DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);

    public static string PackageXcode(string outputPath)
    {
        string source = Path.GetFullPath(outputPath);
        if (!Directory.Exists(Path.Combine(source, "Unity-iPhone.xcodeproj")))
            throw new BuildFailedException("No existe Unity-iPhone.xcodeproj; no se puede entregar el ZIP de Xcode.");
        string delivery = Path.Combine(ProjectRoot, "Entregables");
        Directory.CreateDirectory(delivery);
        string zipPath = Path.Combine(delivery, "MCOC_iOS_Xcode_" + Stamp() + ".zip");
        string temporaryZip = zipPath + ".partial";
        File.WriteAllText(Path.Combine(source, "LEEME_XCODE.txt"),
            "Este es el proyecto Xcode exportado por Unity, no una IPA firmada.\n" +
            "1. Descomprime TODO el ZIP en el Mac.\n" +
            "2. Abre Unity-iPhone.xcodeproj (o .xcworkspace si el proyecto lo incluye).\n" +
            "3. En Signing & Capabilities elige tu Team y un Bundle Identifier unico.\n" +
            "4. Conecta tu iPhone compatible, habilita Developer Mode si se solicita y selecciona el dispositivo.\n" +
            "5. Ejecuta Product > Run y permite el uso de la camara.\n" +
            "El JSON contiene resultados precalculados: no se ejecuta OpenSees en el iPhone.\n");
        try
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                // ditto preserves executable permissions and symlinks from the Xcode export.
                var info = new System.Diagnostics.ProcessStartInfo {
                    FileName = "/usr/bin/ditto",
                    Arguments = "-c -k --sequesterRsrc --keepParent " + Quote(source) + " " + Quote(temporaryZip),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = System.Diagnostics.Process.Start(info))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0) throw new BuildFailedException("ditto no pudo crear el ZIP.");
                }
            }
            else
            {
                // Mono's ZIP writer cannot open some of IL2CPP's long Windows paths.
                // Windows ships libarchive/bsdtar, which can include them without truncation.
                string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
                if (!File.Exists(tar)) throw new BuildFailedException("Falta tar.exe de Windows para empaquetar rutas largas.");
                var info = new System.Diagnostics.ProcessStartInfo {
                    FileName = tar,
                    Arguments = "--format=zip -cf " + WindowsQuote(temporaryZip) + " -C " +
                        WindowsQuote(Path.GetDirectoryName(source)) + " " + WindowsQuote(Path.GetFileName(source)),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = System.Diagnostics.Process.Start(info))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0) throw new BuildFailedException("tar.exe no pudo crear el ZIP completo.");
                }
                // Windows archives otherwise lose the execute bit required by Xcode build tools.
                // Patch directory metadata directly. Mono's ZipArchive Update can
                // rewrite empty deflated entries incorrectly when reading a tar ZIP.
                MarkZipAsUnix(temporaryZip);
            }
            using (ZipArchive archive = ZipFile.OpenRead(temporaryZip))
                if (!archive.Entries.Any(entry => entry.FullName.EndsWith("Unity-iPhone.xcodeproj/project.pbxproj", StringComparison.Ordinal)))
                    throw new BuildFailedException("El ZIP no contiene el proyecto Xcode completo.");
            File.Move(temporaryZip, zipPath);
            LastPackagedBuildPath = zipPath;
            Debug.Log("[MCOC iOS] ZIP Xcode creado: " + zipPath);
            return zipPath;
        }
        catch
        {
            if (File.Exists(temporaryZip)) File.Delete(temporaryZip);
            throw;
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private static string WindowsQuote(string value) => "\"" + value + "\"";

    private static void MarkZipAsUnix(string zipPath)
    {
        // ZIP files written on Windows declare DOS as their creator platform.
        // Mark the central-directory entries as Unix so macOS honors execute bits.
        using (var stream = new FileStream(zipPath, FileMode.Open, FileAccess.ReadWrite))
        using (var reader = new BinaryReader(stream))
        using (var writer = new BinaryWriter(stream))
        {
            long endRecord = -1;
            for (long position = stream.Length - 22; position >= Math.Max(0, stream.Length - 65557); position--)
            {
                stream.Position = position;
                if (reader.ReadUInt32() != 0x06054b50) continue;
                stream.Position = position + 20;
                if (position + 22 + reader.ReadUInt16() == stream.Length) { endRecord = position; break; }
            }
            if (endRecord < 0) throw new InvalidDataException("No se encontro el directorio del ZIP.");
            stream.Position = endRecord + 10;
            ulong count = reader.ReadUInt16();
            stream.Position = endRecord + 16;
            long central = reader.ReadUInt32();
            if (count == ushort.MaxValue || central == uint.MaxValue)
            {
                stream.Position = endRecord - 20;
                if (reader.ReadUInt32() != 0x07064b50) throw new InvalidDataException("Falta el locator ZIP64.");
                reader.ReadUInt32();
                long zip64 = checked((long)reader.ReadUInt64());
                stream.Position = zip64;
                if (reader.ReadUInt32() != 0x06064b50) throw new InvalidDataException("Directorio ZIP64 invalido.");
                stream.Position = zip64 + 32;
                count = reader.ReadUInt64();
                stream.Position = zip64 + 48;
                central = checked((long)reader.ReadUInt64());
            }
            for (ulong index = 0; index < count; index++)
            {
                stream.Position = central;
                if (reader.ReadUInt32() != 0x02014b50) throw new InvalidDataException("Entrada ZIP invalida.");
                stream.Position = central + 28;
                int nameLength = reader.ReadUInt16();
                int extraLength = reader.ReadUInt16();
                int commentLength = reader.ReadUInt16();
                stream.Position = central + 46;
                string entryName = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                string fileName = entryName.Substring(entryName.LastIndexOf('/') + 1);
                if (fileName.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("usymtool", StringComparison.OrdinalIgnoreCase) ||
                    fileName == "il2cpp" || fileName == "bee_backend")
                {
                    stream.Position = central + 38;
                    writer.Write(0x81ED0000U);
                }
                stream.Position = central + 5;
                writer.Write((byte)3);
                central += 46L + nameLength + extraLength + commentLength;
            }
        }
    }

    private static void ValidateArchive()
    {
        string fixture = Path.Combine(ProjectRoot, "Temp", "IOSZipCheck_" + Stamp());
        string project = Path.Combine(fixture, "Unity-iPhone.xcodeproj");
        string script = Path.Combine(fixture, "prueba.sh");
        string projectFile = Path.Combine(project, "project.pbxproj");
        string emptyFile = Path.Combine(fixture, "empty.txt");
        string zip = null;
        try
        {
            Directory.CreateDirectory(project);
            File.WriteAllText(projectFile, "archivo de prueba del empaquetador");
            File.WriteAllText(script, "#!/bin/sh\nexit 0\n");
            File.WriteAllText(emptyFile, "");
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = "/bin/chmod", Arguments = "+x " + Quote(script), UseShellExecute = false
                })) { process.WaitForExit(); if (process.ExitCode != 0) throw new IOException("chmod fallo en la prueba."); }
            }
            zip = PackageXcode(fixture);
            using (var archive = ZipFile.OpenRead(zip))
            {
                var entry = archive.Entries.Single(item => item.Name == "prueba.sh");
                if (((uint)entry.ExternalAttributes >> 16 & 0x49) != 0x49)
                    throw new InvalidDataException("El ZIP perdio los permisos de ejecucion.");
                using (var text = new StreamReader(entry.Open()))
                    if (text.ReadToEnd() != "#!/bin/sh\nexit 0\n") throw new InvalidDataException("El ZIP cambio el contenido.");
                var empty = archive.Entries.Single(item => item.Name == "empty.txt");
                using (var stream = empty.Open())
                    if (stream.ReadByte() != -1) throw new InvalidDataException("El ZIP cambio un archivo vacio.");
            }
            Debug.Log("[MCOC iOS] Empaquetador ZIP: PASS (contenido y permisos). No se ha compilado Xcode.");
        }
        finally
        {
            // Remove only the explicitly created test files; no recursive deletion.
            if (zip != null && File.Exists(zip)) File.Delete(zip);
            if (File.Exists(script)) File.Delete(script);
            if (File.Exists(emptyFile)) File.Delete(emptyFile);
            if (File.Exists(projectFile)) File.Delete(projectFile);
            string guide = Path.Combine(fixture, "LEEME_XCODE.txt");
            if (File.Exists(guide)) File.Delete(guide);
            if (Directory.Exists(project)) Directory.Delete(project);
            if (Directory.Exists(fixture)) Directory.Delete(fixture);
        }
    }
}

public sealed class IOSZipBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => int.MaxValue;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.iOS) IOSBuild.ValidateIOS();
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.iOS)
            IOSBuild.PackageXcode(report.summary.outputPath);
    }
}
