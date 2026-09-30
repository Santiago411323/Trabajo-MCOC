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
        GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(StructuralARSceneSetup.EnvironmentPath);
        Require(environment != null, "Falta el prefab de entorno XR Simulation.");
        Require(environment.GetComponents<Component>().Any(component => component != null && component.GetType().Name == "SimulationEnvironment"),
            "El prefab no tiene SimulationEnvironment.");
        Require(environment.GetComponentsInChildren<Collider>(true).Length >= 2,
            "El entorno no contiene la viga y columna fisicas de practica.");
        Require(!environment.GetComponentsInChildren<Component>(true).Any(component => component != null && component.GetType().Name == "SimulatedTrackedImage"),
            "El entorno sigue dependiendo de una imagen de referencia.");

        XRGeneralSettings general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        Require(general != null && general.Manager != null &&
                general.Manager.activeLoaders.Any(loader => loader != null && loader.GetType().FullName == "UnityEngine.XR.Simulation.SimulationLoader"),
            "XR Simulation Loader no esta habilitado para Standalone.");
        Require(general.InitManagerOnStart,
            "El inicio automatico de XR Simulation no esta habilitado para Play Mode.");

        IOSBuild.ValidateIOS();

        Scene previous = SceneManager.GetActiveScene();
        bool alreadyOpen = previous.IsValid() && previous.path == StructuralARSceneSetup.ScenePath;
        Scene scene = alreadyOpen ? previous :
            EditorSceneManager.OpenScene(StructuralARSceneSetup.ScenePath, OpenSceneMode.Additive);
        StructuralARController controller = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<StructuralARController>(true)).FirstOrDefault();
        ARRaycastManager raycasts = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<ARRaycastManager>(true)).FirstOrDefault();
        ARAnchorManager anchors = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<ARAnchorManager>(true)).FirstOrDefault();
        Require(controller != null && raycasts != null && anchors != null, "Faltan raycast/anchor AR en la escena.");
        Camera camera = controller.arCamera;
        Require(camera != null && camera.enabled && camera.gameObject.activeInHierarchy &&
            camera.GetComponent<ARCameraManager>() != null && camera.GetComponent<ARCameraManager>().enabled &&
            camera.GetComponent<ARCameraBackground>() != null && camera.GetComponent<ARCameraBackground>().enabled,
            "La camara AR necesita Camera, ARCameraManager y ARCameraBackground activos.");
        Require(scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ARSession>(true))
            .Any(session => session.enabled && session.gameObject.activeInHierarchy), "Falta una sesion AR activa.");
        Require(scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ARTrackedImageManager>(true)).Count() == 0,
            "La escena todavia tiene un manager de imagenes.");
        Require(controller.preferredElementTag == StructuralARSceneSetup.ElementTag, "El elementTag configurado cambio.");
        Require(controller.useFreePlacement && controller.fallbackDistanceMeters >= 0.3f,
            "La escena debe iniciar permitiendo fijar I y J sin puntos AR.");
        if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);
        if (!alreadyOpen && previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);

        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/estructura_p1l4_unity.json");
        Require(json != null, "Falta el JSON exportado por OpenSees.");
        StructureData data = JsonUtility.FromJson<StructureData>(json.text);
        UnityData.LoadData(data);
        ElementData beam = (data.elements ?? new ElementData[0]).FirstOrDefault(element =>
            element != null && element.type == "viga" && element.elementTag == StructuralARSceneSetup.ElementTag);
        ElementData column = (data.elements ?? new ElementData[0]).FirstOrDefault(element =>
            element != null && element.type == "columna" &&
            (data.nodes ?? new NodeData[0]).Any(node => node.id == element.nodeI) &&
            (data.nodes ?? new NodeData[0]).Any(node => node.id == element.nodeJ));
        Require(beam != null && column != null, "Faltan viga E1_72 o columna real.");
        foreach (ElementData member in new[] { beam, column })
        {
            NodeData i = data.nodes.FirstOrDefault(node => node.id == member.nodeI);
            NodeData j = data.nodes.FirstOrDefault(node => node.id == member.nodeJ);
            Require(i != null && j != null, "Faltan nodos I/J de " + member.elementTag + ".");
            Vector3 realI = new Vector3(i.x, i.z, i.y);
            Vector3 realJ = new Vector3(j.x, j.z, j.y);
            float realLength = Vector3.Distance(realI, realJ);
            Require(realLength > 0.05f,
                "El modelo no tiene una longitud util para " + member.elementTag + ".");
            string practiceName = member.type == "columna"
                ? "Columna fisica de practica (sin ID automatico)"
                : "Viga fisica de practica (sin ID automatico)";
            Transform practice = environment.transform.Find(practiceName);
            Require(practice != null, "Falta el objeto de practica para " + member.elementTag + ".");
            float practiceLength = member.type == "columna"
                ? practice.localScale.y : practice.localScale.x;
            Require(Mathf.Abs(practiceLength - realLength) < 0.0001f,
                "La longitud del objeto de practica no coincide con el JSON en " + member.elementTag + ".");
        }
        foreach (ElementData member in new[] { beam, column })
        {
            foreach (string combo in new[] { "C1", "C2", "C3" })
                foreach (float t in new[] { 0f, 0.5f, 1f })
                    Require(UnityData.TryGetSectionForces(member.id, combo, t, out _),
                        "Faltan fuerzas OpenSees " + combo + " en " + t + " para " + member.elementTag + ".");
        }

        Debug.Log("[StructuralARValidation][PASS] I + J USER POINTS -> FACE -> AR ANCHOR -> " +
                  beam.elementTag + " / " + column.elementTag + " -> DIAGRAMS -> OPENSEES C1/C2/C3");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[StructuralARValidation][FAIL] " + message);
    }
}
