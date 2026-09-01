using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
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
    private bool structureCreated;

    private void Start()
    {
        CreateStructure();
    }

    private void OnEnable()
    {
        CreateStructure();
    }

    private void CreateStructure()
    {
        CreateDefaultMaterials();

        if (structureCreated)
        {
            return;
        }

        if (structureJson == null)
        {
            structureJson = Resources.Load<TextAsset>("estructura_edificio_ingenieria_unity");
            if (structureJson == null)
            {
                Debug.LogError("Asigna estructura_edificio_ingenieria_unity.json en el campo Structure Json o copialo a Assets/Resources.");
                return;
            }
        }

        StructureData data = JsonUtility.FromJson<StructureData>(structureJson.text);
        ClearStructureChildren();
        nodes.Clear();
        selectables.Clear();
        CreateNodes(data);
        CreateElements(data);
        CreateSupports(data);
        CreatePointLoads(data);
        CreateGlobalAxes();
        CreateDiagramController();
        structureCreated = true;
    }

    private void ClearStructureChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying)
            {
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }
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

    private void CreateSupports(StructureData data)
    {
        if (data.supports == null || data.supports.Length == 0)
        {
            return;
        }

        foreach (SupportData supportData in data.supports)
        {
            if (!nodes.ContainsKey(supportData.node))
            {
                continue;
            }

            CreateSupportSymbol(supportData);
        }
    }

    private void CreateSupportSymbol(SupportData supportData)
    {
        Vector3 node = nodes[supportData.node];
        string supportType = supportData.type;

        if (supportType == "fixed")
        {
            GameObject support = GameObject.CreatePrimitive(PrimitiveType.Cube);
            support.name = $"Apoyo_Empotrado_N{supportData.node}";
            support.transform.SetParent(transform);
            support.transform.position = node + Vector3.down * 0.08f;
            support.transform.localScale = new Vector3(0.55f, 0.14f, 0.55f);
            support.GetComponent<Renderer>().material = SupportMaterial();
            CreateSupportLabel(supportData, "Empotrado", node);
            return;
        }

        if (supportType == "pinned")
        {
            GameObject support = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            support.name = $"Apoyo_Pasador_N{supportData.node}";
            support.transform.SetParent(transform);
            support.transform.position = node + Vector3.down * 0.16f;
            support.transform.localScale = new Vector3(0.28f, 0.28f, 0.28f);
            support.GetComponent<Renderer>().material = CreateMaterial(new Color(0.45f, 0.2f, 0.9f));

            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = $"Base_Pasador_N{supportData.node}";
            plate.transform.SetParent(transform);
            plate.transform.position = node + Vector3.down * 0.34f;
            plate.transform.localScale = new Vector3(0.5f, 0.08f, 0.5f);
            plate.GetComponent<Renderer>().material = CreateMaterial(new Color(0.45f, 0.2f, 0.9f));
            CreateSupportLabel(supportData, "Pasador", node);
            return;
        }

        CreateRollerSupport(supportData, supportType, node);
        CreateSupportLabel(supportData, SupportLabel(supportType), node);
    }

    private string SupportLabel(string supportType)
    {
        if (supportType == "vertical") return "Vertical";
        if (supportType == "roller_x") return "Rodillo X";
        if (supportType == "roller_y") return "Rodillo Y";
        return "Custom";
    }

    private void CreateSupportLabel(SupportData supportData, string label, Vector3 node)
    {
        GameObject labelObject = new GameObject($"Etiqueta_Apoyo_N{supportData.node}");
        labelObject.transform.SetParent(transform);
        labelObject.transform.position = node + new Vector3(0.15f, 0.25f, 0.15f);

        TextMesh text = labelObject.AddComponent<TextMesh>();
        text.text = $"N{supportData.node}\n{label}";
        text.characterSize = 0.18f;
        text.anchor = TextAnchor.MiddleCenter;
        text.color = Color.yellow;
    }

    private void CreateRollerSupport(SupportData supportData, string supportType, Vector3 node)
    {
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = $"Apoyo_{supportType}_N{supportData.node}";
        plate.transform.SetParent(transform);
        plate.transform.position = node + Vector3.down * 0.08f;
        plate.transform.localScale = new Vector3(0.5f, 0.08f, 0.5f);
        plate.GetComponent<Renderer>().material = CreateMaterial(new Color(0.85f, 0.55f, 0.1f));

        GameObject roller = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        roller.name = $"Rodillo_N{supportData.node}";
        roller.transform.SetParent(transform);
        roller.transform.position = node + Vector3.down * 0.22f;
        roller.transform.rotation = supportType == "roller_y" ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
        roller.transform.localScale = new Vector3(0.12f, 0.28f, 0.12f);
        roller.GetComponent<Renderer>().material = CreateMaterial(new Color(0.55f, 0.35f, 0.12f));
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
