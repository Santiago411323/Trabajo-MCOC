using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

public static class AndroidVRBuild
{
    [MenuItem("MCOC/Android/Configurar AR y VR")]
    public static void ConfigureAndroid()
    {
        const string path = "Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset";
        var settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
        if (settings == null) throw new BuildFailedException("Faltan los settings XR de Android.");
        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
        if (!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        var general = settings.SettingsForBuildTarget(BuildTargetGroup.Android);
        const string ar = "UnityEngine.XR.ARCore.ARCoreLoader";
        foreach (string loader in new[] { ar, "Google.XR.Cardboard.XRLoader" })
            if (!general.Manager.activeLoaders.Any(l => l != null && l.GetType().FullName == loader) &&
                !XRPackageMetadataStore.AssignLoader(general.Manager, loader, BuildTargetGroup.Android))
                throw new BuildFailedException("No se pudo registrar " + loader);
        // ARCore owns startup. Cardboard starts only after the user enters VR.
        general.Manager.TrySetLoaders(general.Manager.activeLoaders.OrderBy(l => l.GetType().FullName == ar ? 0 : 1).ToList());
        general.InitManagerOnStart = true;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        if ((int)PlayerSettings.Android.minSdkVersion < 26)
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        if (PlayerSettings.Android.targetSdkVersion != AndroidSdkVersions.AndroidApiLevelAuto &&
            (int)PlayerSettings.Android.targetSdkVersion < 35)
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)35;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
        PlayerSettings.Android.optimizedFramePacing = false;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.Android.forceInternetPermission = true;
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(StructuralARSceneSetup.ScenePath, true) };
        EditorUtility.SetDirty(settings); EditorUtility.SetDirty(general); EditorUtility.SetDirty(general.Manager);
        AssetDatabase.SaveAssets();
        Debug.Log("[MCOC Android] ARCore primero; Cardboard VR; ARM64/IL2CPP/OpenGLES3/Activity configurados.");
    }

    public static void ValidateBatch()
    {
        try
        {
            ConfigureAndroid();
            StructuralARValidation.ValidateOrThrow(); StructuralARSectorValidation.Validate();
            Debug.Log("[MCOC Android] PASS: configuracion AR/VR y datos.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}

public sealed class AndroidVRPrebuild : IPreprocessBuildWithReport
{
    public int callbackOrder => -100;
    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.Android) AndroidVRBuild.ConfigureAndroid();
    }
}

#if UNITY_ANDROID
// Add SDK dependencies to the generated project without replacing ARCore's templates.
public sealed class AndroidVRGradle : UnityEditor.Android.IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 100;
    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string build = Path.Combine(path, "build.gradle");
        string text = File.ReadAllText(build);
        if (!text.Contains("// MCOC Cardboard dependencies"))
            File.AppendAllText(build, "\n// MCOC Cardboard dependencies\ndependencies {\n" +
                "    implementation 'androidx.appcompat:appcompat:1.6.1'\n" +
                "    implementation 'com.google.android.gms:play-services-vision:20.1.3'\n" +
                "    implementation 'com.google.android.material:material:1.12.0'\n" +
                "    implementation 'com.google.protobuf:protobuf-javalite:3.19.4'\n}\n");
        string properties = Path.GetFullPath(Path.Combine(path, "..", "gradle.properties"));
        string original = File.Exists(properties) ? File.ReadAllText(properties) : "";
        foreach (string line in new[] { "android.enableJetifier=true", "android.useAndroidX=true" })
            if (!original.Contains(line)) File.AppendAllText(properties, "\n" + line + "\n");
    }
}
#endif
