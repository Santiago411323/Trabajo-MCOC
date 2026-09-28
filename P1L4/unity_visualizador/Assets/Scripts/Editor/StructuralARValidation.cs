using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

public static class StructuralARValidation
{
    [MenuItem("MCOC/AR/Validar configuracion AR")]
    public static void ValidateFromMenu()
    {
        ValidateOrThrow();
        EditorUtility.DisplayDialog("Validacion AR", "Configuracion y trazabilidad estructural: PASS", "Aceptar");
    }

    public static void ValidateBatch()
    {
        ValidateOrThrow();
        EditorApplication.Exit(0);
    }

    public static void ValidateOrThrow()
    {
        Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(StructuralARSceneSetup.ScenePath) != null,
            "Falta StructuralARScene.");
        XRReferenceImageLibrary library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(StructuralARSceneSetup.ImageLibraryPath);
        Require(library != null && library.count == 1, "Reference Image Library invalida.");
        Require(library[0].name == StructuralARSceneSetup.MarkerName, "Nombre de imagen de referencia incorrecto.");
        Require(library[0].specifySize && Vector2.Distance(library[0].size, new Vector2(0.18f, 0.18f)) < 0.0001f,
            "Dimension fisica del marcador incorrecta.");

        GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(StructuralARSceneSetup.EnvironmentPath);
        Require(environment != null, "Falta el prefab de entorno XR Simulation.");
        Require(environment.GetComponents<Component>().Any(component => component != null && component.GetType().Name == "SimulationEnvironment"),
            "El prefab no tiene SimulationEnvironment.");
        Require(environment.GetComponentsInChildren<Component>(true).Any(component => component != null && component.GetType().Name == "SimulatedTrackedImage"),
            "El entorno no tiene SimulatedTrackedImage.");

        XRGeneralSettings general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        Require(general != null && general.Manager != null &&
                general.Manager.activeLoaders.Any(loader => loader != null && loader.GetType().FullName == "UnityEngine.XR.Simulation.SimulationLoader"),
            "XR Simulation Loader no esta habilitado para Standalone.");
        Require(general.InitManagerOnStart,
            "El inicio automatico de XR Simulation no esta habilitado para Play Mode.");

        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.OpenScene(StructuralARSceneSetup.ScenePath, OpenSceneMode.Additive);
        StructuralARController controller = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<StructuralARController>(true)).FirstOrDefault();
        ARTrackedImageManager images = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<ARTrackedImageManager>(true)).FirstOrDefault();
        ARAnchorManager anchors = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<ARAnchorManager>(true)).FirstOrDefault();
        Require(controller != null && images != null && anchors != null, "Faltan componentes AR en la escena.");
        Require(ReferenceEquals(images.referenceLibrary, library), "La escena no usa la biblioteca MCOC.");
        Require(controller.preferredElementTag == StructuralARSceneSetup.ElementTag, "El elementTag configurado cambio.");
        EditorSceneManager.CloseScene(scene, true);
        if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);

        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/estructura_p1l4_unity.json");
        Require(json != null, "Falta el JSON exportado por OpenSees.");
        StructureData data = JsonUtility.FromJson<StructureData>(json.text);
        ElementData column = (data.elements ?? new ElementData[0]).FirstOrDefault(element =>
            element != null && element.type == "columna" && element.elementTag == StructuralARSceneSetup.ElementTag);
        Require(column != null, "El elementTag AR no corresponde a una columna real.");
        Require((data.nodes ?? new NodeData[0]).Any(node => node.id == column.nodeI) &&
                (data.nodes ?? new NodeData[0]).Any(node => node.id == column.nodeJ),
            "La columna AR no conserva nodos reales.");

        UnityData.LoadData(data);
        foreach (string combo in new[] { "C1", "C2", "C3" })
            Require(UnityData.TryGetSectionForces(column.id, combo, 0.5f, out _),
                "Faltan fuerzas OpenSees " + combo + " para " + column.elementTag + ".");

        Debug.Log("[StructuralARValidation][PASS] REFERENCE IMAGE -> SIMULATED TRACKED IMAGE -> " +
                  "AR SCENE -> ANCHOR MANAGER -> " + column.elementTag + " -> OPENSEES C1/C2/C3");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[StructuralARValidation][FAIL] " + message);
    }
}
