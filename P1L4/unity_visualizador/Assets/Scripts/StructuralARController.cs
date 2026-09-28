using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Connects XR image tracking to the existing structural JSON. It never creates
/// analytical values: geometry and forces always come from estructura_p1l4_unity.
/// </summary>
public sealed class StructuralARController : MonoBehaviour
{
    [Header("Project data")]
    public TextAsset structureJson;
    public string preferredElementTag = "E1_229";

    [Header("AR tracking")]
    public ARTrackedImageManager trackedImageManager;
    public ARAnchorManager anchorManager;
    public Camera arCamera;
    public string markerName = "MCOC_STRUCTURAL_MARKER";

    [Header("Column relative to marker")]
    [Min(0.001f)] public float uniformScale = 0.12f;
    public Vector3 positionOffsetMeters = Vector3.zero;
    public Vector3 rotationOffsetEuler = Vector3.zero;

    [Header("Optional legacy status label")]
    public Text statusText;

    private readonly Dictionary<int, NodeData> nodes = new Dictionary<int, NodeData>();
    private StructureData data;
    private ElementData selectedColumn;
    private ARAnchor structuralAnchor;
    private GameObject columnObject;
    private Material columnMaterial;
    private bool imageRecognized;
    private bool poseObtained;
    private bool anchorRequested;
    private bool columnCreated;
    private bool elementSelected;
    private bool resultsAvailable;
    private string activeCombo = "C1";
    private string status = "Esperando imagen XR Simulation...";
    private Rect panelRect = new Rect(18f, 18f, 410f, 510f);

    public string ActiveElementTag => selectedColumn == null ? string.Empty : selectedColumn.elementTag;
    public string ActiveCombination => activeCombo;
    public bool ImageRecognized => imageRecognized;
    public bool PoseObtained => poseObtained;
    public bool AnchorSupported => anchorManager != null && anchorManager.subsystem != null && anchorManager.subsystem.running;
    public bool ColumnCreated => columnCreated;
    public bool ResultsAvailable => resultsAvailable;

    private void Awake()
    {
        trackedImageManager = trackedImageManager != null
            ? trackedImageManager
            : FindAnyObjectByType<ARTrackedImageManager>();
        anchorManager = anchorManager != null
            ? anchorManager
            : FindAnyObjectByType<ARAnchorManager>();
        arCamera = arCamera != null ? arCamera : Camera.main;
        structureJson = structureJson != null
            ? structureJson
            : Resources.Load<TextAsset>("estructura_p1l4_unity");

        LoadStructure();
        SetStatus(selectedColumn == null
            ? "ERROR: no existe la columna solicitada en el JSON."
            : "Esperando imagen XR Simulation: " + markerName);
    }

    private void OnEnable()
    {
        if (trackedImageManager != null)
            trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
    }

    private void OnDisable()
    {
        if (trackedImageManager != null)
            trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
    }

    private void Update()
    {
        UpdateAnchorState();
        HandleColumnSelection();
    }

    private void OnDestroy()
    {
        if (columnMaterial != null) Destroy(columnMaterial);
    }

