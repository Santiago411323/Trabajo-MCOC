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
    public const string ElementTag = "E1_72";

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
            "Se crearon la escena independiente y el entorno XR Simulation sin marcador.\n\n" +
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
        IOSBuild.ConfigureIOS();
        GameObject environment = CreateOrUpdateSimulationEnvironment();
        SetActiveSimulationEnvironment(environment);
        CreateOrUpdateScene();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[StructuralARSetup] Escena sin marcador, entorno y XR Simulation configurados.");
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

    private static GameObject CreateOrUpdateSimulationEnvironment()
    {
        Type environmentType = FindComponentType("SimulationEnvironment");
        if (environmentType == null)
            throw new InvalidOperationException("AR Foundation no expuso SimulationEnvironment.");

        GameObject root = new GameObject("MCOC Structural XR Environment");
        Component simulationEnvironment = root.AddComponent(environmentType);
        ConfigureSimulationEnvironment(simulationEnvironment);

        // The practice members have the same dimensions as their exported
        // counterparts. Otherwise a 1:1 I-J calibration cannot be completed
        // in XR Simulation.
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(
            "Assets/Resources/estructura_p1l4_unity.json");
        if (json == null) throw new InvalidOperationException("Falta el JSON estructural para el entorno AR.");
        StructureData data = JsonUtility.FromJson<StructureData>(json.text);
        ElementData beam = data.elements.FirstOrDefault(e => e != null && e.elementTag == ElementTag);
        ElementData column = data.elements.FirstOrDefault(e => e != null && e.elementTag == "E1_229");
        if (beam == null || column == null)
            throw new InvalidOperationException("Faltan E1_72 o E1_229 para practicar la calibracion.");
        float beamLength = MemberLength(data, beam);
        float columnLength = MemberLength(data, column);

        GameObject sampleBeam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sampleBeam.name = "Viga fisica de practica (sin ID automatico)";
        sampleBeam.transform.SetParent(root.transform, false);
        sampleBeam.transform.localPosition = Vector3.zero;
        sampleBeam.transform.localScale = new Vector3(beamLength, beam.height_m, beam.width_m);
        GameObject sampleColumn = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sampleColumn.name = "Columna fisica de practica (sin ID automatico)";
        sampleColumn.transform.SetParent(root.transform, false);
        sampleColumn.transform.localPosition = new Vector3(beamLength * 0.5f, -columnLength * 0.5f, 0f);
        sampleColumn.transform.localScale = new Vector3(column.width_m, columnLength, column.height_m);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, EnvironmentPath);
        UnityEngine.Object.DestroyImmediate(root);
        if (prefab == null) throw new InvalidOperationException("No se pudo guardar el entorno XR Simulation.");
        return prefab;
    }

    private static float MemberLength(StructureData data, ElementData member)
    {
        NodeData i = data.nodes.FirstOrDefault(n => n.id == member.nodeI);
        NodeData j = data.nodes.FirstOrDefault(n => n.id == member.nodeJ);
        if (i == null || j == null)
            throw new InvalidOperationException("Faltan nodos de " + member.elementTag + ".");
        float length = Vector3.Distance(new Vector3(i.x, i.y, i.z), new Vector3(j.x, j.y, j.z));
        if (length < 0.01f) throw new InvalidOperationException("Longitud invalida en " + member.elementTag + ".");
        return length;
    }

    private static void ConfigureSimulationEnvironment(Component component)
    {
        // The simulated camera starts facing a beam and a column. They are
        // visual practice objects, never automatically identified as model IDs.
        Pose start = new Pose(new Vector3(0f, 0f, -8f), Quaternion.identity);
        SerializedObject serialized = new SerializedObject(component);
        SerializedProperty bounds = serialized.FindProperty("m_CameraMovementBounds");
        SerializedProperty pose = serialized.FindProperty("m_CameraStartingPose");
        if (bounds == null || pose == null)
            throw new InvalidOperationException("XR Simulation no expuso los limites o pose inicial de camara.");
        // Wide enough to stand at either node and walk around the 7.51 m beam.
        bounds.boundsValue = new Bounds(new Vector3(0f, -1f, -2f), new Vector3(16f, 12f, 20f));
        SerializedProperty position = pose.FindPropertyRelative("position") ??
                                      pose.FindPropertyRelative("m_Position");
        SerializedProperty rotation = pose.FindPropertyRelative("rotation") ??
                                      pose.FindPropertyRelative("m_Rotation");
        if (position == null || rotation == null)
            throw new InvalidOperationException("XR Simulation no expuso la pose inicial de camara.");
        position.vector3Value = start.position;
        rotation.quaternionValue = start.rotation;
        serialized.ApplyModifiedPropertiesWithoutUndo();
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

    private static void CreateOrUpdateScene()
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
            string.IsNullOrEmpty(previous.path) ? NewSceneMode.Single : NewSceneMode.Additive);
        scene.name = "StructuralARScene";
        SceneManager.SetActiveScene(scene);

        GameObject sessionObject = new GameObject("AR Session");
        sessionObject.AddComponent<ARSession>();
        Type inputManagerType = FindComponentType("ARInputManager");
        if (inputManagerType != null) sessionObject.AddComponent(inputManagerType);

        GameObject originObject = new GameObject("XR Origin (Structural AR)");
        XROrigin origin = originObject.AddComponent<XROrigin>();
        ARRaycastManager raycastManager = originObject.AddComponent<ARRaycastManager>();
        originObject.AddComponent<ARPointCloudManager>();
        originObject.AddComponent<ARPlaneManager>();
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
        controller.raycastManager = raycastManager;
        controller.anchorManager = anchorManager;
        controller.arCamera = camera;
        controller.useFreePlacement = true;
        controller.fallbackDistanceMeters = 3f;
        controller.preferredElementTag = ElementTag;
        controller.uniformScale = 1f;
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
