using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[ExecuteAlways]
public class DiagramController : MonoBehaviour
{
    private const string AuditElementTag = "B3003_V60/80";
    private const string AuditCombo = "C1";

    private enum DiagramMode
    {
        None,
        Axial,
        Shear,
        Moment,
        Deformed,
        DeformedReal
    }

    public float diagramScale = 1.3f;
    public float axialMultiplier = 0.9f;
    public float shearMultiplier = 1.0f;
    public float momentMultiplier = 1.2f;
    public float diagramBaseOffset = 0.06f;
    [Tooltip("Escala exclusivamente visual de la deformada. No modifica los desplazamientos de OpenSees.")]
    public float deformedMultiplier = 50f;
    [Tooltip("Segundos desde la forma original hasta la deformada maxima.")]
    public float deformationHalfCycleSeconds = 1.75f;
    public bool animateDeformation = true;
    public bool showOriginalDeformationReference = true;
    public bool auditSingleElementDiagrams = false;
    public bool drawGlobalForceDiagrams = false;

    private readonly List<ElementSelectable> elements = new List<ElementSelectable>();
    private readonly List<ElementSelectable> structuralElements = new List<ElementSelectable>();
    public IReadOnlyList<ElementSelectable> StructuralElements => structuralElements;
    private readonly List<GameObject> diagramObjects = new List<GameObject>();
    private readonly List<GameObject> mobileDiagramObjects = new List<GameObject>();
    private DiagramMode currentMode = DiagramMode.None;
    private sealed class DeformedSegment
    {
        public int nodeI;
        public int nodeJ;
        public Vector3 originalI;
        public Vector3 originalJ;
        public LineRenderer originalLine;
        public LineRenderer deformedLine;
    }

    private readonly List<DeformedSegment> deformedSegments = new List<DeformedSegment>();
    private readonly Dictionary<Renderer, bool> hiddenOriginalRenderers = new Dictionary<Renderer, bool>();
    private float deformationAnimationTime;
    private float deformationAnimationFactor;
    private float maximumRealDisplacement;
    private int maximumDisplacementNode;
    private Dictionary<string, float> currentMaxByBuilding = new Dictionary<string, float>();
    private GUIStyle tableBoxStyle;
    private GUIStyle tableTextStyle;
    private GUIStyle tableTitleStyle;

    public void Initialize(List<ElementSelectable> selectables)
    {
        elements.Clear();
        elements.AddRange(selectables);
        structuralElements.Clear();
        foreach (ElementSelectable e in elements)
        {
            if (e.data != null)
            {
                structuralElements.Add(e);
            }
        }

        ShowDiagram(DiagramMode.None);
        Debug.Log($"[DiagramController] listo con {structuralElements.Count} elementos con datos (todos los edificios)");
    }

    private DiagramMode modeToRedraw = DiagramMode.None;

    public void SetResultMode(string modeName)
    {
        if (modeName == "None" || modeName == "Exploration") ShowDiagram(DiagramMode.None);
        else if (modeName == "Axial") ShowDiagram(DiagramMode.Axial);
        else if (modeName == "Corte") ShowDiagram(DiagramMode.Shear);
        else if (modeName == "Momento") ShowDiagram(DiagramMode.Moment);
        else if (modeName == "Deformada") ShowDiagram(DiagramMode.Deformed);
        else if (modeName == "Deformada real") ShowDiagram(DiagramMode.DeformedReal);
        else ShowDiagram(DiagramMode.None);
    }

    public string CurrentResultName()
    {
        if (currentMode == DiagramMode.Shear) return "Corte";
        if (currentMode == DiagramMode.Moment) return "Momento";
        if (currentMode == DiagramMode.Deformed) return "Deformada";
        if (currentMode == DiagramMode.DeformedReal) return "Deformada real 1x";
        return currentMode.ToString();
    }

    public void Refresh()
    {
        if (modeToRedraw != DiagramMode.None)
        {
            ShowDiagram(modeToRedraw);
        }
    }

    private void Update()
    {
        if (!Application.isPlaying) return;

        if (PressedKey(KeyCode.Alpha0)) ShowDiagram(DiagramMode.None);
        if (PressedKey(KeyCode.Alpha1)) ShowDiagram(DiagramMode.Axial);
        if (PressedKey(KeyCode.Alpha2)) ShowDiagram(DiagramMode.Shear);
        if (PressedKey(KeyCode.Alpha3)) ShowDiagram(DiagramMode.Moment);
        if (PressedKey(KeyCode.Alpha5)) ShowDiagram(DiagramMode.Deformed);
        if (PressedKey(KeyCode.Alpha6)) ShowDiagram(DiagramMode.DeformedReal);

        if (currentMode == DiagramMode.Deformed)
        {
            UpdateDeformationAnimation();
        }
    }