    private void LoadStructure()
    {
        if (structureJson == null)
        {
            Debug.LogError("[StructuralAR] Falta Resources/estructura_p1l4_unity.json.");
            return;
        }

        data = JsonUtility.FromJson<StructureData>(structureJson.text);
        if (data == null)
        {
            Debug.LogError("[StructuralAR] El JSON estructural no se pudo interpretar.");
            return;
        }

        UnityData.LoadData(data);
        nodes.Clear();
        foreach (NodeData node in data.nodes ?? new NodeData[0]) nodes[node.id] = node;

        // Exact match only. Falling back to a different column would break the
        // elementTag/result traceability required by the AR verification.
        foreach (ElementData element in data.elements ?? new ElementData[0])
        {
            if (element != null && element.type == "columna" && element.elementTag == preferredElementTag)
            {
                selectedColumn = element;
                break;
            }
        }

        if (selectedColumn == null)
            Debug.LogError("[StructuralAR] No se encontro la columna exacta " + preferredElementTag + ".");
    }

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        foreach (ARTrackedImage image in args.added) TryUseTrackedImage(image);
        foreach (ARTrackedImage image in args.updated) TryUseTrackedImage(image);
    }

    private void TryUseTrackedImage(ARTrackedImage image)
    {
        if (image == null || image.trackingState != TrackingState.Tracking) return;
        if (!string.IsNullOrEmpty(markerName) && image.referenceImage.name != markerName) return;

        imageRecognized = true;
        Pose markerPose = new Pose(image.transform.position, image.transform.rotation);
        poseObtained = IsFinite(markerPose.position) && IsFinite(markerPose.rotation);
        if (!poseObtained)
        {
            SetStatus("Imagen reconocida, pero la pose recibida no es valida.");
            return;
        }

        if (structuralAnchor == null) CreateAnchor(markerPose);
    }

    private async void CreateAnchor(Pose pose)
    {
        if (selectedColumn == null || anchorRequested) return;
        if (!AnchorSupported)
        {
            SetStatus("Imagen y pose correctas. XR Anchor subsystem no esta activo; no se coloca la columna.");
            Debug.LogError("[StructuralAR] ARAnchorManager no tiene un subsistema activo.");
            return;
        }

        anchorRequested = true;
        var result = await anchorManager.TryAddAnchorAsync(pose);
        if (!result.status.IsSuccess() || result.value == null)
        {
            anchorRequested = false;
            SetStatus("El proveedor XR rechazo la creacion del anchor.");
            return;
        }

        structuralAnchor = result.value;
        structuralAnchor.name = "Structural AR Anchor";
        CreateColumn(structuralAnchor.transform);
        SetStatus("Imagen detectada. Pose obtenida y anchor creado para " + selectedColumn.elementTag + ".");
    }

    private void UpdateAnchorState()
    {
        if (!anchorRequested || structuralAnchor == null) return;

        bool tracked = structuralAnchor.trackingState == TrackingState.Tracking;
        if (columnObject != null) columnObject.SetActive(tracked);
        if (tracked && !columnCreated)
        {
            // The geometry is already built; this flag means that the full
            // image -> pose -> provider anchor chain has been accepted.
            columnCreated = true;
            elementSelected = true;
            resultsAvailable = TryReadForces(activeCombo, out _);
            SetColumnColor(true);
            SetStatus("Cadena AR activa: imagen -> pose -> anchor -> columna " + selectedColumn.elementTag + ".");
            Debug.Log("[StructuralAR][ACCEPTANCE] IMAGE -> POSE -> ANCHOR -> COLUMN -> " +
                      selectedColumn.elementTag + " -> OPENSEES " + activeCombo + " = " +
                      (resultsAvailable ? "OK" : "SIN DATOS"));
        }
    }

    private void CreateColumn(Transform parent)
    {
        if (!nodes.TryGetValue(selectedColumn.nodeI, out NodeData nodeI) ||
            !nodes.TryGetValue(selectedColumn.nodeJ, out NodeData nodeJ))
        {
            SetStatus("La columna existe, pero faltan sus nodos reales en el JSON.");
            return;
        }

        Vector3 originalI = ToUnity(nodeI);
        Vector3 originalJ = ToUnity(nodeJ);
        Vector3 structuralAxis = originalJ - originalI;
        if (structuralAxis.sqrMagnitude < 1e-10f)
        {
            SetStatus("La columna tiene longitud nula en el JSON.");
            return;
        }

        columnObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        columnObject.name = "AR Column " + selectedColumn.elementTag;
        columnObject.transform.SetParent(parent, false);

        Quaternion userRotation = Quaternion.Euler(rotationOffsetEuler);
        Vector3 scaledAxis = structuralAxis * uniformScale;
        columnObject.transform.localPosition = positionOffsetMeters + userRotation * (scaledAxis * 0.5f);
        columnObject.transform.localRotation = userRotation *
            Quaternion.FromToRotation(Vector3.up, structuralAxis.normalized);
        columnObject.transform.localScale = new Vector3(
            Mathf.Max(selectedColumn.width_m * uniformScale, 0.01f),
            scaledAxis.magnitude,
            Mathf.Max(selectedColumn.height_m * uniformScale, 0.01f));

        StructuralARElement identity = columnObject.AddComponent<StructuralARElement>();
        identity.elementId = selectedColumn.id;
        identity.elementTag = selectedColumn.elementTag;
        identity.elementType = selectedColumn.type;

        Renderer columnRenderer = columnObject.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        columnMaterial = new Material(shader);
        columnRenderer.sharedMaterial = columnMaterial;
        SetColumnColor(false);
        columnObject.SetActive(false);
    }

    private void HandleColumnSelection()
    {
        if (!columnCreated || columnObject == null || arCamera == null) return;
        if (!Input.GetMouseButtonDown(0)) return;

        Vector2 guiPoint = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        if (panelRect.Contains(guiPoint)) return;

        Ray ray = arCamera.ScreenPointToRay(Input.mousePosition);
        elementSelected = Physics.Raycast(ray, out RaycastHit hit) &&
                          hit.collider != null &&
                          hit.collider.GetComponent<StructuralARElement>() != null;
        SetColumnColor(elementSelected);
    }

    private void SetColumnColor(bool selected)
    {
        if (columnMaterial == null) return;
        columnMaterial.color = selected
            ? new Color(0.10f, 0.86f, 1f, 1f)
            : new Color(0.94f, 0.55f, 0.12f, 1f);
    }

    private bool TryReadForces(string combo, out FrameSectionForces forces)
    {
        forces = default;
        return selectedColumn != null && UnityData.TryGetSectionForces(selectedColumn.id, combo, 0.5f, out forces);
    }

    private void SetCombination(string combo)
    {
        activeCombo = combo;
        UnityData.ActiveCombo = combo;
        resultsAvailable = TryReadForces(combo, out _);
        Debug.Log("[StructuralAR] Caso activo " + combo + " para " + ActiveElementTag +
                  ": " + (resultsAvailable ? "datos OpenSees disponibles" : "sin datos"));
    }

    private void OnGUI()
    {
        panelRect.height = elementSelected ? 510f : 300f;
        GUI.Box(panelRect, GUIContent.none);
        GUILayout.BeginArea(new Rect(panelRect.x + 16f, panelRect.y + 12f,
            panelRect.width - 32f, panelRect.height - 24f));

        GUILayout.Label("STRUCTURAL AR — XR SIMULATION", HeaderStyle());
        GUILayout.Space(5f);
        DrawStep("IMAGEN DETECTADA", imageRecognized);
        DrawStep("POSE OBTENIDA", poseObtained);
        DrawStep("ANCHOR XR", structuralAnchor != null && structuralAnchor.trackingState == TrackingState.Tracking);
        DrawStep("COLUMNA VIRTUAL", columnCreated);
        DrawStep("ELEMENT TAG", selectedColumn != null && selectedColumn.elementTag == preferredElementTag);
        DrawStep("RESULTADO OPENSEES", resultsAvailable);
        GUILayout.Space(6f);
        GUILayout.Label(status, WrapStyle());

        if (columnCreated)
        {
            GUILayout.Space(6f);
            GUILayout.Label("Haz clic sobre la columna para consultar resultados.", WrapStyle());
        }

        if (elementSelected && selectedColumn != null)
        {
            GUILayout.Space(8f);
            GUILayout.Label(selectedColumn.elementTag + "  |  COLUMNA REAL", HeaderStyle());
            GUILayout.Label("ID interno: " + selectedColumn.id + "   Nodos: " +
                            selectedColumn.nodeI + " → " + selectedColumn.nodeJ);
            GUILayout.Label("Seccion: " + selectedColumn.seccion + "   Piso: " + selectedColumn.piso);
            GUILayout.BeginHorizontal();
            foreach (string combo in new[] { "C1", "C2", "C3" })
            {
                GUI.backgroundColor = combo == activeCombo ? new Color(0.1f, 0.8f, 1f) : Color.white;
                if (GUILayout.Button(combo, GUILayout.Height(30f))) SetCombination(combo);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            if (TryReadForces(activeCombo, out FrameSectionForces f))
            {
                GUILayout.Label("RESULTADOS REALES — " + activeCombo, HeaderStyle());
                GUILayout.Label(string.Format("N     {0,12:0.000} kN", f.N));
                GUILayout.Label(string.Format("Vy    {0,12:0.000} kN", f.Vy));
                GUILayout.Label(string.Format("Vz    {0,12:0.000} kN", f.Vz));
                GUILayout.Label(string.Format("T     {0,12:0.000} kN·m", f.T));
                GUILayout.Label(string.Format("My    {0,12:0.000} kN·m", f.My));
                GUILayout.Label(string.Format("Mz    {0,12:0.000} kN·m", f.Mz));
                GUILayout.Label("Valores en la seccion media, leidos del JSON exportado por OpenSees.", WrapStyle());
            }
            else
            {
                GUILayout.Label("Sin registro OpenSees para " + activeCombo + ".", ErrorStyle());
            }
        }

        GUILayout.EndArea();
    }

    private static void DrawStep(string label, bool ok)
    {
        Color previous = GUI.color;
        GUI.color = ok ? new Color(0.30f, 1f, 0.55f) : new Color(0.72f, 0.76f, 0.82f);
        GUILayout.Label((ok ? "[OK]  " : "[  ]  ") + label);
        GUI.color = previous;
    }

    private static GUIStyle HeaderStyle()
    {
        return new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 15 };
    }

    private static GUIStyle WrapStyle()
    {
        return new GUIStyle(GUI.skin.label) { wordWrap = true };
    }

    private static GUIStyle ErrorStyle()
    {
        GUIStyle style = WrapStyle();
        style.normal.textColor = new Color(1f, 0.4f, 0.35f);
        return style;
    }

    private static Vector3 ToUnity(NodeData node) => new Vector3(node.x, node.z, node.y);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) &&
        float.IsFinite(value.z) && float.IsFinite(value.w);

    private void SetStatus(string message)
    {
        status = message;
        if (statusText != null) statusText.text = message;
        Debug.Log("[StructuralAR] " + message);
    }
}

/// <summary>Identity attached to the AR mesh so ray selection preserves the source element.</summary>
public sealed class StructuralARElement : MonoBehaviour
{
    public int elementId;
    public string elementTag;
    public string elementType;
}
