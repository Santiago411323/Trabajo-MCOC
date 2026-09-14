using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[ExecuteAlways]
public class DiagramController : MonoBehaviour
{
    private enum DiagramMode
    {
        None,
        Axial,
        Shear,
        Moment,
        Deformed
    }

    public float diagramScale = 1.3f;
    public float axialMultiplier = 0.9f;
    public float shearMultiplier = 1.0f;
    public float momentMultiplier = 1.2f;
    public float diagramBaseOffset = 0.06f;
    public float deformedMultiplier = 120f;
    public float deformedTargetPct = 0.06f;

    private readonly List<ElementSelectable> elements = new List<ElementSelectable>();
    private readonly List<ElementSelectable> structuralElements = new List<ElementSelectable>();
    private readonly List<GameObject> diagramObjects = new List<GameObject>();
    private DiagramMode currentMode = DiagramMode.None;
    private readonly Dictionary<string, float> deformedScaleByBuilding = new Dictionary<string, float>();
    private Dictionary<string, float> currentMaxByBuilding = new Dictionary<string, float>();

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
        }
#endif
        return Input.GetKeyDown(key);
    }

    private void ShowDiagram(DiagramMode mode)
    {
        currentMode = mode;
        modeToRedraw = mode;
        ClearDiagram();

        if (mode == DiagramMode.None)
        {
            return;
        }

        if (mode == DiagramMode.Deformed)
        {
            deformedScaleByBuilding.Clear();
            CreateDeformedDiagram();
            Debug.Log("[DiagramController] modo Deformada activado (escala por edificio)");
            return;
        }

        currentMaxByBuilding.Clear();
        currentMaxByBuilding = GetMaxValueByBuilding(mode);
        int created = 0;

        foreach (ElementSelectable element in structuralElements)
        {
            if ((mode == DiagramMode.Moment || mode == DiagramMode.Shear) && element.data.type != "viga")
            {
                continue;
            }

            CreateElementDiagram(element, mode);
            created++;
        }

        Debug.Log($"[DiagramController] modo {mode}: {created} diagramas, max por edificio {FormatMaxByBuilding()}");
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
        if (string.IsNullOrEmpty(combo) || UnityData.DisplacementsByCombo == null)
        {
            Debug.LogWarning("[DiagramController] No hay desplazamientos para el combo activo.");
            return;
        }

        int created = 0;
        var scales = new List<string>();
        foreach (ElementSelectable element in structuralElements)
        {
            string building = string.IsNullOrEmpty(element.data.sourceBuilding) ? "?" : element.data.sourceBuilding;
            float scale = GetDeformedScale(building, combo);
            if (scale <= 0f)
            {
                scale = deformedMultiplier;
            }

            Vector3 dI = UnityData.GetNodeDisplacement(combo, element.data.nodeI);
            Vector3 dJ = UnityData.GetNodeDisplacement(combo, element.data.nodeJ);

            Vector3 p0 = element.startPoint + dI * scale;
            Vector3 p1 = element.endPoint + dJ * scale;

            CreateLine(element.startPoint, element.endPoint, new Color(0.5f, 0.5f, 0.55f, 0.6f), 0.04f,
                $"Deformada_Ref_E{element.data.id}");

            CreateLine(p0, p1, new Color(0.35f, 1f, 0.4f), 0.16f,
                $"Deformada_E{element.data.id}");

            created++;
        }

        foreach (var kv in deformedScaleByBuilding)
        {
            scales.Add($"{kv.Key}={kv.Value:0.#}");
        }
        Debug.Log($"[DiagramController] Deformada combo={combo}: {created} elementos, escala por edificio {string.Join(", ", scales)} ({deformedTargetPct * 100:0.#}% de la altura por edificio)");
    }

    private void CreateLine(Vector3 a, Vector3 b, Color color, float width, string name)
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
    }

    private float GetDeformedScale(string building, string combo)
    {
        if (deformedScaleByBuilding.TryGetValue(building, out float cached))
        {
            return cached;
        }

        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        float maxDisp = 0f;
        bool first = true;

        foreach (ElementSelectable e in structuralElements)
        {
            if (e.data == null) continue;
            string eb = string.IsNullOrEmpty(e.data.sourceBuilding) ? "?" : e.data.sourceBuilding;
            if (eb != building) continue;

            Vector3 a = e.startPoint;
            Vector3 b = e.endPoint;
            if (first)
            {
                bounds = new Bounds(a, Vector3.zero);
                bounds.Encapsulate(b);
                first = false;
            }
            else
            {
                bounds.Encapsulate(a);
                bounds.Encapsulate(b);
            }

            Vector3 dI = UnityData.GetNodeDisplacement(combo, e.data.nodeI);
            Vector3 dJ = UnityData.GetNodeDisplacement(combo, e.data.nodeJ);
            maxDisp = Mathf.Max(maxDisp, dI.magnitude, dJ.magnitude);
        }

        float scale = 0f;
        if (maxDisp >= 1e-9f)
        {
            float height = bounds.size.y + 1f;
            scale = (height * deformedTargetPct) / maxDisp;
        }

        deformedScaleByBuilding[building] = scale;
        return scale;
    }

    private Dictionary<string, float> GetMaxValueByBuilding(DiagramMode mode)
    {
        var result = new Dictionary<string, float>();

        foreach (ElementSelectable element in structuralElements)
        {
            if (element.data == null) continue;
            if ((mode == DiagramMode.Moment || mode == DiagramMode.Shear) && element.data.type != "viga")
            {
                continue;
            }

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
        Vector3 offsetDirection = GetOffsetDirection(axis, mode);
        float length = axis.magnitude;

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector3 basePoint = Vector3.Lerp(element.startPoint, element.endPoint, t);
            float value = GetValue(element, mode, t, length);
            float maxValue = MaxForElement(element);
            points[i] = basePoint + offsetDirection * (diagramBaseOffset + value / maxValue * ScaleFor(mode));
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

    private float MaxForElement(ElementSelectable element)
    {
        if (element == null || element.data == null) return 1f;
        string building = string.IsNullOrEmpty(element.data.sourceBuilding) ? "?" : element.data.sourceBuilding;
        return currentMaxByBuilding.TryGetValue(building, out float v) ? v : 1f;
    }

    private float GetValue(ElementSelectable element, DiagramMode mode, float t, float length)
    {
        ElementData data = element.data;
        if (data == null)
        {
            return 0f;
        }

        if (mode == DiagramMode.Axial)
        {
            return GetForceGradient(data, t, 0, 6);
        }

        if (mode == DiagramMode.Shear)
        {
            return GetForceGradient(data, t, 1, 7);
        }

        float linearMoment = GetForceGradient(data, t, 4, 10);
        float spanMoment = Mathf.Abs(data.uniformLoad) * length * length * t * (1f - t) / 2f;
        return linearMoment + spanMoment;
    }

    private float GetForceGradient(ElementData data, float t, int iIndex, int jIndex)
    {
        if (!string.IsNullOrEmpty(UnityData.ActiveCombo) && UnityData.ElementForcesByCombo != null)
        {
            float[] f = UnityData.GetElementForces(UnityData.ActiveCombo, data.id);
            if (f != null && f.Length >= 12 && iIndex < 12 && jIndex < 12)
            {
                return Mathf.Lerp(f[iIndex], f[jIndex], t);
            }
        }

        if (iIndex == 0)
        {
            return Mathf.Lerp(data.axialI, data.axialJ, t);
        }
        if (iIndex == 1)
        {
            return Mathf.Lerp(data.shearI, data.shearJ, t);
        }
        return Mathf.Lerp(data.momentI, data.momentJ, t);
    }

    private float ScaleFor(DiagramMode mode)
    {
        if (mode == DiagramMode.Axial) return diagramScale * axialMultiplier;
        if (mode == DiagramMode.Shear) return diagramScale * shearMultiplier;
        return diagramScale * momentMultiplier;
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
        if (mode == DiagramMode.Deformed) return new Color(0.3f, 1f, 0.4f);
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

    private void OnGUI()
    {
        float boxW = Mathf.Min(440f, Screen.width - 40f);
        float boxY = 20f;

        GUILayout.BeginArea(new Rect(Screen.width - boxW - 20f, boxY, boxW, 130f), GUI.skin.box);
        GUILayout.Label("Diagramas OpenSees");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("0 Ocultar")) ShowDiagram(DiagramMode.None);
        if (GUILayout.Button("1 Axial")) ShowDiagram(DiagramMode.Axial);
        if (GUILayout.Button("2 Corte")) ShowDiagram(DiagramMode.Shear);
        if (GUILayout.Button("3 Momento")) ShowDiagram(DiagramMode.Moment);
        if (GUILayout.Button("5 Deformada")) ShowDiagram(DiagramMode.Deformed);
        GUILayout.EndHorizontal();
        string comboText = string.IsNullOrEmpty(UnityData.ActiveCombo) ? "sin combo" : UnityData.ActiveCombo;
        GUILayout.Label($"Actual: {currentMode} | Combo: {comboText}");
        GUILayout.Label("P-M: selecciona una columna o muro en la escena.");
        GUILayout.EndArea();

        string useHint = Application.isPlaying
            ? "Teclas 1-5 o botones para cambiar de diagrama."
            : "Modo edicion: usa los botones (las teclas requieren Play).";
        GUI.Label(new Rect(Screen.width - boxW - 20f, boxY + 134f, boxW, 24f), useHint);

        if (currentMode == DiagramMode.Moment)
        {
            GUI.Label(new Rect(Screen.width - boxW - 20f, boxY + 158f, boxW, 24f), "Momento My: valores OpenSees + qL2/8 en vigas");
        }
    }
}