    private bool PressedKey(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (key == KeyCode.Alpha0) return keyboard.digit0Key.wasPressedThisFrame;
            if (key == KeyCode.Alpha1) return keyboard.digit1Key.wasPressedThisFrame;
            if (key == KeyCode.Alpha2) return keyboard.digit2Key.wasPressedThisFrame;
            if (key == KeyCode.Alpha3) return keyboard.digit3Key.wasPressedThisFrame;
            if (key == KeyCode.Alpha5) return keyboard.digit5Key.wasPressedThisFrame;
            if (key == KeyCode.Alpha6) return keyboard.digit6Key.wasPressedThisFrame;
        }
#endif
        return Input.GetKeyDown(key);
    }

    private void ShowDiagram(DiagramMode mode)
    {
        bool enteringDeformedMode = mode == DiagramMode.Deformed && currentMode != DiagramMode.Deformed;
        currentMode = mode;
        modeToRedraw = mode;
        ClearDiagram();

        if (auditSingleElementDiagrams && mode != DiagramMode.None &&
            mode != DiagramMode.Deformed && mode != DiagramMode.DeformedReal)
        {
            CreateAuditDiagramForSingleElement(mode);
            return;
        }

        if (mode == DiagramMode.None)
        {
            return;
        }

        if (mode == DiagramMode.Deformed || mode == DiagramMode.DeformedReal)
        {
            if (enteringDeformedMode) deformationAnimationTime = 0f;
            CreateDeformedDiagram();
            float activeScale = mode == DiagramMode.DeformedReal ? 1f : deformedMultiplier;
            Debug.Log($"[DiagramController] modo {CurrentResultName()} activado (escala visual {activeScale:0.#}x; desplazamientos numericos reales)");
            return;
        }

        if (!drawGlobalForceDiagrams)
        {
            Debug.Log("[DiagramController] Diagramas globales desactivados. Usa el panel 'Diagrama seleccionado'.");
            return;
        }

        currentMaxByBuilding.Clear();
        currentMaxByBuilding = GetMaxValueByBuilding(mode);
        int created = 0;

        foreach (ElementSelectable element in structuralElements)
        {
            // Columnas y enlaces tambien tienen corte y momento (sismo): se dibujan todos.

            CreateElementDiagram(element, mode);
            created++;
        }

        Debug.Log($"[DiagramController] modo {mode}: {created} diagramas, max por edificio {FormatMaxByBuilding()}");
    }

    public void RefreshDiagramForElement(ElementSelectable element)
    {
        if (element == null || element.data == null)
        {
            return;
        }
        if (currentMode == DiagramMode.None || currentMode == DiagramMode.Deformed || currentMode == DiagramMode.DeformedReal)
        {
            return;
        }
        if (auditSingleElementDiagrams || !drawGlobalForceDiagrams)
        {
            return;
        }

        string prefix = $"Diagrama_{currentMode}_E{element.data.id}";
        for (int i = diagramObjects.Count - 1; i >= 0; i--)
        {
            if (diagramObjects[i] == null)
            {
                diagramObjects.RemoveAt(i);
                continue;
            }
            GameObject go = diagramObjects[i];
            if (go.name == prefix || go.name.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                Destroy(go);
                diagramObjects.RemoveAt(i);
            }
        }

        string building = string.IsNullOrEmpty(element.data.sourceBuilding) ? "?" : element.data.sourceBuilding;
        currentMaxByBuilding[building] = ComputeBuildingMax(building, currentMode);
        CreateElementDiagram(element, currentMode);
    }

    public void ShowMobileComponent(string component, IList<ElementSelectable> targets)
    {
        ClearMobileComponentDiagrams();
        if (component == "Deformed")
        {
            ShowDiagram(DiagramMode.Deformed);
            return;
        }
        if (currentMode != DiagramMode.None) ShowDiagram(DiagramMode.None);
        if (targets == null || targets.Count == 0) return;

        float bound = 0.000001f;
        foreach (ElementSelectable element in targets)
            for (int i = 0; i <= 12; i++)
                if (TryMobileComponent(element, component, i / 12f, out float value))
                    bound = Mathf.Max(bound, Mathf.Abs(value));

        foreach (ElementSelectable element in targets)
            CreateMobileComponentDiagram(element, component, bound);
    }

    public void ClearMobileComponentDiagrams()
    {
        foreach (GameObject diagram in mobileDiagramObjects)
            if (diagram != null) Destroy(diagram);
        mobileDiagramObjects.Clear();
    }

    private void CreateMobileComponentDiagram(ElementSelectable element, string component, float bound)
    {
        if (element == null || element.data == null) return;
        const int segments = 12;
        var points = new Vector3[segments + 1];
        Vector3 axis = element.endPoint - element.startPoint;
        Vector3 direction = MobileDiagramDirection(element, component, axis);
        float drawSign = component == "My" || component == "Mz" ? -1f : 1f;
        bool found = false;
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector3 basePoint = Vector3.Lerp(element.startPoint, element.endPoint, t);
            float value = 0f;
            if (TryMobileComponent(element, component, t, out float current)) { value = current; found = true; }
            points[i] = basePoint + direction * (diagramBaseOffset + drawSign * value / bound * diagramScale);
        }
        if (!found) return;

        GameObject root = new GameObject($"Mobile_{component}_E{element.data.id}");
        root.transform.SetParent(transform); root.hideFlags = HideFlags.DontSave;
        LineRenderer line = root.AddComponent<LineRenderer>();
        line.useWorldSpace = true; line.positionCount = points.Length; line.SetPositions(points);
        line.startWidth = line.endWidth = .075f;
        line.material = CreateMaterial(new Color(.12f, .82f, 1f, .95f));
        mobileDiagramObjects.Add(root);
    }

    private bool TryMobileComponent(ElementSelectable element, string component, float t, out float value)
    {
        value = 0f;
        if (element == null || element.data == null ||
            !UnityData.TryGetSectionForces(element.data.id, UnityData.ActiveCombo, t, out FrameSectionForces forces)) return false;
        if (component == "N") value = forces.N;
        else if (component == "Vy") value = forces.Vy;
        else if (component == "Vz") value = forces.Vz;
        else if (component == "My") value = forces.My;
        else if (component == "Mz") value = forces.Mz;
        else return false;
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private Vector3 MobileDiagramDirection(ElementSelectable element, string component, Vector3 axis)
    {
        if (UnityData.TryGetFrameGeometry(element.data.id, out FrameGeometry frame))
        {
            Vector3 local = UnityData.AxisToUnity(component == "Vz" || component == "My" ? frame.Z : frame.Y).normalized;
            if (local.sqrMagnitude > .01f) return local;
        }
        return GetOffsetDirection(axis, component == "N" ? DiagramMode.Axial : component.StartsWith("V") ? DiagramMode.Shear : DiagramMode.Moment);
    }

    private float ComputeBuildingMax(string building, DiagramMode mode)
    {
        float max = 0.001f;
        foreach (ElementSelectable e in structuralElements)
        {
            if (e == null || e.data == null) continue;
            string eb = string.IsNullOrEmpty(e.data.sourceBuilding) ? "?" : e.data.sourceBuilding;
            if (eb != building) continue;
            int segments = 24;
            float length = (e.endPoint - e.startPoint).magnitude;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                max = Mathf.Max(max, Mathf.Abs(GetValue(e, mode, t, length)));
            }
        }
        return max;
    }

    private void CreateAuditDiagramForSingleElement(DiagramMode mode)
    {
        ElementSelectable element = FindAuditElement();
        if (element == null || element.data == null)
        {
            Debug.LogWarning($"[DiagramController] No se encontro elemento de auditoria {AuditElementTag}.");
            return;
        }

        InternalDiagramForces internalForces = ConvertOpenSeesEndForcesToInternalForces(element, AuditCombo, true);
        bool ok = CheckInternalDiagramEquilibrium(element, internalForces, true);
        if (!ok)
        {
            Debug.LogWarning($"WARNING: {AuditElementTag} falla chequeo de equilibrio. No usar este diagrama como validado.");
        }

        CreateAuditLocalAxes(element, internalForces);
        if (mode == DiagramMode.Axial)
        {
            CreateAuditComponentDiagram(element, internalForces, "N", new Color(1f, 0.15f, 0.15f), internalForces.localY, 0);
        }
        else if (mode == DiagramMode.Shear)
        {
            CreateAuditComponentDiagram(element, internalForces, "Vy", new Color(1f, 0.55f, 0f), internalForces.localY, 1);
            CreateAuditComponentDiagram(element, internalForces, "Vz", new Color(1f, 0.85f, 0.1f), internalForces.localZ, 2);
        }
        else if (mode == DiagramMode.Moment)
        {
            CreateAuditComponentDiagram(element, internalForces, "My", Color.magenta, internalForces.localY, 4);
            CreateAuditComponentDiagram(element, internalForces, "Mz", new Color(0.65f, 0.25f, 1f), internalForces.localZ, 5);
        }

        Debug.Log($"[DiagramController] AUDIT DEBUG: mostrando solo {AuditElementTag}, modo={mode}, equilibrio={(ok ? "PASS" : "FAIL")}");
    }

    private ElementSelectable FindAuditElement()
    {
        foreach (ElementSelectable e in structuralElements)
        {
            if (e == null || e.data == null) continue;
            if (e.data.elementTag == AuditElementTag || e.data.sourceId == AuditElementTag)
            {
                return e;
            }
        }
        return null;
    }

    private string FormatMaxByBuilding()
    {
        var parts = new List<string>();
        foreach (KeyValuePair<string, float> kv in currentMaxByBuilding)
        {
            parts.Add($"{kv.Key}={kv.Value:0.###}");
        }
        return parts.Count == 0 ? "-" : string.Join(", ", parts);
    }

    private void CreateDeformedDiagram()
    {
        string combo = UnityData.ActiveCombo;
        StructureData structure = UnityData.Structure;
        if (string.IsNullOrEmpty(combo) || UnityData.DisplacementsByCombo == null || structure == null)
        {
            Debug.LogWarning("[DiagramController] No hay desplazamientos para el combo activo.");
            return;
        }

        var nodePositions = new Dictionary<int, Vector3>();
        if (structure.nodes != null)
        {
            foreach (NodeData node in structure.nodes)
            {
                nodePositions[node.id] = new Vector3(node.x, node.z, node.y);
            }
        }

        maximumRealDisplacement = 0f;
        maximumDisplacementNode = 0;
        foreach (KeyValuePair<int, Vector3> node in nodePositions)
        {
            float magnitude = UnityData.GetNodeDisplacement(combo, node.Key).magnitude;
            if (magnitude > maximumRealDisplacement)
            {
                maximumRealDisplacement = magnitude;
                maximumDisplacementNode = node.Key;
            }
        }

        HideOriginalSolidStructure();
        int created = 0;
        if (structure.elements != null)
        {
            foreach (ElementData element in structure.elements)
            {
                if (!nodePositions.TryGetValue(element.nodeI, out Vector3 originalI) ||
                    !nodePositions.TryGetValue(element.nodeJ, out Vector3 originalJ)) continue;

                float width = element.type == "muro_eq" ? 0.18f :
                    element.type == "brazo_rigido" ? 0.08f : 0.13f;
                AddDeformedSegment(element.nodeI, element.nodeJ, originalI, originalJ, width, element.id);
                created++;
            }
        }

        float initialAnimation = currentMode == DiagramMode.Deformed && animateDeformation ? deformationAnimationFactor : 1f;
        UpdateDeformedSegmentPositions(initialAnimation);
        float activeScale = currentMode == DiagramMode.DeformedReal ? 1f : deformedMultiplier;
        Debug.Log($"[DiagramController] {CurrentResultName()} combo={combo}: {created} elementos del modelo completo; escala visual={activeScale:0.#}x; max real={maximumRealDisplacement * 1000f:0.###} mm nodo={maximumDisplacementNode}");
    }

    private void AddDeformedSegment(int nodeI, int nodeJ, Vector3 originalI, Vector3 originalJ, float width, int elementId)
    {
        LineRenderer original = CreateLine(originalI, originalJ, new Color(0.55f, 0.58f, 0.64f, 0.5f), 0.035f,
            $"Deformada_Ref_E{elementId}");
        LineRenderer deformed = CreateLine(originalI, originalJ, new Color(0.25f, 1f, 0.48f), width,
            $"Deformada_E{elementId}");
        deformedSegments.Add(new DeformedSegment
        {
            nodeI = nodeI,
            nodeJ = nodeJ,
            originalI = originalI,
            originalJ = originalJ,
            originalLine = original,
            deformedLine = deformed
        });
    }

    private LineRenderer CreateLine(Vector3 a, Vector3 b, Color color, float width, string name)
    {
        GameObject lineObject = new GameObject(name);
        lineObject.transform.SetParent(transform);
        lineObject.hideFlags = HideFlags.DontSave;
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.SetPosition(0, a);
        line.SetPosition(1, b);
        line.startWidth = width;
        line.endWidth = width;
        line.useWorldSpace = true;
        line.material = CreateMaterial(color);
        diagramObjects.Add(lineObject);
        return line;
    }

    private void UpdateDeformationAnimation()
    {
        if (animateDeformation)
        {
            float duration = Mathf.Max(0.2f, deformationHalfCycleSeconds);
            deformationAnimationTime += Time.unscaledDeltaTime;
            float linear = Mathf.PingPong(deformationAnimationTime / duration, 1f);
            deformationAnimationFactor = Mathf.SmoothStep(0f, 1f, linear);
        }
        else
        {
            deformationAnimationFactor = 1f;
        }
        UpdateDeformedSegmentPositions(deformationAnimationFactor);
    }

    private void UpdateDeformedSegmentPositions(float animationFactor)
    {
        string combo = UnityData.ActiveCombo;
        float visualScale = currentMode == DiagramMode.DeformedReal ? 1f : Mathf.Max(0f, deformedMultiplier);
        float displayFactor = visualScale * Mathf.Clamp01(animationFactor);
        foreach (DeformedSegment segment in deformedSegments)
        {
            if (segment.originalLine != null) segment.originalLine.enabled = showOriginalDeformationReference;
            if (segment.deformedLine == null) continue;
            Vector3 displacementI = UnityData.GetNodeDisplacement(combo, segment.nodeI);
            Vector3 displacementJ = UnityData.GetNodeDisplacement(combo, segment.nodeJ);
            segment.deformedLine.SetPosition(0, segment.originalI + displacementI * displayFactor);
            segment.deformedLine.SetPosition(1, segment.originalJ + displacementJ * displayFactor);
        }
    }

    private void HideOriginalSolidStructure()
    {
        foreach (ElementSelectable element in elements)
        {
            if (element == null) continue;
            Renderer renderer = element.GetComponent<Renderer>();
            if (renderer == null || hiddenOriginalRenderers.ContainsKey(renderer)) continue;
            hiddenOriginalRenderers[renderer] = renderer.enabled;
            renderer.enabled = false;
        }
    }

    private void RestoreOriginalSolidStructure()
    {
        foreach (KeyValuePair<Renderer, bool> item in hiddenOriginalRenderers)
        {
            if (item.Key != null) item.Key.enabled = item.Value;
        }
        hiddenOriginalRenderers.Clear();
    }

    private Dictionary<string, float> GetMaxValueByBuilding(DiagramMode mode)
    {
        var result = new Dictionary<string, float>();

        foreach (ElementSelectable element in structuralElements)
        {
            if (element.data == null) continue;
            // Columnas y enlaces tambien tienen corte y momento (sismo): se dibujan todos.

            string building = string.IsNullOrEmpty(element.data.sourceBuilding) ? "?" : element.data.sourceBuilding;
            if (!result.ContainsKey(building))
            {
                result[building] = 0.001f;
            }

            float buildingMax = result[building];
            int segments = 20;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float length = (element.endPoint - element.startPoint).magnitude;
                buildingMax = Mathf.Max(buildingMax, Mathf.Abs(GetValue(element, mode, t, length)));
            }
            result[building] = buildingMax;
        }

        return result;
    }

    private void CreateElementDiagram(ElementSelectable element, DiagramMode mode)
    {
        int segments = 12;
        Vector3[] points = new Vector3[segments + 1];
        Vector3 axis = element.endPoint - element.startPoint;
        Vector3 offsetDirection = GetDiagramDirection(element, mode, axis, out float drawSign);
        float length = axis.magnitude;

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector3 basePoint = Vector3.Lerp(element.startPoint, element.endPoint, t);
            float value = GetValue(element, mode, t, length);
            float maxValue = MaxForElement(element);
            points[i] = basePoint + offsetDirection * (diagramBaseOffset + drawSign * value / maxValue * ScaleFor(mode));
        }

        GameObject lineObject = new GameObject($"Diagrama_{mode}_E{element.data.id}");
        lineObject.transform.SetParent(transform);
        lineObject.hideFlags = HideFlags.DontSave;
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.startWidth = 0.08f;
        line.endWidth = 0.08f;
        line.useWorldSpace = true;
        line.material = CreateMaterial(GetColor(mode));
        diagramObjects.Add(lineObject);

        CreateLabel(points[0], GetValue(element, mode, 0f, length), UnitFor(mode), lineObject.transform);
        CreateLabel(points[segments / 2], GetValue(element, mode, 0.5f, length), UnitFor(mode), lineObject.transform);
        CreateLabel(points[segments], GetValue(element, mode, 1f, length), UnitFor(mode), lineObject.transform);
    }

    private struct InternalDiagramForces
    {
        public float[] localActions;
        public float length;
        public Vector3 localX;
        public Vector3 localY;
        public Vector3 localZ;
        public float Ni;
        public float VyI;
        public float VzI;
        public float Ti;
        public float MyI;
        public float MzI;
        public float Nj;
        public float VyJ;
        public float VzJ;
        public float Tj;
        public float MyJ;
        public float MzJ;
        public float qLocalY;
        public float qLocalZ;
    }

    // The selected panel uses the active combination, including the factor sliders.
    public bool TryGetSelectedDiagramSamples(ElementSelectable element, float[,] samples)
    {
        if (element == null || element.data == null ||
            (element.data.type != "viga" && element.data.type != "columna" && element.data.type != "enlace") ||
            samples == null || samples.GetLength(0) != 6 || samples.GetLength(1) < 2 ||
            (element.endPoint - element.startPoint).sqrMagnitude < 0.000001f)
            return false;

        float[] raw = UnityData.GetElementForces(UnityData.ActiveCombo, element.data.id);
        if (raw == null || raw.Length < 12) return false;
        foreach (float value in raw)
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;

        if (!UnityData.TryGetFrameGeometry(element.data.id, out var frame)) return false;
        for (int i = 0; i < samples.GetLength(1); i++)
        {
            float t = i / (float)(samples.GetLength(1) - 1);
            var f = FrameForces.Evaluate(raw, frame.Length, t);
            samples[0, i] = f.My;
            samples[1, i] = f.Mz;
            samples[2, i] = f.Vy;
            samples[3, i] = f.Vz;
            samples[4, i] = f.N;
            samples[5, i] = f.T;
        }
        return true;
    }

    private InternalDiagramForces ConvertOpenSeesEndForcesToInternalForces(ElementSelectable element, string combo, bool printLog, float[] activeForces = null)
    {
        ElementData data = element.data;
        float[] raw = activeForces ?? UnityData.GetElementForces(combo, data.id);
        if (raw == null || raw.Length < 12)
        {
            raw = UnityData.GetElementForces(combo, data.id);
        }
        if (raw == null || raw.Length < 12)
        {
            Debug.LogWarning($"[DiagramController] Sin fuerzas OpenSees para {data.elementTag} combo {combo}.");
            return new InternalDiagramForces();
        }

        if (!UnityData.TryGetFrameGeometry(data.id, out var frame)) return new InternalDiagramForces();
        float length = (float)frame.Length;
        Vector3 localX = UnityData.AxisToUnity(frame.X);
        Vector3 localY = UnityData.AxisToUnity(frame.Y);
        Vector3 localZ = UnityData.AxisToUnity(frame.Z);

        InternalDiagramForces f = new InternalDiagramForces();
        f.localActions = raw;
        f.length = length;
        f.localX = localX;
        f.localY = localY;
        f.localZ = localZ;

        // OpenSees entrega acciones nodales resistentes de extremo.
        // Para dibujar esfuerzos internos fisicos, el extremo j se compara con signo opuesto:
        // Vj_internal = -Vj_raw, Tj_internal = -Tj_raw, Mj_internal = -Mj_raw.
        // Axial: si OpenSees devuelve Ni=-N y Nj=+N, el axial interno constante es N=-Ni=Nj.
        f.Ni = -raw[0];
        f.VyI = raw[1];
        f.VzI = raw[2];
        f.Ti = raw[3];
        f.MyI = raw[4];
        f.MzI = raw[5];
        f.Nj = raw[6];
        f.VyJ = -raw[7];
        f.VzJ = -raw[8];
        f.Tj = -raw[9];
        f.MyJ = -raw[10];
        f.MzJ = -raw[11];
        f.qLocalY = (f.VyI - f.VyJ) / length;
        f.qLocalZ = (f.VzI - f.VzJ) / length;

        if (printLog)
        {
            Debug.Log(
                $"OPENSEES LOCAL END ACTIONS (global JSON transformed at load)\n" +
                $"Element: {data.elementTag}\n" +
                $"Combo: {combo}\n" +
                $"N_i={raw[0]:0.######}\nVy_i={raw[1]:0.######}\nVz_i={raw[2]:0.######}\nT_i={raw[3]:0.######}\nMy_i={raw[4]:0.######}\nMz_i={raw[5]:0.######}\n\n" +
                $"N_j={raw[6]:0.######}\nVy_j={raw[7]:0.######}\nVz_j={raw[8]:0.######}\nT_j={raw[9]:0.######}\nMy_j={raw[10]:0.######}\nMz_j={raw[11]:0.######}\n\n" +
                $"localX={FormatStructuralVector(localX)}\nlocalY={FormatStructuralVector(localY)}\nlocalZ={FormatStructuralVector(localZ)}\n" +
                $"length={length:0.######}\nq_local_y={f.qLocalY:0.######}\nq_local_z={f.qLocalZ:0.######}\n\n" +
                $"INTERNAL DIAGRAM VALUES\n" +
                $"N_i_internal={f.Ni:0.######}\nVy_i_internal={f.VyI:0.######}\nVz_i_internal={f.VzI:0.######}\nT_i_internal={f.Ti:0.######}\nMy_i_internal={f.MyI:0.######}\nMz_i_internal={f.MzI:0.######}\n\n" +
                $"N_j_internal={f.Nj:0.######}\nVy_j_internal={f.VyJ:0.######}\nVz_j_internal={f.VzJ:0.######}\nT_j_internal={f.Tj:0.######}\nMy_j_internal={f.MyJ:0.######}\nMz_j_internal={f.MzJ:0.######}");
        }

        return f;
    }

    private bool CheckInternalDiagramEquilibrium(ElementSelectable element, InternalDiagramForces f, bool printLog)
    {
        float tolForce = 0.05f;
        float tolMoment = 0.25f;
        float nError = Mathf.Abs(f.Ni - f.Nj);
        float vyEnd = f.VyI - f.qLocalY * f.length;
        float vzEnd = f.VzI - f.qLocalZ * f.length;
        float myEnd = f.MyI + f.VzI * f.length - 0.5f * f.qLocalZ * f.length * f.length;
        float mzEnd = f.MzI - f.VyI * f.length + 0.5f * f.qLocalY * f.length * f.length;

        bool axialOk = nError <= tolForce;
        bool shearYOk = Mathf.Abs(vyEnd - f.VyJ) <= tolForce;
        bool shearZOk = Mathf.Abs(vzEnd - f.VzJ) <= tolForce;
        bool momentYOk = Mathf.Abs(myEnd - f.MyJ) <= tolMoment;
        bool momentZOk = Mathf.Abs(mzEnd - f.MzJ) <= tolMoment;

        if (printLog)
        {
            Debug.Log(
                $"ELEMENT DIAGRAM CHECK\n" +
                $"Element: {element.data.elementTag}\n" +
                $"Length: {f.length:0.######}\n\n" +
                $"Axial equilibrium: {(axialOk ? "PASS" : "FAIL")} error={nError:0.######}\n" +
                $"Shear equilibrium Y: {(shearYOk ? "PASS" : "FAIL")} V(L)={vyEnd:0.######} target={f.VyJ:0.######}\n" +
                $"Shear equilibrium Z: {(shearZOk ? "PASS" : "FAIL")} V(L)={vzEnd:0.######} target={f.VzJ:0.######}\n" +
                $"Moment equilibrium Y: {(momentYOk ? "PASS" : "FAIL")} M(L)={myEnd:0.######} target={f.MyJ:0.######}\n" +
                $"Moment equilibrium Z: {(momentZOk ? "PASS" : "FAIL")} M(L)={mzEnd:0.######} target={f.MzJ:0.######}\n\n" +
                BuildPointTable(f));
        }

        return axialOk && shearYOk && shearZOk && momentYOk && momentZOk;
    }

    private string BuildPointTable(InternalDiagramForces f)
    {
        string text = "21 INTERNAL POINTS\nx,N,Vy,Vz,My,Mz\n";
        for (int i = 0; i <= 20; i++)
        {
            float x = f.length * i / 20f;
            text += $"{x:0.###},{EvaluateInternalValue(f, 0, x):0.###},{EvaluateInternalValue(f, 1, x):0.###},{EvaluateInternalValue(f, 2, x):0.###},{EvaluateInternalValue(f, 4, x):0.###},{EvaluateInternalValue(f, 5, x):0.###}\n";
        }
        return text;
    }

    private float EvaluateInternalValue(InternalDiagramForces f, int component, float x)
    {
        if (f.length <= 0f || !FrameForces.IsValid(f.localActions)) return 0f;
        return FrameForces.Evaluate(f.localActions, f.length, x / f.length).Component(component);
    }

    private string FormatStructuralVector(Vector3 v)
    {
        return $"({v.x:0.###},{v.z:0.###},{v.y:0.###})";
    }

    private void CreateAuditLocalAxes(ElementSelectable element, InternalDiagramForces f)
    {
        Vector3 origin = element.startPoint;
        CreateLine(element.startPoint, element.endPoint, Color.white, 0.10f, "AUDIT_B3003_ElementAxis");
        CreateAuditAxis("AUDIT_B3003_localX", origin, f.localX, Color.red, "localX");
        CreateAuditAxis("AUDIT_B3003_localY", origin, f.localY, Color.green, "localY");
        CreateAuditAxis("AUDIT_B3003_localZ", origin, f.localZ, Color.blue, "localZ");
    }

    private void CreateAuditAxis(string name, Vector3 origin, Vector3 direction, Color color, string label)
    {
        Vector3 end = origin + direction.normalized * 2.2f;
        CreateLine(origin, end, color, 0.07f, name);
        CreateAuditText(end + Vector3.up * 0.15f, label, color);
    }

    private void CreateAuditText(Vector3 position, string label, Color color)
    {
        GameObject labelObject = new GameObject("AUDIT_Label_" + label);
        labelObject.transform.SetParent(transform);
        labelObject.hideFlags = HideFlags.DontSave;
        labelObject.transform.position = position;
        TextMesh text = labelObject.AddComponent<TextMesh>();
        text.text = label;
        text.characterSize = 0.26f;
        text.anchor = TextAnchor.MiddleCenter;
        text.color = color;
        diagramObjects.Add(labelObject);
    }

    private void CreateAuditComponentDiagram(ElementSelectable element, InternalDiagramForces f, string componentName, Color color, Vector3 offsetDirection, int component)
    {
        int pointCount = 21;
        Vector3[] points = new Vector3[pointCount];
        float maxAbs = 0.001f;
        for (int i = 0; i < pointCount; i++)
        {
            float x = f.length * i / (pointCount - 1);
            maxAbs = Mathf.Max(maxAbs, Mathf.Abs(EvaluateInternalValue(f, component, x)));
        }

        float visualScale = 1.4f / maxAbs;
        for (int i = 0; i < pointCount; i++)
        {
            float x = f.length * i / (pointCount - 1);
            float value = EvaluateInternalValue(f, component, x);
            Vector3 basePoint = element.startPoint + f.localX * x;
            points[i] = basePoint + offsetDirection.normalized * (0.25f + value * visualScale);
        }

        GameObject lineObject = new GameObject($"AUDIT_B3003_{componentName}");
        lineObject.transform.SetParent(transform);
        lineObject.hideFlags = HideFlags.DontSave;
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.startWidth = 0.075f;
        line.endWidth = 0.075f;
        line.useWorldSpace = true;
        line.material = CreateMaterial(color);
        diagramObjects.Add(lineObject);

        for (int i = 0; i < pointCount; i++)
        {
            float x = f.length * i / (pointCount - 1);
            float value = EvaluateInternalValue(f, component, x);
            CreateLabel(points[i], value, " " + componentName, lineObject.transform);
        }
    }

    private float MaxForElement(ElementSelectable element)
    {
        if (element == null || element.data == null) return 1f;
        string building = string.IsNullOrEmpty(element.data.sourceBuilding) ? "?" : element.data.sourceBuilding;
        return currentMaxByBuilding.TryGetValue(building, out float v) ? v : 1f;
    }

    private float GetValue(ElementSelectable element, DiagramMode mode, float t, float length)
    {
        if (element == null || element.data == null)
        {
            return 0f;
        }
        return GetBaseValue(element, mode, t, length);
    }

    private float GetBaseValue(ElementSelectable element, DiagramMode mode, float t, float length)
    {
        if (element == null || element.data == null ||
            !UnityData.TryGetSectionForces(element.data.id, UnityData.ActiveCombo, t, out var f))
        {
            return 0f;
        }

        if (mode == DiagramMode.Axial)
        {
            return f.N;
        }

        if (mode == DiagramMode.Shear)
        {
            float vy = f.Vy;
            float vz = f.Vz;
            float sign = Mathf.Abs(vy) >= Mathf.Abs(vz) ? Mathf.Sign(vy) : Mathf.Sign(vz);
            return sign * Mathf.Sqrt(vy * vy + vz * vz);
        }

        float my = f.My;
        float mz = f.Mz;
        float momentSign = Mathf.Abs(my) >= Mathf.Abs(mz) ? Mathf.Sign(my) : Mathf.Sign(mz);
        return momentSign * Mathf.Sqrt(my * my + mz * mz);
    }

    public float ValueAt(ElementSelectable element, string modeName, float t, float length)
    {
        // Base curves for the separate vertical mobile-load simulation.
        // Do not add a scalar load effect to a biaxial resultant.
        if (element == null || element.data == null ||
            !UnityData.TryGetSectionForces(element.data.id, UnityData.ActiveCombo, t, out var f)) return 0f;
        if (modeName == "Axial") return f.N;
        if (modeName == "Shear") return f.Vz;
        if (modeName == "Moment") return f.My;
        return 0f;
    }

    private float ScaleFor(DiagramMode mode)
    {
        if (mode == DiagramMode.Axial) return diagramScale * axialMultiplier;
        if (mode == DiagramMode.Shear) return diagramScale * shearMultiplier;
        return diagramScale * momentMultiplier;
    }

    // Dibuja V/M en el plano local donde actua la componente dominante del
    // elemento (Vz/My -> z local, Vy/Mz -> y local). Para momentos el signo
    // deja el diagrama del lado traccionado: My+ tracciona -z; Mz+ tracciona +y.
    private Vector3 GetDiagramDirection(ElementSelectable element, DiagramMode mode, Vector3 axis, out float drawSign)
    {
        drawSign = mode == DiagramMode.Moment ? -1f : 1f;
        if (mode == DiagramMode.Axial || element == null || element.data == null ||
            !UnityData.TryGetFrameGeometry(element.data.id, out var frame))
        {
            return GetOffsetDirection(axis, mode);
        }

        float sumY = 0f;
        float sumZ = 0f;
        for (int i = 0; i <= 8; i++)
        {
            if (!UnityData.TryGetSectionForces(element.data.id, UnityData.ActiveCombo, i / 8f, out var f)) continue;
            if (mode == DiagramMode.Moment)
            {
                sumY += Mathf.Abs(f.Mz);
                sumZ += Mathf.Abs(f.My);
            }
            else
            {
                sumY += Mathf.Abs(f.Vy);
                sumZ += Mathf.Abs(f.Vz);
            }
        }

        bool zPlane = sumZ >= sumY;
        Vector3 direction = UnityData.AxisToUnity(zPlane ? frame.Z : frame.Y).normalized;
        if (direction.sqrMagnitude < 0.01f)
        {
            return GetOffsetDirection(axis, mode);
        }
        if (mode == DiagramMode.Moment)
        {
            drawSign = zPlane ? -1f : 1f;
        }
        return direction;
    }

    private Vector3 GetOffsetDirection(Vector3 axis, DiagramMode mode)
    {
        if (mode == DiagramMode.Moment && Mathf.Abs(axis.normalized.y) < 0.2f)
        {
            return Vector3.up;
        }

        Vector3 direction = Vector3.Cross(axis.normalized, Vector3.forward).normalized;
        if (direction.sqrMagnitude < 0.01f)
        {
            direction = Vector3.right;
        }

        return direction;
    }

    private Color GetColor(DiagramMode mode)
    {
        if (mode == DiagramMode.Axial) return Color.red;
        if (mode == DiagramMode.Shear) return new Color(1f, 0.55f, 0f);
        if (mode == DiagramMode.Moment) return Color.magenta;
        if (mode == DiagramMode.Deformed || mode == DiagramMode.DeformedReal) return new Color(0.3f, 1f, 0.4f);
        return Color.green;
    }

    private string UnitFor(DiagramMode mode)
    {
        if (mode == DiagramMode.Moment) return " kN*m";
        return " kN";
    }

    private void CreateLabel(Vector3 position, float value, string unit, Transform parent)
    {
        GameObject labelObject = new GameObject("ValorDiagrama");
        labelObject.transform.SetParent(parent);
        labelObject.hideFlags = HideFlags.DontSave;
        labelObject.transform.position = position + Vector3.up * 0.18f;

        TextMesh text = labelObject.AddComponent<TextMesh>();
        text.text = value.ToString("0.0") + unit;
        text.characterSize = 0.2f;
        text.anchor = TextAnchor.MiddleCenter;
        text.color = Color.white;
    }

    private Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Custom/AlwaysOnTopLine");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.color = color;
        material.renderQueue = shader.name == "Custom/AlwaysOnTopLine" ? 5000 : 4000;
        return material;
    }

    private void ClearDiagram()
    {
        RestoreOriginalSolidStructure();
        deformedSegments.Clear();
        foreach (GameObject diagramObject in diagramObjects)
        {
            if (Application.isPlaying)
            {
                Destroy(diagramObject);
            }
            else
            {
                DestroyImmediate(diagramObject);
            }
        }

        diagramObjects.Clear();
    }

    private ElementPicker cachedPicker;

    private void OnGUI()
    {
        DrawDeformationControls();
        DrawSelectedValueTable();
    }

    private void DrawDeformationControls()
    {
        if (currentMode != DiagramMode.Deformed) return;

        float width = 610f;
        float x = Mathf.Max(12f, (Screen.width - width) * 0.5f);
        float y = 138f;
        GUI.Box(new Rect(x, y, width, 32f), GUIContent.none);
        GUI.Label(new Rect(x + 10f, y + 7f, 112f, 20f), "Deformation Scale:");

        if (GUI.Button(new Rect(x + 124f, y + 5f, 28f, 22f), "−")) StepDeformationScale(-1);
        GUI.Label(new Rect(x + 157f, y + 7f, 48f, 20f), $"{deformedMultiplier:0.#}x");
        if (GUI.Button(new Rect(x + 205f, y + 5f, 28f, 22f), "+")) StepDeformationScale(1);

        bool nextAnimate = GUI.Toggle(new Rect(x + 248f, y + 6f, 92f, 20f), animateDeformation, " Animate");
        if (nextAnimate != animateDeformation)
        {
            animateDeformation = nextAnimate;
            if (animateDeformation) deformationAnimationTime = 0f;
        }

        showOriginalDeformationReference = GUI.Toggle(new Rect(x + 342f, y + 6f, 88f, 20f),
            showOriginalDeformationReference, " Original");
        GUI.Label(new Rect(x + 438f, y + 7f, 166f, 20f),
            $"Max real: {maximumRealDisplacement * 1000f:0.###} mm (N{maximumDisplacementNode})");
    }

    private void StepDeformationScale(int direction)
    {
        float[] scales = { 1f, 10f, 25f, 50f, 100f, 200f, 500f };
        int nearest = 0;
        float distance = Mathf.Abs(deformedMultiplier - scales[0]);
        for (int i = 1; i < scales.Length; i++)
        {
            float candidate = Mathf.Abs(deformedMultiplier - scales[i]);
            if (candidate < distance)
            {
                nearest = i;
                distance = candidate;
            }
        }
        deformedMultiplier = scales[Mathf.Clamp(nearest + direction, 0, scales.Length - 1)];
        UpdateDeformedSegmentPositions(animateDeformation ? deformationAnimationFactor : 1f);
    }

    private void DrawSelectedValueTable()
    {
        if (currentMode == DiagramMode.None || currentMode == DiagramMode.Deformed || currentMode == DiagramMode.DeformedReal)
        {
            return;
        }

        if (cachedPicker == null) cachedPicker = FindObjectOfType<ElementPicker>();
        ElementPicker picker = cachedPicker;
        if (picker == null || picker.Selected == null)
        {
            return;
        }

        EnsureTableStyles();
        ElementSelectable selected = picker.Selected;
        if (selected.data == null && selected.isWall)
        {
            DrawSelectedWallValueTable(selected);
            return;
        }
        if (selected.data == null)
        {
            return;
        }

        ElementData data = selected.data;
        if (!UnityData.TryGetSectionForces(data.id, UnityData.ActiveCombo, 0f, out var availableForces)) return;
        float length = (selected.endPoint - selected.startPoint).magnitude;
        float vi = GetBaseValue(selected, currentMode, 0f, length);
        float vm = GetBaseValue(selected, currentMode, 0.5f, length);
        float vj = GetBaseValue(selected, currentMode, 1f, length);

        float vmax = vi;
        int segments = 24;
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float v = GetBaseValue(selected, currentMode, t, length);
            if (Mathf.Abs(v) > Mathf.Abs(vmax))
            {
                vmax = v;
            }
        }

        float nI, vyI, vzI, tI, myI, mzI;
        float nJ, vyJ, vzJ, tJ, myJ, mzJ;
        GetForcesAt(selected, 0f, length, out nI, out vyI, out vzI, out tI, out myI, out mzI);
        GetForcesAt(selected, 1f, length, out nJ, out vyJ, out vzJ, out tJ, out myJ, out mzJ);

        string tag = !string.IsNullOrEmpty(data.elementTag) ? data.elementTag : data.id.ToString();
        string unit = UnitFor(currentMode).Trim();
        string title = $"Valores {currentMode} - {tag}";
        string body = $"Combo: {UnityData.GetActiveLoadLabel()}\n" +
                      $"I = {vi:0.##} {unit} | centro = {vm:0.##} {unit} | J = {vj:0.##} {unit}\n" +
                      $"Max abs = {vmax:0.##} {unit}\n";

        if (currentMode == DiagramMode.Moment)
        {
            body += $"My I/J = {myI:0.##} / {myJ:0.##} kN*m\n" +
                    $"Mz I/J = {mzI:0.##} / {mzJ:0.##} kN*m";
        }
        else if (currentMode == DiagramMode.Shear)
        {
            body += $"Vy I/J = {vyI:0.##} / {vyJ:0.##} kN\n" +
                    $"Vz I/J = {vzI:0.##} / {vzJ:0.##} kN";
        }
        else
        {
            body += $"N I/J = {nI:0.##} / {nJ:0.##} kN\n" +
                    $"T I/J = {tI:0.##} / {tJ:0.##} kN*m";
        }

        float w = Mathf.Min(380f, Screen.width * 0.34f);
        float h = 132f;
        float x = Mathf.Max(16f, (Screen.width - w) * 0.5f);
        float y = Screen.height - h - 18f;
        Rect tableRect = PanelLayout.Apply("DiagramValues", new Rect(x, y, w, h));
        x = tableRect.x;
        y = tableRect.y;
        GUI.Box(new Rect(x, y, w, h), GUIContent.none, tableBoxStyle);
        GUI.Label(new Rect(x + 12f, y + 8f, w - 24f, 22f), title, tableTitleStyle);
        GUI.Label(new Rect(x + 12f, y + 32f, w - 24f, h - 40f), body, tableTextStyle);
    }

    private void DrawSelectedWallValueTable(ElementSelectable selected)
    {
        DemandRecord demand = selected.GetActiveWallDemand();
        if (demand == null)
        {
            return;
        }

        float value = 0f;
        string unit = "kN";
        string detail = "";
        if (currentMode == DiagramMode.Axial)
        {
            value = demand.P_kN;
            detail = $"N demanda = {demand.P_kN:0.##} kN";
        }
        else if (currentMode == DiagramMode.Shear)
        {
            value = 0f;
            detail = "Vy/Vz no se extraen como fuerza interna de barra para muros equivalentes.";
        }
        else if (currentMode == DiagramMode.Moment)
        {
            value = demand.M_kN_m;
            unit = "kN*m";
            detail = $"My/Mz demanda P-M = {demand.M_kN_m:0.##} kN*m";
        }

        string body = $"Combo: {UnityData.GetActiveLoadLabel()}\n" +
                      $"I = {value:0.##} {unit} | centro = {value:0.##} {unit} | J = {value:0.##} {unit}\n" +
                      $"Max abs = {value:0.##} {unit}\n" +
                      detail;

        float w = Mathf.Min(380f, Screen.width * 0.34f);
        float h = 118f;
        float x = Mathf.Max(16f, (Screen.width - w) * 0.5f);
        float y = Screen.height - h - 18f;
        Rect tableRect = PanelLayout.Apply("DiagramWallValues", new Rect(x, y, w, h));
        x = tableRect.x;
        y = tableRect.y;
        GUI.Box(new Rect(x, y, w, h), GUIContent.none, tableBoxStyle);
        GUI.Label(new Rect(x + 12f, y + 8f, w - 24f, 22f), $"Valores {currentMode} - Muro {selected.wallId}", tableTitleStyle);
        GUI.Label(new Rect(x + 12f, y + 32f, w - 24f, h - 40f), body, tableTextStyle);
    }

    private void GetForcesAt(ElementSelectable element, float t, float length,
        out float n, out float vy, out float vz, out float torsion, out float my, out float mz)
    {
        UnityData.TryGetSectionForces(element.data.id, UnityData.ActiveCombo, t, out var f);
        n = f.N; vy = f.Vy; vz = f.Vz; torsion = f.T; my = f.My; mz = f.Mz;
    }

    private void EnsureTableStyles()
    {
        if (tableBoxStyle != null) return;
        tableBoxStyle = new GUIStyle(GUI.skin.box);
        tableBoxStyle.normal.background = MakeTex(new Color(0.03f, 0.03f, 0.04f, 0.88f));
        tableTextStyle = new GUIStyle(GUI.skin.label);
        tableTextStyle.fontSize = 13;
        tableTextStyle.normal.textColor = Color.white;
        tableTextStyle.wordWrap = true;
        tableTitleStyle = new GUIStyle(tableTextStyle);
        tableTitleStyle.fontStyle = FontStyle.Bold;
        tableTitleStyle.fontSize = 14;
    }

    private Texture2D MakeTex(Color color)
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }
}
