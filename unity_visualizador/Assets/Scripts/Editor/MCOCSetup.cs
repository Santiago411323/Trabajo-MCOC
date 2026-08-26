using UnityEditor;
using UnityEngine;

public static class MCOCSetup
{
    [MenuItem("MCOC/Crear Visualizador")]
    public static void CrearVisualizador()
    {
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/estructura_3d_unity.json");
        if (json == null)
        {
            EditorUtility.DisplayDialog(
                "Falta JSON",
                "No se encontro Assets/estructura_3d_unity.json. Copia ese archivo dentro de Assets y vuelve a intentar.",
                "OK");
            return;
        }

        GameObject existing = GameObject.Find("StructureViewer");
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }

        GameObject viewerObject = new GameObject("StructureViewer");
        StructureViewer viewer = viewerObject.AddComponent<StructureViewer>();
        viewer.structureJson = json;

        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
        }

        camera.transform.position = new Vector3(8f, 6f, -8f);
        camera.transform.rotation = Quaternion.Euler(35f, -35f, 0f);

        if (camera.GetComponent<ElementPicker>() == null)
        {
            ElementPicker picker = camera.gameObject.AddComponent<ElementPicker>();
            picker.targetCamera = camera;
        }

        if (camera.GetComponent<OrbitCamera>() == null)
        {
            camera.gameObject.AddComponent<OrbitCamera>();
        }

        Selection.activeGameObject = viewerObject;
        EditorUtility.DisplayDialog("Listo", "Visualizador creado. Ahora presiona Play.", "OK");
    }
}
