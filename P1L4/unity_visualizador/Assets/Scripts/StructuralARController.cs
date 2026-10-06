using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Connects markerless AR placement to the existing structural JSON. It never creates
/// analytical values: geometry and forces always come from estructura_p1l4_unity.
/// </summary>
public sealed partial class StructuralARController : MonoBehaviour
{
    [Header("Project data")]
    public TextAsset structureJson;
    public string preferredElementTag = "E1_72";

    [Header("AR tracking")]
    public ARRaycastManager raycastManager;
    public ARAnchorManager anchorManager;
    public Camera arCamera;
    public bool useFreePlacement = true;
    [Min(0.3f)] public float fallbackDistanceMeters = 3f;

    [Header("Element relative to the selected camera point")]
    [Min(0.001f)] public float uniformScale = 0.2f;
    public Vector3 positionOffsetMeters = Vector3.zero;
    public Vector3 rotationOffsetEuler = Vector3.zero;

    [Header("Structural diagram")]
    [Min(0.01f)] public float diagramHeightMeters = 0.12f;
    [Range(11, 101)] public int diagramSamples = 41;

    [Header("Optional legacy status label")]
    public Text statusText;

    private readonly Dictionary<int, NodeData> nodes = new Dictionary<int, NodeData>();
    private readonly List<ElementData> availableElements = new List<ElementData>();
    private static readonly List<ARRaycastHit> placementHits = new List<ARRaycastHit>();
    private StructureData data;
    private ElementData selectedElement;
    private ARAnchor structuralAnchor;
    private GameObject elementObject;
    private LineRenderer diagramLine;
    private LineRenderer diagramBaseline;
    private Material diagramMaterial;
    private Material baselineMaterial;
    private bool pointDetected;
    private bool poseObtained;
    private bool anchorRequested;
    private bool elementCreated;
    private bool elementSelected;
    private bool resultsAvailable;
    private string activeCombo = "C1";
    private string activeResult = "My";
    private string elementSearch = "E1_72";
    private string selectedType = "viga";
    private Vector2 selectionScroll;
    private bool placementInProgress;
    private bool approximatePlacement;
    private float diagramMinimum;
    private float diagramMaximum;
    private string status = "Elige un ID y apunta al nodo I para comenzar.";
    private Rect panelRect = new Rect(18f, 18f, 430f, 650f);
    private Vector2 panelScroll;
    private float uiScale = 1f;
    private bool menuExpanded = true;
    private enum CalibrationStage { Idle, FindI, FindJ, ChooseFace, ConfirmAnchor, Locked }
    private CalibrationStage calibrationStage;
    private Vector3 measuredI;
    private Vector3 measuredJ;
    private Vector3 candidateJ;
    private float memberLengthMeters;
    private float placedLengthMeters;
    private bool approximateI;
    private bool candidateDetected;
    private bool aimEstimated;
    private int faceQuarterTurns;
    private Vector3 faceOutward;
    private Quaternion calibratedRotation = Quaternion.identity;
    private GameObject guideI;
    private GameObject guideJ;
    private GameObject previewMember;
    private Material guideMaterial;
    private Material previewMaterial;
    private Material faceMaterial;
    private LineRenderer guideLine;
    private LineRenderer faceLine;
    private bool calibratedPlacement;
    private GameObject freeAimMarker;
    private Material freeAimMaterial;

    public string ActiveElementTag => selectedElement == null ? string.Empty : selectedElement.elementTag;
    public string ActiveCombination => activeCombo;
    public bool ImageRecognized => pointDetected;
    public bool PoseObtained => poseObtained;
    public bool AnchorSupported => anchorManager != null && anchorManager.subsystem != null && anchorManager.subsystem.running;
    public bool ElementCreated => elementCreated;
    public bool ResultsAvailable => resultsAvailable;

    private void Awake()
    {
        raycastManager = raycastManager != null
            ? raycastManager
            : FindAnyObjectByType<ARRaycastManager>();
        anchorManager = anchorManager != null
            ? anchorManager
            : FindAnyObjectByType<ARAnchorManager>();
        arCamera = arCamera != null ? arCamera : Camera.main;
        structureJson = structureJson != null
            ? structureJson
            : Resources.Load<TextAsset>("estructura_p1l4_unity");

        LoadStructure();
        CreateFreeAimMarker();
        SetStatus(data == null ? "ERROR: falta el JSON estructural." :
            "Elige un ID y apunta al nodo I para comenzar.");
    }

