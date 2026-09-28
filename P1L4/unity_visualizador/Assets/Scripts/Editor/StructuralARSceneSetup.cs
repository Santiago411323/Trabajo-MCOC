using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.ARSubsystems;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using Unity.XR.CoreUtils;

public static class StructuralARSceneSetup
{
    public const string ScenePath = "Assets/Scenes/StructuralARScene.unity";
    public const string MarkerTexturePath = "Assets/StructuralAR/MCOC_STRUCTURAL_MARKER.png";
    public const string ImageLibraryPath = "Assets/StructuralAR/MCOCStructuralReferenceImages.asset";
    public const string EnvironmentPath = "Assets/StructuralAR/Simulation/MCOCStructuralSimulationEnvironment.prefab";
    public const string PreferencesPath = "Assets/XR/UserSimulationSettings/Resources/XRSimulationPreferences.asset";
    public const string MarkerName = "MCOC_STRUCTURAL_MARKER";
    public const string ElementTag = "E1_229";

    [InitializeOnLoadMethod]
    private static void BootstrapMissingAssets()
    {
        // The first successful editor compilation creates the independent AR
        // assets automatically.  The guard prevents repeating the operation on
        // every domain reload and leaves StructureViewerScene untouched.
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                return;

            try
            {
                CreateAll(false);
                StructuralARValidation.ValidateOrThrow();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        };
    }

    [MenuItem("MCOC/AR/Crear o reparar escena Structural AR")]
    public static void CreateSceneFromMenu()
    {
        CreateAll(false);
        EditorUtility.DisplayDialog(
            "Structural AR listo",
            "Se crearon la escena independiente, la biblioteca de imagenes y el entorno XR Simulation.\n\n" +
            "Abre Assets/Scenes/StructuralARScene y presiona Play.",
            "Aceptar");
    }

    // Entry point used by command-line validation.
    public static void CreateAllForBatch() => CreateAll(true);

    private static void CreateAll(bool batch)
    {
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Assets/StructuralAR/Simulation");
        Directory.CreateDirectory("Assets/XR/UserSimulationSettings/Resources");

        EnableXRSimulationLoader();
        Texture2D marker = CreateOrLoadMarkerTexture();
        XRReferenceImageLibrary library = CreateOrUpdateImageLibrary(marker);
        GameObject environment = CreateOrUpdateSimulationEnvironment(marker);
        SetActiveSimulationEnvironment(environment);
        CreateOrUpdateScene(library);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[StructuralARSetup] Escena, biblioteca, entorno y XR Simulation configurados.");
        if (batch) StructuralARValidation.ValidateOrThrow();
    }

    private static void EnableXRSimulationLoader()
    {
        const string settingsDirectory = "Assets/XR/Settings";
        const string settingsPath = settingsDirectory + "/XRGeneralSettingsPerBuildTarget.asset";
        Directory.CreateDirectory(settingsDirectory);

        XRGeneralSettings general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (general == null)
        {
            XRGeneralSettingsPerBuildTarget perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(settingsPath);
            if (perTarget == null)
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, settingsPath);
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
        }
        if (general == null || general.Manager == null)
            throw new InvalidOperationException("XR Plug-in Management no creo sus settings para Standalone.");

        const string loaderType = "UnityEngine.XR.Simulation.SimulationLoader";
        bool alreadyAssigned = general.Manager.activeLoaders.Any(loader => loader != null && loader.GetType().FullName == loaderType);
        if (!alreadyAssigned && !XRPackageMetadataStore.AssignLoader(general.Manager, loaderType, BuildTargetGroup.Standalone))
            throw new InvalidOperationException("No fue posible activar XR Simulation para Standalone.");

        // XRGeneralSettings 4.5 owns the simulation lifecycle when this flag is
        // enabled. It initializes the assigned loader and starts subsystems
        // before the AR scene begins.
        general.InitManagerOnStart = true;

