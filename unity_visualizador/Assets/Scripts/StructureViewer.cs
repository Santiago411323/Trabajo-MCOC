using System.Collections.Generic;
using UnityEngine;

public class StructureViewer : MonoBehaviour
{
    [Header("Datos exportados desde OpenSeesPy")]
    public TextAsset structureJson;

    [Header("Apariencia")]
    public float elementRadius = 0.06f;
    public Material beamMaterial;
    public Material columnMaterial;
    public Material supportMaterial;

    private Material defaultBeamMaterial;
    private Material defaultColumnMaterial;
    private Material defaultSupportMaterial;

    private readonly Dictionary<int, Vector3> nodes = new Dictionary<int, Vector3>();
    private readonly List<ElementSelectable> selectables = new List<ElementSelectable>();
    private DiagramController diagramController;

    private void Start()
    {
        CreateDefaultMaterials();

        if (structureJson == null)
        {
            Debug.LogError("Asigna estructura_3d_unity.json en el campo Structure Json.");
            return;
        }

        StructureData data = JsonUtility.FromJson<StructureData>(structureJson.text);
        CreateNodes(data);
        CreateElements(data);
        CreateSupports();
        CreatePointLoads(data);
        CreateGlobalAxes();
        CreateDiagramController();
    }

    private void CreateNodes(StructureData data)
    {
        foreach (NodeData node in data.nodes)
        {
            nodes[node.id] = ToUnity(node);
        }
    }

    private void CreateElements(StructureData data)
    {
        foreach (ElementData element in data.elements)
        {
            Vector3 start = nodes[element.nodeI];
            Vector3 end = nodes[element.nodeJ];
            Vector3 midpoint = (start + end) * 0.5f;
            Vector3 direction = end - start;

            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = $"Elemento_{element.id}_{element.type}";
            cylinder.transform.SetParent(transform);
            cylinder.transform.position = midpoint;
            cylinder.transform.rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            cylinder.transform.localScale = new Vector3(elementRadius, direction.magnitude * 0.5f, elementRadius);

            Renderer renderer = cylinder.GetComponent<Renderer>();
            renderer.material = element.type == "columna" ? ColumnMaterial() : BeamMaterial();

            ElementSelectable selectable = cylinder.AddComponent<ElementSelectable>();
            selectable.data = element;
            selectable.startPoint = start;
            selectable.endPoint = end;
            selectables.Add(selectable);
        }
    }

    private void CreateDiagramController()
    {
        diagramController = gameObject.AddComponent<DiagramController>();
        diagramController.Initialize(selectables);
    }

    private void CreateSupports()
    {
        for (int id = 1; id <= 4; id++)
        {
            GameObject support = GameObject.CreatePrimitive(PrimitiveType.Cube);
            support.name = $"Apoyo_Empotrado_N{id}";
            support.transform.SetParent(transform);
            support.transform.position = nodes[id] + Vector3.down * 0.08f;
            support.transform.localScale = new Vector3(0.45f, 0.12f, 0.45f);

            Renderer renderer = support.GetComponent<Renderer>();
            renderer.material = SupportMaterial();
        }
    }

    private void CreatePointLoads(StructureData data)
    {
        if (data.pointLoads == null)
        {
            return;
        }

        foreach (PointLoadData load in data.pointLoads)
        {
            if (!nodes.ContainsKey(load.node) || Mathf.Abs(load.fz) < 0.001f)
            {
                continue;
            }

            Vector3 node = nodes[load.node];
            float sign = load.fz < 0f ? -1f : 1f;
            Vector3 start = node + Vector3.up * sign * 0.9f;
            Vector3 end = node + Vector3.up * sign * 0.15f;
            Vector3 direction = end - start;

            GameObject arrow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arrow.name = $"Carga_Puntual_N{load.node}";
            arrow.transform.SetParent(transform);
            arrow.transform.position = (start + end) * 0.5f;
            arrow.transform.rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            arrow.transform.localScale = new Vector3(0.035f, direction.magnitude * 0.5f, 0.035f);
            arrow.GetComponent<Renderer>().material = CreateMaterial(Color.red);

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = $"Punta_Carga_N{load.node}";
            head.transform.SetParent(transform);
            head.transform.position = end;
            head.transform.localScale = new Vector3(0.18f, 0.18f, 0.18f);
            head.GetComponent<Renderer>().material = CreateMaterial(Color.red);
        }
    }

    private void CreateGlobalAxes()
    {
        CreateAxis("X global", Vector3.zero, Vector3.right, Color.red);
        CreateAxis("Y global", Vector3.zero, Vector3.forward, Color.green);
        CreateAxis("Z global", Vector3.zero, Vector3.up, Color.blue);
    }

    private void CreateAxis(string name, Vector3 start, Vector3 direction, Color color)
    {
        GameObject axis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        axis.name = name;
        axis.transform.SetParent(transform);
        axis.transform.position = start + direction * 0.5f;
        axis.transform.rotation = Quaternion.FromToRotation(Vector3.up, direction);
        axis.transform.localScale = new Vector3(0.025f, 0.5f, 0.025f);

        Renderer renderer = axis.GetComponent<Renderer>();
        renderer.material = CreateMaterial(color);
    }

    private Vector3 ToUnity(NodeData node)
    {
        return new Vector3(node.x, node.z, node.y);
    }

    private void CreateDefaultMaterials()
    {
        defaultBeamMaterial = CreateMaterial(new Color(0.0f, 0.62f, 0.85f));
        defaultColumnMaterial = CreateMaterial(new Color(0.18f, 0.18f, 0.24f));
        defaultSupportMaterial = CreateMaterial(new Color(0.95f, 0.38f, 0.12f));
    }

    private Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material material = new Material(shader);
        material.color = color;
        return material;
    }

    private Material BeamMaterial()
    {
        return beamMaterial != null ? beamMaterial : defaultBeamMaterial;
    }

    private Material ColumnMaterial()
    {
        return columnMaterial != null ? columnMaterial : defaultColumnMaterial;
    }

    private Material SupportMaterial()
    {
        return supportMaterial != null ? supportMaterial : defaultSupportMaterial;
    }
}