    private void Update()
    {
        UpdateFreeAimMarker();
        UpdateCalibrationGuide();
        UpdateAnchorState();
        HandleElementSelection();
        SaveCurrentPlacement();
        UpdateOtherPlacements();
    }

    private void OnDestroy()
    {
        ReleaseOtherPlacements();
        if (diagramMaterial != null) Destroy(diagramMaterial);
        if (baselineMaterial != null) Destroy(baselineMaterial);
        if (guideMaterial != null) Destroy(guideMaterial);
        if (previewMaterial != null) Destroy(previewMaterial);
        if (faceMaterial != null) Destroy(faceMaterial);
        if (freeAimMaterial != null) Destroy(freeAimMaterial);
        if (freeAimMarker != null) Destroy(freeAimMarker);
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

        availableElements.Clear();
        foreach (ElementData element in data.elements ?? new ElementData[0])
        {
            if (element != null && (element.type == "viga" || element.type == "columna") &&
                !string.IsNullOrEmpty(element.elementTag)) availableElements.Add(element);
        }
    }

    private ElementData FindElement(string query)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0) return null;
        foreach (ElementData element in availableElements)
            if (element.type == selectedType &&
                (string.Equals(element.elementTag, query, System.StringComparison.OrdinalIgnoreCase) ||
                 element.id.ToString() == query)) return element;
        return null;
    }

    private bool TryGetReticlePoint(out Vector3 position)
    {
        position = default;
        if (arCamera == null) return false;
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        // In XR Simulation the practice member has a real collider. Prefer its
        // surface over a simulated feature point that may belong to the floor.
        Ray ray = arCamera.ScreenPointToRay(center);
        RaycastHit[] hits = Physics.RaycastAll(ray, 30f);
        float nearest = float.PositiveInfinity;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.GetComponent<StructuralARElement>() != null ||
                hit.collider.gameObject.layer == 2 || hit.distance >= nearest) continue;
            nearest = hit.distance;
            position = hit.point;
        }
        if (nearest < float.PositiveInfinity && IsFinite(position)) return true;
        placementHits.Clear();
        if (raycastManager != null && raycastManager.Raycast(center, placementHits,
                TrackableType.FeaturePoint | TrackableType.PlaneWithinPolygon) && placementHits.Count > 0)
        {
            position = placementHits[0].pose.position;
            return IsFinite(position);
        }
        return false;
    }

    private bool TryGetPlacementPoint(out Vector3 position, out bool estimated)
    {
        if (!useFreePlacement && TryGetReticlePoint(out position))
        { estimated = false; return true; }
        estimated = true;
        position = default;
        if (arCamera == null) return false;
        Ray ray = arCamera.ScreenPointToRay(
            new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        position = ray.GetPoint(Mathf.Clamp(fallbackDistanceMeters, 0.3f, 20f));
        return IsFinite(position);
    }

    private void CreateFreeAimMarker()
    {
        Shader shader = Shader.Find("Custom/AlwaysOnTopLine");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) return;
        freeAimMaterial = new Material(shader) { color = new Color(0.1f, 0.88f, 1f) };
        freeAimMaterial.renderQueue = 5000;
        freeAimMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        freeAimMarker.name = "Punto I libre bajo la mira";
        freeAimMarker.layer = 2;
        freeAimMarker.transform.localScale = Vector3.one * 0.10f;
        freeAimMarker.GetComponent<Renderer>().sharedMaterial = freeAimMaterial;
        Destroy(freeAimMarker.GetComponent<Collider>());
    }

    private void UpdateFreeAimMarker()
    {
        if (freeAimMarker == null) return;
        Vector3 point = default;
        bool show = calibrationStage == CalibrationStage.FindI &&
            TryGetPlacementPoint(out point, out _);
        freeAimMarker.SetActive(show);
        if (show) freeAimMarker.transform.position = point;
    }

    private void BeginCalibration()
    {
        if (placementInProgress) return;
        ElementData requested = FindElement(elementSearch);
        if (requested == null)
        {
            SetStatus("ID no encontrado para " + selectedType + ". Usa un elementTag o ID interno del JSON.");
            return;
        }
        if (!nodes.TryGetValue(requested.nodeI, out NodeData nodeI) ||
            !nodes.TryGetValue(requested.nodeJ, out NodeData nodeJ))
        {
            SetStatus("El elemento no tiene ambos nodos en el JSON.");
            return;
        }
        float length = Vector3.Distance(ToUnity(nodeI), ToUnity(nodeJ));
        if (length < 0.01f ||
            !TryGetPlacementPoint(out Vector3 pointI, out bool estimatedI))
        {
            SetStatus("No se pudo ubicar I. Comprueba que la camara AR este activa.");
            return;
        }
        DetachCurrentPlacement();
        ClearCalibrationVisuals();
        selectedElement = requested;
        measuredI = pointI;
        approximateI = estimatedI;
        memberLengthMeters = length;
        placedLengthMeters = 0f;
        pointDetected = true;
        poseObtained = true;
        calibratedPlacement = false;
        calibrationStage = CalibrationStage.FindJ;
        CreateCalibrationVisuals();
        UpdateCalibrationGuide();
        SetStatus("Nodo I fijado" + (estimatedI ? " con profundidad libre" : " sobre superficie") +
            ". Apunta al lugar de J y pulsa FIJAR J. No necesitas alineacion verde.");
    }

    private void UpdateCalibrationGuide()
    {
        if (calibrationStage != CalibrationStage.FindJ || selectedElement == null) return;
        bool available = TryGetPlacementPoint(out candidateJ, out aimEstimated);
        candidateDetected = available && !aimEstimated;
        if (!available) return;
        if (guideJ != null) guideJ.transform.position = candidateJ;
        if (guideLine != null)
        {
            guideLine.SetPosition(0, measuredI);
            guideLine.SetPosition(1, candidateJ);
            Color color = new Color(0.1f, 0.88f, 1f);
            guideLine.startColor = guideLine.endColor = color;
            if (guideMaterial != null) guideMaterial.color = color;
        }
    }

    private void CaptureJ()
    {
        if (calibrationStage != CalibrationStage.FindJ) return;
        UpdateCalibrationGuide();
        float span = Vector3.Distance(measuredI, candidateJ);
        if (!IsFinite(candidateJ) || span < 0.05f)
        { SetStatus("J esta demasiado cerca de I. Apunta al otro extremo y reintenta."); return; }
        measuredJ = candidateJ;
        placedLengthMeters = span;
        approximatePlacement = approximateI || aimEstimated;
        calibrationStage = CalibrationStage.ChooseFace;
        faceQuarterTurns = 0;
        Vector3 axis = (measuredJ - measuredI).normalized;
        faceOutward = Vector3.ProjectOnPlane(arCamera.transform.position -
            (measuredI + measuredJ) * 0.5f, axis).normalized;
        if (faceOutward.sqrMagnitude < 1e-6f) faceOutward = Vector3.forward;
        UpdateFacePreview();
        SetStatus("J fijado libremente donde lo marcaste. Tramo visual " +
            placedLengthMeters.ToString("0.00") + " m; largo del modelo " +
            memberLengthMeters.ToString("0.00") + " m. Revisa el contorno antes de anclar.");
    }

    private void CaptureFace()
    {
        if (calibrationStage != CalibrationStage.ChooseFace) return;
        if (!TryGetPlacementPoint(out Vector3 point, out _))
        {
            SetStatus("No se pudo ubicar la mira. Usa GIRAR CARA para alinearla visualmente.");
            return;
        }
        Vector3 axis = (measuredJ - measuredI).normalized;
        Vector3 fromAxis = Vector3.ProjectOnPlane(point -
            (measuredI + measuredJ) * 0.5f, axis);
        if (fromAxis.sqrMagnitude > 0.0025f) faceOutward = fromAxis.normalized;
        else faceOutward = Vector3.ProjectOnPlane(arCamera.transform.position - point, axis).normalized;
        faceQuarterTurns = 0;
        UpdateFacePreview();
        SetStatus("Cara capturada. Revisa el contorno; si no calza, gira 90 grados.");
    }

    private void AdjustJAgain()
    {
        if (calibrationStage != CalibrationStage.ChooseFace) return;
        if (previewMember != null) Destroy(previewMember);
        previewMember = null;
        if (previewMaterial != null) Destroy(previewMaterial);
        previewMaterial = null;
        if (faceLine != null) faceLine.gameObject.SetActive(false);
        calibratedPlacement = false;
        approximatePlacement = false;
        calibrationStage = CalibrationStage.FindJ;
        UpdateCalibrationGuide();
        SetStatus("Nodo I conservado. Apunta en otra direccion y vuelve a fijar J.");
    }

    private void RotateFace()
    {
        if (calibrationStage != CalibrationStage.ChooseFace) return;
        faceQuarterTurns = (faceQuarterTurns + 1) % 4;
        UpdateFacePreview();
    }

    private void ConfirmCalibration()
    {
        if (calibrationStage != CalibrationStage.ConfirmAnchor || !AnchorSupported) return;
        uniformScale = 1f;
        positionOffsetMeters = Vector3.zero;
        rotationOffsetEuler = Vector3.zero;
        calibratedPlacement = true;
        CreateAnchor(new Pose((measuredI + measuredJ) * 0.5f, calibratedRotation));
    }

    private void ResetCalibration()
    {
        ClearCalibrationVisuals();
        CancelGuidedPlacement();
        if (currentPlacement == null)
        {
            pointDetected = poseObtained = calibratedPlacement = false;
            SetStatus("Colocacion cancelada. Elige un elemento.");
        }
    }

    private void CreateCalibrationVisuals()
    {
        Shader lineShader = Shader.Find("Custom/AlwaysOnTopLine");
        if (lineShader == null) lineShader = Shader.Find("Sprites/Default");
        if (lineShader == null) return;
        guideMaterial = new Material(lineShader) { color = new Color(0.1f, 0.88f, 1f) };
        guideMaterial.renderQueue = 5000;
        faceMaterial = new Material(lineShader) { color = new Color(1f, 0.35f, 0.78f) };
        faceMaterial.renderQueue = 5001;

        guideI = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        guideI.name = "Nodo I medido";
        guideI.layer = 2;
        guideI.transform.position = measuredI;
        guideI.transform.localScale = Vector3.one * 0.09f;
        guideI.GetComponent<Renderer>().sharedMaterial = guideMaterial;
        Destroy(guideI.GetComponent<Collider>());

        guideJ = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        guideJ.name = "Nodo J elegido con la mira";
        guideJ.layer = 2;
        guideJ.transform.localScale = Vector3.one * 0.12f;
        guideJ.GetComponent<Renderer>().sharedMaterial = guideMaterial;
        Destroy(guideJ.GetComponent<Collider>());

        GameObject guideObject = new GameObject("Guia entre nodos I y J marcados");
        guideLine = guideObject.AddComponent<LineRenderer>();
        ConfigureLine(guideLine, guideMaterial, 0.02f);
        guideLine.useWorldSpace = true;
        guideLine.positionCount = 2;

        GameObject faceObject = new GameObject("Cara visible elegida");
        faceLine = faceObject.AddComponent<LineRenderer>();
        ConfigureLine(faceLine, faceMaterial, 0.03f);
        faceLine.useWorldSpace = true;
        faceLine.positionCount = 2;
        faceObject.SetActive(false);
    }

    private void UpdateFacePreview()
    {
        if (selectedElement == null ||
            !nodes.TryGetValue(selectedElement.nodeI, out NodeData nodeI) ||
            !nodes.TryGetValue(selectedElement.nodeJ, out NodeData nodeJ) ||
            !UnityData.TryGetFrameGeometry(selectedElement.id, out FrameGeometry frame)) return;
        Vector3 modelAxis = (ToUnity(nodeJ) - ToUnity(nodeI)).normalized;
        Vector3 measuredAxis = (measuredJ - measuredI).normalized;
        Vector3 sourceFace = UnityData.AxisToUnity(frame.Z).normalized;
        Quaternion axisAlignment = Quaternion.FromToRotation(modelAxis, measuredAxis);
        Vector3 currentFace = Vector3.ProjectOnPlane(axisAlignment * sourceFace, measuredAxis).normalized;
        Vector3 targetFace = Vector3.ProjectOnPlane(faceOutward, measuredAxis).normalized;
        if (targetFace.sqrMagnitude < 1e-6f) targetFace = currentFace;
        float roll = Vector3.SignedAngle(currentFace, targetFace, measuredAxis) + 90f * faceQuarterTurns;
        calibratedRotation = Quaternion.AngleAxis(roll, measuredAxis) * axisAlignment;

        if (previewMember == null)
        {
            previewMember = GameObject.CreatePrimitive(PrimitiveType.Cube);
            previewMember.name = "Contorno previo del elemento";
            previewMember.layer = 2;
            Destroy(previewMember.GetComponent<Collider>());
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                previewMaterial = new Material(shader) { color = new Color(0.1f, 0.9f, 1f, 0.25f) };
                previewMaterial.renderQueue = 4000;
                previewMember.GetComponent<Renderer>().sharedMaterial = previewMaterial;
            }
        }
        previewMember.transform.position = (measuredI + measuredJ) * 0.5f;
        previewMember.transform.rotation = calibratedRotation * ModelMemberRotation(modelAxis);
        previewMember.transform.localScale = new Vector3(
            Mathf.Max(selectedElement.height_m, 0.01f), placedLengthMeters,
            Mathf.Max(selectedElement.width_m, 0.01f));
        if (faceLine != null)
        {
            faceLine.gameObject.SetActive(true);
            Vector3 faceNormal = calibratedRotation * sourceFace;
            Vector3 center = previewMember.transform.position;
            faceLine.SetPosition(0, center);
            faceLine.SetPosition(1, center + faceNormal *
                (Mathf.Max(selectedElement.width_m, selectedElement.height_m) * 0.5f + 0.25f));
        }
    }

    private void ClearCalibrationVisuals()
    {
        if (guideI != null) Destroy(guideI);
        if (guideJ != null) Destroy(guideJ);
        if (previewMember != null) Destroy(previewMember);
        if (guideLine != null) Destroy(guideLine.gameObject);
        if (faceLine != null) Destroy(faceLine.gameObject);
        guideI = guideJ = previewMember = null;
        guideLine = faceLine = null;
        if (guideMaterial != null) Destroy(guideMaterial);
        if (previewMaterial != null) Destroy(previewMaterial);
        if (faceMaterial != null) Destroy(faceMaterial);
        guideMaterial = previewMaterial = faceMaterial = null;
    }

    private async void CreateAnchor(Pose pose)
    {
        if (selectedElement == null || placementInProgress) return;
        if (!AnchorSupported)
        {
            SetStatus("El subsistema AR Anchor no esta activo; no se coloca el elemento.");
            Debug.LogError("[StructuralAR] ARAnchorManager no tiene un subsistema activo.");
            return;
        }

        placementInProgress = true;
        int requestedId = selectedElement.id;
        int requestRevision = placementRevision;
        anchorRequested = true;
        var result = await anchorManager.TryAddAnchorAsync(pose);
        placementInProgress = false;
        if (!result.status.IsSuccess() || result.value == null)
        {
            anchorRequested = false;
            SetStatus("El proveedor XR rechazo la creacion del anchor.");
            return;
        }

        if (this == null || requestRevision != placementRevision || selectedElement == null || selectedElement.id != requestedId)
        {
            Destroy(result.value.gameObject);
            return;
        }
        structuralAnchor = result.value;
        structuralAnchor.name = "Structural AR Anchor";
        GameObject visualRoot = new GameObject("Visual " + selectedElement.elementTag);
        visualRoot.transform.SetParent(structuralAnchor.transform, false);
        CreateElement(visualRoot.transform);
        RegisterCurrentPlacement(visualRoot);
        ClearCalibrationVisuals();
        calibrationStage = CalibrationStage.Locked;
        SetStatus("Calibracion I-J + cara fijada para " + selectedElement.elementTag +
            ". Tramo visual " + placedLengthMeters.ToString("0.00") +
            " m; modelo OpenSees " + memberLengthMeters.ToString("0.00") + " m.");
    }

    private void ClearPlacement()
    {
        if (structuralAnchor != null) Destroy(structuralAnchor.gameObject);
        structuralAnchor = null;
        elementObject = null;
        diagramLine = null;
        diagramBaseline = null;
        elementCreated = false;
        elementSelected = false;
        resultsAvailable = false;
        anchorRequested = false;
        if (diagramMaterial != null) Destroy(diagramMaterial);
        if (baselineMaterial != null) Destroy(baselineMaterial);
    }

    private void UpdateAnchorState()
    {
        if (!anchorRequested || structuralAnchor == null) return;

        bool tracked = structuralAnchor.trackingState == TrackingState.Tracking;
        if (currentPlacement != null && currentPlacement.Root != null) currentPlacement.Root.SetActive(tracked);
        if (elementObject != null) elementObject.SetActive(tracked);
        if (diagramLine != null) diagramLine.gameObject.SetActive(tracked);
        if (diagramBaseline != null) diagramBaseline.gameObject.SetActive(tracked);
        if (tracked && !elementCreated)
        {
            // The geometry is already built; this flag means that the full
            // image -> pose -> provider anchor chain has been accepted.
            elementCreated = true;
            elementSelected = true;
            resultsAvailable = TryReadForces(activeCombo, out _);
            UpdateDiagram();
            SetStatus("Punto elegido -> anchor -> " + selectedElement.elementTag + " -> OpenSees.");
            Debug.Log("[StructuralAR][ACCEPTANCE] CAMERA POINT -> ID -> ANCHOR -> MEMBER -> " +
                      selectedElement.elementTag + " -> OPENSEES " + activeCombo + " = " +
                      (resultsAvailable ? "OK" : "SIN DATOS"));
        }
    }

    private void CreateElement(Transform parent)
    {
        if (!nodes.TryGetValue(selectedElement.nodeI, out NodeData nodeI) ||
            !nodes.TryGetValue(selectedElement.nodeJ, out NodeData nodeJ))
        {
            SetStatus("El elemento existe, pero faltan sus nodos reales en el JSON.");
            return;
        }

        Vector3 originalI = ToUnity(nodeI);
        Vector3 originalJ = ToUnity(nodeJ);
        Vector3 structuralAxis = originalJ - originalI;
        if (structuralAxis.sqrMagnitude < 1e-10f)
        {
            SetStatus("El elemento tiene longitud nula en el JSON.");
            return;
        }

        elementObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        elementObject.name = "AR Member " + selectedElement.elementTag;
        elementObject.transform.SetParent(parent, false);

        ApplyElementTransform();

        StructuralARElement identity = elementObject.AddComponent<StructuralARElement>();
        identity.elementId = selectedElement.id;
        identity.elementTag = selectedElement.elementTag;
        identity.elementType = selectedElement.type;

        Renderer elementRenderer = elementObject.GetComponent<Renderer>();
        // Retain the collider and identity for touch selection, without covering
        // the physical member with an opaque virtual volume.
        elementRenderer.enabled = false;
        CreateDiagramRenderers(parent);
        elementObject.SetActive(false);
    }

    private void HandleElementSelection()
    {
        if (calibrationStage != CalibrationStage.Locked || placementInProgress || arCamera == null) return;
        if (!Input.GetMouseButtonDown(0)) return;

        Vector2 guiPoint = new Vector2(Input.mousePosition.x / uiScale,
            (Screen.height - Input.mousePosition.y) / uiScale);
        if (panelRect.Contains(guiPoint)) return;

        Ray ray = arCamera.ScreenPointToRay(Input.mousePosition);
        PlacementRecord hitRecord = null;
        float closest = float.PositiveInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(ray))
        {
            PlacementRecord record = placements.Find(item => item.Member != null && hit.collider != null && item.Member == hit.collider.gameObject);
            if (record != null && hit.distance < closest)
            {
                closest = hit.distance;
                hitRecord = record;
            }
        }
        if (hitRecord != null) SelectPlacement(hitRecord);
    }

    private void ApplyElementTransform()
    {
        if (elementObject == null || selectedElement == null ||
            !nodes.TryGetValue(selectedElement.nodeI, out NodeData nodeI) ||
            !nodes.TryGetValue(selectedElement.nodeJ, out NodeData nodeJ)) return;
        Vector3 axis = ToUnity(nodeJ) - ToUnity(nodeI);
        if (axis.sqrMagnitude < 1e-10f) return;
        elementObject.transform.localPosition = positionOffsetMeters;
        elementObject.transform.localRotation = Quaternion.Euler(rotationOffsetEuler) *
            ModelMemberRotation(axis.normalized);
        elementObject.transform.localScale = new Vector3(
            Mathf.Max(selectedElement.height_m * uniformScale, 0.01f),
            calibratedPlacement ? placedLengthMeters : axis.magnitude * uniformScale,
            Mathf.Max(selectedElement.width_m * uniformScale, 0.01f));
        UpdateDiagram();
    }

    private Quaternion ModelMemberRotation(Vector3 axis)
    {
        if (selectedElement != null &&
            UnityData.TryGetFrameGeometry(selectedElement.id, out FrameGeometry frame))
        {
            Vector3 face = Vector3.ProjectOnPlane(UnityData.AxisToUnity(frame.Z), axis);
            if (face.sqrMagnitude > 1e-6f) return Quaternion.LookRotation(face.normalized, axis);
        }
        return Quaternion.FromToRotation(Vector3.up, axis);
    }

    private void AdjustScale(float delta)
    {
        uniformScale = Mathf.Clamp(uniformScale + delta, 0.04f, 1.5f);
        ApplyElementTransform();
    }

    private void RotateElement(float degrees)
    {
        rotationOffsetEuler.y += degrees;
        ApplyElementTransform();
    }

    private void CreateDiagramRenderers(Transform parent)
    {
        Shader shader = Shader.Find("Custom/AlwaysOnTopLine");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError("[StructuralAR] No hay shader disponible para dibujar diagramas.");
            return;
        }

        diagramMaterial = new Material(shader);
        baselineMaterial = new Material(shader) { color = new Color(0.65f, 0.72f, 0.78f, 0.9f) };
        diagramMaterial.renderQueue = 5000;
        baselineMaterial.renderQueue = 4999;

        GameObject baselineObject = new GameObject("AR Diagram Baseline " + selectedElement.elementTag);
        baselineObject.transform.SetParent(parent, false);
        diagramBaseline = baselineObject.AddComponent<LineRenderer>();
        ConfigureLine(diagramBaseline, baselineMaterial, 0.008f);
        diagramBaseline.positionCount = 2;

        GameObject diagramObject = new GameObject("AR Diagram " + selectedElement.elementTag);
        diagramObject.transform.SetParent(parent, false);
        diagramLine = diagramObject.AddComponent<LineRenderer>();
        ConfigureLine(diagramLine, diagramMaterial, 0.014f);

        baselineObject.SetActive(false);
        diagramObject.SetActive(false);
    }

    private static void ConfigureLine(LineRenderer line, Material material, float width)
    {
        line.useWorldSpace = false;
        line.sharedMaterial = material;
        line.startWidth = width;
        line.endWidth = width;
        line.numCornerVertices = 4;
        line.numCapVertices = 4;
        line.alignment = LineAlignment.View;
    }

    private void SetActiveResult(string component)
    {
        activeResult = component;
        UpdateDiagram();
    }

    private void UpdateDiagram()
    {
        if (diagramLine == null || diagramBaseline == null || selectedElement == null ||
            !nodes.TryGetValue(selectedElement.nodeI, out NodeData nodeI) ||
            !nodes.TryGetValue(selectedElement.nodeJ, out NodeData nodeJ))
            return;

        Vector3 structuralAxis = ToUnity(nodeJ) - ToUnity(nodeI);
        Quaternion userRotation = Quaternion.Euler(rotationOffsetEuler);
        float visualLength = calibratedPlacement ? placedLengthMeters :
            structuralAxis.magnitude * uniformScale;
        Vector3 displayedAxis = userRotation * structuralAxis.normalized * visualLength;
        Transform diagramParent = diagramLine.transform.parent;
        Vector3 visibleSide = PositiveDiagramDirection(diagramParent, displayedAxis);
        // The baseline follows the actual I-J axis; no solid mesh needs clearance.
        Vector3 baselineStart = positionOffsetMeters - displayedAxis * 0.5f;

        diagramBaseline.SetPosition(0, baselineStart);
        diagramBaseline.SetPosition(1, baselineStart + displayedAxis);

        int count = Mathf.Clamp(diagramSamples, 11, 101);
        float[] values = new float[count];
        diagramMinimum = float.PositiveInfinity;
        diagramMaximum = float.NegativeInfinity;
        bool valid = true;
        for (int index = 0; index < count; index++)
        {
            float t = index / (float)(count - 1);
            if (!TryDisplayedSection(t, out FrameSectionForces section))
            {
                valid = false;
                break;
            }
            values[index] = ComponentValue(section, activeResult);
            diagramMinimum = Mathf.Min(diagramMinimum, values[index]);
            diagramMaximum = Mathf.Max(diagramMaximum, values[index]);
        }

        resultsAvailable = valid;
        diagramLine.enabled = valid;
        diagramBaseline.enabled = valid;
        if (!valid) return;

        float maximumAbsolute = Mathf.Max(Mathf.Abs(diagramMinimum), Mathf.Abs(diagramMaximum));
        float visualDenominator = Mathf.Max(maximumAbsolute, 1e-6f);
        diagramLine.positionCount = count;
        for (int index = 0; index < count; index++)
        {
            float t = index / (float)(count - 1);
            float visualValue = values[index] / visualDenominator *
                Mathf.Max(diagramHeightMeters, displayedAxis.magnitude * 0.12f);
            diagramLine.SetPosition(index, baselineStart + displayedAxis * t + visibleSide * visualValue);
        }

        Color color = DiagramColor(activeResult);
        diagramMaterial.color = color;
        diagramLine.startColor = color;
        diagramLine.endColor = color;
        SaveCurrentPlacement();
        UpdateComparisonDiagrams();
    }

    private Vector3 PositiveDiagramDirection(Transform parent, Vector3 axis)
    {
        Vector3 direction = axis.normalized;
        if (activeResult == "My" || activeResult == "Mz")
        {
            // Positive bending is drawn below the beam, independently of I/J,
            // camera position and face roll. Numeric OpenSees signs stay intact.
            Vector3 down = parent.InverseTransformDirection(Vector3.down);
            Vector3 side = Vector3.ProjectOnPlane(down, direction);
            if (side.sqrMagnitude > 1e-4f) return side.normalized;
            // A vertical member has no downward direction perpendicular to its
            // axis. Use its fixed local face instead of a camera-dependent side.
            Vector3 face = elementObject != null ? elementObject.transform.localRotation * Vector3.right : Vector3.right;
            side = Vector3.ProjectOnPlane(face, direction);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.ProjectOnPlane(Vector3.forward, direction);
            return side.normalized;
        }
        Vector3 view = arCamera != null
            ? parent.InverseTransformDirection(arCamera.transform.position - parent.position).normalized
            : Vector3.back;
        Vector3 visible = Vector3.Cross(direction, view);
        if (visible.sqrMagnitude < 1e-6f) visible = Vector3.Cross(direction, Vector3.up);
        if (visible.sqrMagnitude < 1e-6f) visible = Vector3.right;
        return visible.normalized;
    }

    private static float ComponentValue(FrameSectionForces forces, string component)
    {
        switch (component)
        {
            case "N": return forces.N;
            case "Vy": return forces.Vy;
            case "Vz": return forces.Vz;
            case "My": return forces.My;
            default: return forces.Mz;
        }
    }

    private static Color DiagramColor(string component)
    {
        switch (component)
        {
            case "N": return new Color(1f, 0.78f, 0.16f);
            case "Vy": return new Color(0.25f, 0.95f, 0.55f);
            case "Vz": return new Color(0.55f, 1f, 0.35f);
            case "My": return new Color(0.95f, 0.30f, 0.90f);
            default: return new Color(1f, 0.42f, 0.18f);
        }
    }

    private bool TryReadForces(string combo, out FrameSectionForces forces)
    {
        forces = default;
        return selectedElement != null && UnityData.TryGetSectionForces(selectedElement.id, combo, 0.5f, out forces);
    }

    private void SetCombination(string combo)
    {
        activeCombo = combo;
        UnityData.ActiveCombo = combo;
        resultsAvailable = TryReadForces(combo, out _);
        UpdateDiagram();
        Debug.Log("[StructuralAR] Caso activo " + combo + " para " + ActiveElementTag +
                  ": " + (resultsAvailable ? "datos OpenSees disponibles" : "sin datos"));
    }

    private void OnGUI() => DrawGuidedInterface();

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