        EditorUtility.SetDirty(general);
        EditorUtility.SetDirty(general.Manager);
    }

    private static Texture2D CreateOrLoadMarkerTexture()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(MarkerTexturePath);
        if (existing != null) return existing;

        const int size = 512;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color dark = new Color(0.02f, 0.025f, 0.035f, 1f);
        Color light = new Color(0.94f, 0.96f, 0.98f, 1f);
        Color cyan = new Color(0.02f, 0.74f, 0.92f, 1f);

        // Deterministic asymmetric cells give the tracker many unique feature points
        // and remove the rotational ambiguity of the old checkerboard marker.
        int[,] cells =
        {
            { 1, 0, 1, 1, 0, 0, 1 },
            { 0, 1, 0, 0, 1, 1, 1 },
            { 1, 1, 1, 0, 1, 0, 0 },
            { 1, 0, 0, 1, 0, 1, 0 },
            { 0, 1, 1, 1, 0, 0, 1 },
            { 1, 0, 1, 0, 1, 1, 0 },
            { 0, 0, 1, 1, 1, 0, 1 }
        };

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool outer = x < 26 || y < 26 || x >= size - 26 || y >= size - 26;
            bool inner = x < 42 || y < 42 || x >= size - 42 || y >= size - 42;
            int cx = Mathf.Clamp((x - 54) * 7 / (size - 108), 0, 6);
            int cy = Mathf.Clamp((y - 54) * 7 / (size - 108), 0, 6);
            bool diagonal = Mathf.Abs(x - y) < 5 && x > 80 && x < size - 80;
            Color color = outer ? dark : inner ? cyan : cells[cy, cx] == 1 ? dark : light;
            if (diagonal) color = cyan;
            texture.SetPixel(x, y, color);
        }

        texture.Apply();
        File.WriteAllBytes(MarkerTexturePath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(MarkerTexturePath, ImportAssetOptions.ForceSynchronousImport);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(MarkerTexturePath);
        importer.textureType = TextureImporterType.Default;
        importer.isReadable = true;
        importer.mipmapEnabled = false;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(MarkerTexturePath);
    }

    private static XRReferenceImageLibrary CreateOrUpdateImageLibrary(Texture2D marker)
    {
        XRReferenceImageLibrary library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(ImageLibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
            AssetDatabase.CreateAsset(library, ImageLibraryPath);
        }

        while (library.count > 0) library.RemoveAt(0);
        library.Add();
        library.SetName(0, MarkerName);
        library.SetTexture(0, marker, true);
        library.SetSpecifySize(0, true);
        library.SetSize(0, new Vector2(0.18f, 0.18f));
        EditorUtility.SetDirty(library);
        return library;
    }

    private static GameObject CreateOrUpdateSimulationEnvironment(Texture2D marker)
    {
        Type environmentType = FindComponentType("SimulationEnvironment");
        Type trackedImageType = FindComponentType("SimulatedTrackedImage");
        if (environmentType == null || trackedImageType == null)
            throw new InvalidOperationException("AR Foundation no expuso SimulationEnvironment/SimulatedTrackedImage.");

        GameObject root = new GameObject("MCOC Structural XR Environment");
        Component simulationEnvironment = root.AddComponent(environmentType);
        ConfigureSimulationEnvironment(simulationEnvironment);

        GameObject markerObject = new GameObject(MarkerName);
        markerObject.transform.SetParent(root.transform, false);
        markerObject.transform.localPosition = Vector3.zero;
        markerObject.transform.localRotation = Quaternion.identity;
        Component simulatedImage = markerObject.AddComponent(trackedImageType);
        ConfigureSimulatedTrackedImage(simulatedImage, marker);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, EnvironmentPath);
        UnityEngine.Object.DestroyImmediate(root);
        if (prefab == null) throw new InvalidOperationException("No se pudo guardar el entorno XR Simulation.");
        return prefab;
    }

    private static void ConfigureSimulationEnvironment(Component component)
    {
        // The simulated camera starts looking down at a horizontal marker. This
        // keeps the AR column vertical and resting on the marker, as it will on
        // a table or floor during the later physical test.
        Pose start = new Pose(new Vector3(0f, 0.65f, -1.1f), Quaternion.Euler(25f, 0f, 0f));
        PropertyInfo property = component.GetType().GetProperty(
            "cameraStartingPose", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanWrite && property.PropertyType == typeof(Pose))
        {
            property.SetValue(component, start);
            return;
        }

        SerializedObject serialized = new SerializedObject(component);
        SerializedProperty iterator = serialized.GetIterator();
        SerializedProperty poseProperty = null;
        if (iterator.NextVisible(true))
        {
            do
            {
                string key = (iterator.name + " " + iterator.displayName).ToLowerInvariant();
                if (key.Contains("camera") && key.Contains("starting") && key.Contains("pose"))
                {
                    poseProperty = iterator.Copy();
                    break;
                }
            } while (iterator.NextVisible(false));
        }

        if (poseProperty != null)
        {
            SerializedProperty position = poseProperty.FindPropertyRelative("m_Position") ??
                                          poseProperty.FindPropertyRelative("position");
            SerializedProperty rotation = poseProperty.FindPropertyRelative("m_Rotation") ??
                                          poseProperty.FindPropertyRelative("rotation");
            if (position != null) position.vector3Value = start.position;
            if (rotation != null) rotation.quaternionValue = start.rotation;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            Debug.LogWarning("[StructuralARSetup] Se usara la pose inicial por defecto del entorno XR Simulation.");
        }
    }

    private static void ConfigureSimulatedTrackedImage(Component component, Texture2D marker)
    {
        SerializedObject serialized = new SerializedObject(component);
        SerializedProperty image = serialized.FindProperty("m_Image");
        SerializedProperty physicalSize = serialized.FindProperty("m_ImagePhysicalSizeMeters");
        bool imageSet = image != null;
        bool sizeSet = physicalSize != null;
        if (imageSet) image.objectReferenceValue = marker;
        if (sizeSet) physicalSize.vector2Value = new Vector2(0.18f, 0.18f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (!imageSet || !sizeSet)
            throw new InvalidOperationException("No se pudieron configurar Image/Physical Size en SimulatedTrackedImage.");
    }

    private static void SetActiveSimulationEnvironment(GameObject environmentPrefab)
    {
        Type preferencesType = FindType("XRSimulationPreferences");
        if (preferencesType == null || !typeof(ScriptableObject).IsAssignableFrom(preferencesType))
            throw new InvalidOperationException("No se encontro XRSimulationPreferences.");

        ScriptableObject preferences = AssetDatabase.LoadAssetAtPath<ScriptableObject>(PreferencesPath);
        if (preferences == null)
        {
            preferences = ScriptableObject.CreateInstance(preferencesType);
            AssetDatabase.CreateAsset(preferences, PreferencesPath);
        }

        SerializedObject serialized = new SerializedObject(preferences);
        SerializedProperty iterator = serialized.GetIterator();
        bool environmentSet = false;
        if (iterator.NextVisible(true))
        {
            do
            {
                string key = (iterator.name + " " + iterator.displayName).ToLowerInvariant();
                if (iterator.propertyType == SerializedPropertyType.ObjectReference && key.Contains("environment") && key.Contains("prefab"))
                {
                    iterator.objectReferenceValue = environmentPrefab;
                    environmentSet = true;
                }
                else if (iterator.propertyType == SerializedPropertyType.Boolean && key.Contains("navigation"))
                {
                    iterator.boolValue = true;
                }
            } while (iterator.NextVisible(false));
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(preferences);
        if (!environmentSet)
            throw new InvalidOperationException("XRSimulationPreferences no expuso Environment Prefab.");
    }

    private static void CreateOrUpdateScene(XRReferenceImageLibrary library)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        scene.name = "StructuralARScene";
        SceneManager.SetActiveScene(scene);

        GameObject sessionObject = new GameObject("AR Session");
        sessionObject.AddComponent<ARSession>();
        Type inputManagerType = FindComponentType("ARInputManager");
        if (inputManagerType != null) sessionObject.AddComponent(inputManagerType);

        GameObject originObject = new GameObject("XR Origin (Structural AR)");
        XROrigin origin = originObject.AddComponent<XROrigin>();
        ARTrackedImageManager imageManager = originObject.AddComponent<ARTrackedImageManager>();
        imageManager.referenceLibrary = library;
        imageManager.requestedMaxNumberOfMovingImages = 1;
        ARAnchorManager anchorManager = originObject.AddComponent<ARAnchorManager>();

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(originObject.transform, false);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.025f, 0.035f, 0.05f);
        camera.nearClipPlane = 0.01f;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<ARCameraManager>();
        cameraObject.AddComponent<ARCameraBackground>();
        Type poseDriverType = FindComponentType("TrackedPoseDriver");
        if (poseDriverType != null) cameraObject.AddComponent(poseDriverType);
        origin.Camera = camera;

        GameObject controllerObject = new GameObject("Structural AR Controller");
        StructuralARController controller = controllerObject.AddComponent<StructuralARController>();
        controller.structureJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/estructura_p1l4_unity.json");
        controller.trackedImageManager = imageManager;
        controller.anchorManager = anchorManager;
        controller.arCamera = camera;
        controller.markerName = MarkerName;
        controller.preferredElementTag = ElementTag;
        controller.uniformScale = 0.12f;
        controller.positionOffsetMeters = Vector3.zero;
        controller.rotationOffsetEuler = Vector3.zero;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.CloseScene(scene, true);
        if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
    }

    private static Type FindComponentType(string simpleName)
    {
        return TypeCache.GetTypesDerivedFrom<Component>().FirstOrDefault(type => type.Name == simpleName);
    }

    private static Type FindType(string simpleName)
    {
        // Unity 6.6 forbids AppDomain.GetAssemblies in editor code.  The
        // preferences object is a ScriptableObject, so TypeCache gives the same
        // lookup without using the obsolete AppDomain API.
        return TypeCache.GetTypesDerivedFrom<ScriptableObject>()
            .FirstOrDefault(type => type.Name == simpleName);
    }
}
