using System;
using System.Collections.Generic;
using UnityEngine;

// Compact, opt-in results area placed directly below the element inspector.
// It only presents data that is actually exported by OpenSees/P1L4.
public class ElementResultsPanel : MonoBehaviour
{
    private enum ResultView { None, Forces, Deformed, Interaction, Fibers, StressStrain, MomentCurvature }

    private static ElementResultsPanel active;
    private readonly float[,] samples = new float[6, 61];
    private readonly string[] forceNames = { "N", "Vy", "Vz", "My", "Mz" };
    private readonly int[] forceRows = { 4, 2, 3, 0, 1 };
    private ElementPicker picker;
    private DiagramController diagrams;
    private ElementSelectable selected;
    private bool expanded;
    private int forceIndex = 4;
    private ResultView view;
    private Rect panelRect;
    private GUIStyle panelStyle, titleStyle, textStyle, mutedStyle, valueStyle, successStyle, failureStyle;
    private Texture2D background;

    public static float ReservedHeight
    {
        get
        {
            if (active == null || active.SelectedElement() == null) return 0f;
            if (!active.expanded) return 54f;
            if (active.view == ResultView.Interaction)
                return Mathf.Min(410f, Mathf.Max(300f, Screen.height * .38f));
            return 330f;
        }
    }

    public static bool BlocksPointer(Vector2 mouse)
    {
        return active != null && active.isActiveAndEnabled &&
            active.SelectedElement() != null && active.GetPanelRect().Contains(mouse);
    }

    public static void SelectionChanged(ElementSelectable element)
    {
        if (active != null) active.ResetFor(element);
    }

    private void OnEnable()
    {
        active = this;
        Bind();
    }

    private void OnDisable()
    {
        ClearResultVisualization();
        if (active == this) active = null;
        if (background != null) Destroy(background);
    }

    private void Bind()
    {
        if (picker == null) picker = FindObjectOfType<ElementPicker>();
        if (diagrams == null) diagrams = GetComponent<DiagramController>();
    }

    private ElementSelectable SelectedElement()
    {
        Bind();
        return picker != null ? picker.Selected : null;
    }

    private void Update()
    {
        ElementSelectable current = SelectedElement();
        if (current != selected) ResetFor(current);
    }

    private void ResetFor(ElementSelectable element)
    {
        ClearResultVisualization();
        if (diagrams != null) diagrams.SetResultMode("None");
        selected = element;
        expanded = false;
        view = ResultView.None;
        forceIndex = 4;
    }

    private void ClearResultVisualization()
    {
        if (view == ResultView.Deformed && diagrams != null)
            diagrams.SetResultMode("None");
        PMPanel pm = FindObjectOfType<PMPanel>();
        if (pm != null) pm.Hide();
    }

    private Rect GetPanelRect()
    {
        if (picker == null) return Rect.zero;
        Rect baseRect = picker.GetBasePanelRect();
        float height = ReservedHeight;
        float width = expanded && view == ResultView.Interaction
            ? Mathf.Min(940f, Screen.width - 32f)
            : baseRect.width;
        panelRect = new Rect(Screen.width - picker.panelOffset.x - width,
            baseRect.yMax - height, width, height);
        return panelRect;
    }

    private void OnGUI()
    {
        if (!Application.isPlaying) return;
        ElementSelectable element = SelectedElement();
        if (element == null) return;
        if (element != selected) ResetFor(element);
        EnsureStyles();

        Rect rect = GetPanelRect();
        GUI.Box(rect, GUIContent.none, panelStyle);
        string type = ElementType(element).ToUpperInvariant();
        string tag = ElementTag(element);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 7f, rect.width - 150f, 22f),
            "RESULTADOS — " + type + " " + tag, titleStyle);

        if (!expanded)
        {
            if (GUI.Button(new Rect(rect.x + 12f, rect.y + 28f, rect.width - 24f, 22f), "ACTIVAR RESULTADOS"))
                expanded = true;
            return;
        }

        if (GUI.Button(new Rect(rect.xMax - 132f, rect.y + 6f, 120f, 23f), "OCULTAR RESULTADOS"))
        {
            ClearResultVisualization();
            expanded = false;
            view = ResultView.None;
            return;
        }

        float x = rect.x + 12f;
        float y = rect.y + 36f;
        float width = rect.width - 24f;
        y = DrawResultOptions(element, x, y, width);
        GUI.Label(new Rect(x, y, width, 19f), "Caso: " + UnityData.GetActiveLoadLabel(), mutedStyle);
        y += 22f;

        switch (view)
        {
            case ResultView.Forces: DrawForces(element, x, y, width); break;
            case ResultView.Deformed: DrawDeformed(element, x, y, width); break;
            case ResultView.Interaction: DrawInteraction(element, x, y, width); break;
            case ResultView.Fibers: DrawFibers(element, x, y, width); break;
            case ResultView.StressStrain:
                DrawUnavailable(x, y, width, "TENSIÓN–DEFORMACIÓN",
                    "El archivo actual contiene propiedades de material, pero no curvas constitutivas ni historia de fibras exportadas.");
                break;
            case ResultView.MomentCurvature:
                DrawUnavailable(x, y, width, "MOMENTO–CURVATURA",
                    "No existe un análisis momento–curvatura exportado para esta sección. La curva P–M no se reutiliza porque representa otro ensayo.");
                break;
            default:
                GUI.Label(new Rect(x, y + 8f, width, 42f),
                    "Elija un resultado. Ningún diagrama se abre automáticamente al seleccionar el elemento.", textStyle);
                break;
        }
    }

    private float DrawResultOptions(ElementSelectable element, float x, float y, float width)
    {
        if (IsBeam(element))
        {
            DrawOption(x, y, width * .5f - 3f, "DIAGRAMAS DE ESFUERZOS", ResultView.Forces);
            DrawOption(x + width * .5f + 3f, y, width * .5f - 3f, "DEFORMADA", ResultView.Deformed);
            return y + 31f;
        }

        if (IsColumn(element))
        {
            string[] labels = { "INTERACCIÓN P–M", "SECCIÓN DE FIBRAS", "TENSIÓN–DEFORMACIÓN",
                "DIAGRAMAS DE ESFUERZOS", "MOMENTO–CURVATURA", "DEFORMADA" };
            ResultView[] views = { ResultView.Interaction, ResultView.Fibers, ResultView.StressStrain,
                ResultView.Forces, ResultView.MomentCurvature, ResultView.Deformed };
            return DrawGrid(x, y, width, labels, views, 3);
        }

        string[] wallLabels = { "INTERACCIÓN P–M", "SECCIÓN DE FIBRAS", "MOMENTO–CURVATURA", "DEFORMADA" };
        ResultView[] wallViews = { ResultView.Interaction, ResultView.Fibers, ResultView.MomentCurvature, ResultView.Deformed };
        return DrawGrid(x, y, width, wallLabels, wallViews, 2);
    }

    private float DrawGrid(float x, float y, float width, string[] labels, ResultView[] views, int columns)
    {
        float gap = 5f;
        float cell = (width - gap * (columns - 1)) / columns;
        for (int i = 0; i < labels.Length; i++)
            DrawOption(x + (i % columns) * (cell + gap), y + (i / columns) * 29f, cell, labels[i], views[i]);
        return y + Mathf.Ceil(labels.Length / (float)columns) * 29f + 2f;
    }

    private void DrawOption(float x, float y, float width, string label, ResultView target)
    {
        Color old = GUI.backgroundColor;
        if (view == target) GUI.backgroundColor = new Color(.18f, .78f, .95f);
        if (GUI.Button(new Rect(x, y, width, 24f), label)) SetView(target);
        GUI.backgroundColor = old;
    }

    private void SetView(ResultView next)
    {
        if (view == ResultView.Deformed && next != ResultView.Deformed && diagrams != null)
            diagrams.SetResultMode("None");
        view = next;
        PMPanel pm = FindObjectOfType<PMPanel>();
        if (pm != null) pm.Hide();
        if (view == ResultView.Deformed && diagrams != null)
            diagrams.SetResultMode("Deformada");
    }

    private void DrawForces(ElementSelectable element, float x, float y, float width)
    {
        if (element.data == null)
        {
            DrawUnavailable(x, y, width, "DIAGRAMAS DE ESFUERZOS", "El muro no tiene fuerzas de barra N/V/M exportadas.");
            return;
        }
        forceIndex = GUI.Toolbar(new Rect(x, y, width, 25f), forceIndex, forceNames);
        y += 30f;
        if (diagrams == null || !diagrams.TryGetSelectedDiagramSamples(element, samples))
        {
            DrawUnavailable(x, y, width, forceNames[forceIndex], "No hay fuerzas válidas para este elemento y combinación.");
            return;
        }

        int row = forceRows[forceIndex];
        float min = samples[row, 0], max = samples[row, 0];
        for (int i = 1; i < samples.GetLength(1); i++)
        {
            min = Mathf.Min(min, samples[row, i]);
            max = Mathf.Max(max, samples[row, i]);
        }
        string unit = forceIndex >= 3 ? "kN·m" : "kN";
        GUI.Label(new Rect(x, y, width, 22f),
            $"{forceNames[forceIndex]} [{unit}]     mín {min:0.###}     máx {max:0.###}", valueStyle);
        Rect plot = new Rect(x + 10f, y + 28f, width - 20f, 98f);
        GUI.Box(plot, GUIContent.none);
        float bound = Mathf.Max(Mathf.Abs(min), Mathf.Abs(max), .000001f);
        float zero = plot.center.y;
        DrawLine(new Vector2(plot.x + 5f, zero), new Vector2(plot.xMax - 5f, zero), new Color(.45f, .5f, .58f), 1f);
        Vector2 previous = Vector2.zero;
        int count = samples.GetLength(1);
        for (int i = 0; i < count; i++)
        {
            Vector2 point = new Vector2(plot.x + 5f + (plot.width - 10f) * i / (count - 1),
                zero - samples[row, i] / bound * (plot.height * .42f));
            if (i > 0) DrawLine(previous, point, new Color(.18f, .82f, 1f), 2f);
            previous = point;
        }
        GUI.Label(new Rect(x, y + 130f, width, 20f),
            $"I {samples[row, 0]:0.###}   Centro {samples[row, count / 2]:0.###}   J {samples[row, count - 1]:0.###}", textStyle);
    }

    private void DrawDeformed(ElementSelectable element, float x, float y, float width)
    {
        int nodeI = element.data != null ? element.data.nodeI : element.nodeIId;
        int nodeJ = element.data != null ? element.data.nodeJ : element.nodeJId;
        Vector3 ui = UnityData.GetNodeDisplacement(UnityData.ActiveCombo, nodeI);
        Vector3 uj = UnityData.GetNodeDisplacement(UnityData.ActiveCombo, nodeJ);
        Vector3 critical = ui.magnitude >= uj.magnitude ? ui : uj;
        int criticalNode = ui.magnitude >= uj.magnitude ? nodeI : nodeJ;
        float globalMax = diagrams != null ? diagrams.MaximumRealDisplacement : 0f;
        int globalNode = diagrams != null ? diagrams.MaximumDisplacementNode : 0;
        float scale = diagrams != null ? diagrams.DeformationVisualScale : 1f;

        GUI.Label(new Rect(x, y, width, 23f),
            $"Máximo del elemento: {critical.magnitude * 1000f:0.###} mm — nodo N{criticalNode}", valueStyle);
        GUI.Label(new Rect(x, y + 27f, width, 42f),
            $"Ux {critical.x * 1000f:0.###} mm   Uy {critical.z * 1000f:0.###} mm   Uz {critical.y * 1000f:0.###} mm\n" +
            $"Máximo del edificio: {globalMax * 1000f:0.###} mm — nodo N{globalNode}", textStyle);
        GUI.Label(new Rect(x, y + 74f, width, 54f),
            $"Escala visual: {scale:0.#}x. Los valores numéricos permanecen reales.\n" +
            $"Nodo I: |u|={ui.magnitude * 1000f:0.###} mm   Nodo J: |u|={uj.magnitude * 1000f:0.###} mm", mutedStyle);
    }

    private void DrawInteraction(ElementSelectable element, float x, float y, float width)
    {
        PMCurveData curve = UnityData.GetPMCurve(element.pmSectionId);
        if (curve == null || curve.points == null || curve.points.Length < 2)
        {
            DrawUnavailable(x, y, width, "INTERACCIÓN P–M", "No existe una curva P–M exportada para esta sección.");
            return;
        }

        Vector2 demand = GetDemand(element, curve);
        float pMin = curve.points[0].P_kN, pMax = pMin, mMax = 0f;
        foreach (PMPoint point in curve.points)
        {
            pMin = Mathf.Min(pMin, point.P_kN); pMax = Mathf.Max(pMax, point.P_kN);
            mMax = Mathf.Max(mMax, Mathf.Abs(point.M_kN_m));
        }
        pMin = Mathf.Min(pMin, demand.x);
        pMax = Mathf.Max(pMax, demand.x);
        float pPadding = Mathf.Max((pMax - pMin) * .08f, 1f);
        pMin -= pPadding; pMax += pPadding;
        mMax = Mathf.Max(mMax, Mathf.Abs(demand.y), .001f) * 1.12f;
        float pRange = Mathf.Max(.001f, pMax - pMin);

        List<CharacteristicPoint> characteristic = BuildCharacteristicPoints(element, curve);
        float plotColumn = width * .39f;
        float listColumn = width * .31f;
        float statusColumn = width - plotColumn - listColumn - 24f;
        float availableHeight = Mathf.Max(190f, panelRect.yMax - y - 16f);
        float plotSize = Mathf.Min(plotColumn - 52f, availableHeight - 22f);
        plotSize = Mathf.Max(175f, plotSize);
        Rect plot = new Rect(x + 42f, y + 18f, plotSize, plotSize);
        GUI.Box(new Rect(plot.x - 8f, plot.y - 8f, plot.width + 16f, plot.height + 16f), GUIContent.none);
        DrawInteractionGrid(plot, pMin, pMax, mMax);

        Vector2 previousPositive = Vector2.zero;
        Vector2 previousNegative = Vector2.zero;
        for (int i = 0; i < curve.points.Length; i++)
        {
            PMPoint point = curve.points[i];
            Vector2 positive = MapPM(plot, Mathf.Abs(point.M_kN_m), point.P_kN, pMin, pRange, mMax);
            Vector2 negative = MapPM(plot, -Mathf.Abs(point.M_kN_m), point.P_kN, pMin, pRange, mMax);
            if (i > 0)
            {
                DrawLine(previousPositive, positive, new Color(.15f, .62f, 1f), 2f);
                DrawLine(previousNegative, negative, new Color(.15f, .62f, 1f), 2f);
            }
            previousPositive = positive;
            previousNegative = negative;
        }
        PMPoint first = curve.points[0];
        PMPoint last = curve.points[curve.points.Length - 1];
        DrawLine(MapPM(plot, Mathf.Abs(first.M_kN_m), first.P_kN, pMin, pRange, mMax),
            MapPM(plot, -Mathf.Abs(first.M_kN_m), first.P_kN, pMin, pRange, mMax), new Color(.15f, .62f, 1f), 2f);
        DrawLine(MapPM(plot, Mathf.Abs(last.M_kN_m), last.P_kN, pMin, pRange, mMax),
            MapPM(plot, -Mathf.Abs(last.M_kN_m), last.P_kN, pMin, pRange, mMax), new Color(.15f, .62f, 1f), 2f);

        foreach (CharacteristicPoint point in characteristic)
        {
            Vector2 marker = MapPM(plot, Mathf.Abs(point.M), point.P, pMin, pRange, mMax);
            DrawMarker(marker, new Color(.35f, .88f, 1f), 6f);
            GUI.Label(new Rect(marker.x + 5f, marker.y - 10f, 22f, 18f), point.code, valueStyle);
        }

        Vector2 demandPoint = MapPM(plot, demand.y, demand.x, pMin, pRange, mMax);
        DrawMarker(demandPoint, new Color(.2f, 1f, .35f), 9f);
        GUI.Label(new Rect(plot.x - 5f, plot.y - 29f, plot.width + 10f, 20f),
            "P [kN], compresión +     —     M positivo y negativo", mutedStyle);

        float listX = x + plotColumn;
        GUI.Label(new Rect(listX, y, listColumn - 8f, 22f), "PUNTOS CARACTERÍSTICOS", titleStyle);
        float rowY = y + 27f;
        foreach (CharacteristicPoint point in characteristic)
        {
            GUI.Label(new Rect(listX, rowY, listColumn - 8f, 19f),
                $"{point.code}  P={point.P:0.##} kN   |M|={Mathf.Abs(point.M):0.##} kN·m", textStyle);
            rowY += 20f;
        }
        rowY += 4f;
        GUI.Label(new Rect(listX, rowY, listColumn - 8f, 88f),
            "A Compresión pura\nB Compresión total\nC Inicio de tracción\nD Condición balanceada\n" +
            "E Control por tracción\nF Flexión pura\nG Tracción pura", mutedStyle);
        if (characteristic.Exists(point => point.interpolated))
            GUI.Label(new Rect(listX, rowY + 91f, listColumn - 8f, 42f),
                "* B y C interpolados sobre A–D: el archivo de columna exporta cinco estados calculados.", mutedStyle);

        float statusX = listX + listColumn + 12f;
        float ratio = UnityData.CapacityRatio(curve, demand.x, demand.y);
        bool inside = ratio < UnityData.OutOfCurveRatio && ratio <= 1f + .0001f;
        GUI.Label(new Rect(statusX, y, statusColumn, 22f), "ESTADO Y VERIFICACIÓN", titleStyle);
        GUI.Label(new Rect(statusX, y + 29f, statusColumn, 58f),
            $"P demanda: {demand.x:0.###} kN\nM demanda: {demand.y:0.###} kN·m\nC = {FormatCapacity(curve, demand)}", valueStyle);
        GUI.Label(new Rect(statusX, y + 92f, statusColumn, 42f),
            inside ? "[ OK ] DENTRO DE CAPACIDAD" : "[ NO CUMPLE ] FUERA DE CAPACIDAD",
            inside ? successStyle : failureStyle);
        GUI.Label(new Rect(statusX, y + 139f, statusColumn, 78f),
            inside
                ? "La demanda se encuentra dentro de la envolvente nominal P–M para la combinación activa."
                : "La demanda queda fuera de la envolvente nominal P–M o supera C = 1.", textStyle);
        GUI.Label(new Rect(statusX, y + 220f, statusColumn, 40f),
            $"Sección: {curve.sectionId}\nEnvolvente: ±M simétrica", mutedStyle);
    }

    private sealed class CharacteristicPoint
    {
        public string code;
        public float P, M;
        public bool interpolated;
    }

    private List<CharacteristicPoint> BuildCharacteristicPoints(ElementSelectable element, PMCurveData curve)
    {
        if (IsColumn(element) && curve.points.Length == 5)
        {
            PMPoint a = curve.points[0], d = curve.points[1], e = curve.points[2], f = curve.points[3], g = curve.points[4];
            return new List<CharacteristicPoint> {
                CP("A",a), CPLerp("B",a,d,1f/3f), CPLerp("C",a,d,2f/3f),
                CP("D",d), CP("E",e), CP("F",f), CP("G",g) };
        }

        PMPoint maxP = curve.points[0], minP = curve.points[0], maxM = curve.points[0], zeroP = curve.points[0];
        foreach (PMPoint point in curve.points)
        {
            if (point.P_kN > maxP.P_kN) maxP = point;
            if (point.P_kN < minP.P_kN) minP = point;
            if (Mathf.Abs(point.M_kN_m) > Mathf.Abs(maxM.M_kN_m)) maxM = point;
            if (Mathf.Abs(point.P_kN) < Mathf.Abs(zeroP.P_kN)) zeroP = point;
        }
        PMPoint b = ClosestP(curve, Mathf.Lerp(maxP.P_kN, maxM.P_kN, 1f / 3f));
        PMPoint c = ClosestP(curve, Mathf.Lerp(maxP.P_kN, maxM.P_kN, 2f / 3f));
        PMPoint ePoint = ClosestP(curve, Mathf.Lerp(maxM.P_kN, zeroP.P_kN, .5f));
        return new List<CharacteristicPoint> {
            CP("A",maxP), CP("B",b), CP("C",c), CP("D",maxM),
            CP("E",ePoint), CP("F",zeroP), CP("G",minP) };
    }

    private static CharacteristicPoint CP(string code, PMPoint point)
    {
        return new CharacteristicPoint { code = code, P = point.P_kN, M = Mathf.Abs(point.M_kN_m) };
    }

    private static CharacteristicPoint CPLerp(string code, PMPoint a, PMPoint b, float t)
    {
        return new CharacteristicPoint { code = code, P = Mathf.Lerp(a.P_kN, b.P_kN, t),
            M = Mathf.Lerp(Mathf.Abs(a.M_kN_m), Mathf.Abs(b.M_kN_m), t), interpolated = true };
    }

    private static PMPoint ClosestP(PMCurveData curve, float target)
    {
        PMPoint best = curve.points[0];
        float distance = Mathf.Abs(best.P_kN - target);
        foreach (PMPoint point in curve.points)
        {
            float candidate = Mathf.Abs(point.P_kN - target);
            if (candidate < distance) { best = point; distance = candidate; }
        }
        return best;
    }

    private static Vector2 MapPM(Rect plot, float moment, float axial, float pMin, float pRange, float mMax)
    {
        return new Vector2(plot.center.x + moment / mMax * (plot.width * .5f),
            plot.yMax - (axial - pMin) / pRange * plot.height);
    }

    private void DrawInteractionGrid(Rect plot, float pMin, float pMax, float mMax)
    {
        for (int i = 0; i <= 4; i++)
        {
            float t = i / 4f;
            float px = Mathf.Lerp(plot.x, plot.xMax, t);
            float py = Mathf.Lerp(plot.y, plot.yMax, t);
            DrawLine(new Vector2(px, plot.y), new Vector2(px, plot.yMax), new Color(.23f, .3f, .38f), 1f);
            DrawLine(new Vector2(plot.x, py), new Vector2(plot.xMax, py), new Color(.23f, .3f, .38f), 1f);
            GUI.Label(new Rect(px - 34f, plot.yMax + 2f, 68f, 17f),
                Mathf.Lerp(-mMax, mMax, t).ToString("0.#"), mutedStyle);
            GUI.Label(new Rect(plot.x - 57f, py - 9f, 53f, 17f),
                Mathf.Lerp(pMax, pMin, t).ToString("0.#"), mutedStyle);
        }
        float zeroY = plot.yMax - (0f - pMin) / Mathf.Max(.001f, pMax - pMin) * plot.height;
        DrawLine(new Vector2(plot.x, zeroY), new Vector2(plot.xMax, zeroY), new Color(.68f, .75f, .82f), 1.5f);
        DrawLine(new Vector2(plot.center.x, plot.y), new Vector2(plot.center.x, plot.yMax), new Color(.68f, .75f, .82f), 1.5f);
    }

    private static void DrawMarker(Vector2 point, Color color, float size)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(point.x - size * .5f, point.y - size * .5f, size, size), Texture2D.whiteTexture);
        GUI.color = old;
    }

    private void DrawFibers(ElementSelectable element, float x, float y, float width)
    {
        string sectionId = element.data != null
            ? (!string.IsNullOrEmpty(element.data.sectionId) ? element.data.sectionId : element.data.seccion)
            : element.pmSectionId;
        SectionMaterialData material = UnityData.GetMaterial(sectionId);
        PMCurveData curve = UnityData.GetPMCurve(element.pmSectionId);
        float b = material != null ? material.b_m : curve != null ? curve.b_m : 0f;
        float h = material != null ? material.h_m : curve != null ? curve.h_m : 0f;
        int bars = material != null ? material.steelBars : curve != null ? curve.steelBars : 0;
        float diameter = material != null ? material.barDiameter_mm : curve != null ? curve.barDiameter_mm : 0f;
        GUI.Label(new Rect(x, y, width, 22f), "SECCIÓN DE FIBRAS — " + sectionId, valueStyle);
        GUI.Label(new Rect(x, y + 27f, width, 64f),
            $"Dimensiones exportadas: {b:0.###} × {h:0.###} m\n" +
            $"Armadura resumida: {bars} barras Ø{diameter:0.#} mm\n" +
            "La discretización y posición de cada fibra no están incluidas en el JSON actual.", textStyle);
        GUI.Label(new Rect(x, y + 98f, width, 42f),
            "Se muestra el resumen real de la sección. No se dibuja una malla de fibras supuesta.", mutedStyle);
    }

    private void DrawUnavailable(float x, float y, float width, string title, string explanation)
    {
        GUI.Label(new Rect(x, y, width, 22f), title, valueStyle);
        GUI.Label(new Rect(x, y + 29f, width, 76f), "NO DISPONIBLE EN LOS DATOS ACTUALES\n" + explanation, mutedStyle);
    }

    private Vector2 GetDemand(ElementSelectable element, PMCurveData curve)
    {
        if (element.data != null)
        {
            float[] f = UnityData.GetElementForces(UnityData.ActiveCombo, element.data.id);
            return f != null && f.Length >= 6
                ? new Vector2(f[0], Mathf.Sqrt(f[4] * f[4] + f[5] * f[5])) : Vector2.zero;
        }
        DemandRecord[] records = element.pmDemands != null && element.pmDemands.Length > 0
            ? element.pmDemands : curve.demands;
        if (records == null || records.Length == 0) return Vector2.zero;
        foreach (DemandRecord record in records)
            if (record != null && record.combo == UnityData.ActiveCombo) return new Vector2(record.P_kN, record.M_kN_m);
        foreach (DemandRecord record in records)
            if (record != null) return new Vector2(record.P_kN, record.M_kN_m);
        return Vector2.zero;
    }

    private string FormatCapacity(PMCurveData curve, Vector2 demand)
    {
        float ratio = UnityData.CapacityRatio(curve, demand.x, demand.y);
        return ratio >= UnityData.OutOfCurveRatio ? "fuera de curva" : ratio.ToString("0.###");
    }

    private static bool IsBeam(ElementSelectable e) => e != null && e.data != null &&
        (e.data.type == "viga" || e.data.type == "enlace");
    private static bool IsColumn(ElementSelectable e) => e != null && e.data != null && e.data.type == "columna";
    private static string ElementType(ElementSelectable e) => e.isWall ? "muro" : e.data != null ? e.data.type : "elemento";
    private static string ElementTag(ElementSelectable e) => e.isWall ? e.wallId.ToString() :
        e.data != null && !string.IsNullOrEmpty(e.data.elementTag) ? e.data.elementTag : e.data != null ? e.data.id.ToString() : "-";

    private static void DrawLine(Vector2 a, Vector2 b, Color color, float thickness)
    {
        if (Event.current.type != EventType.Repaint) return;
        Vector2 delta = b - a;
        if (delta.sqrMagnitude < .000001f) return;
        Matrix4x4 matrix = GUI.matrix;
        Color previous = GUI.color;
        GUI.color = color;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, a);
        GUI.DrawTexture(new Rect(a.x, a.y - thickness * .5f, delta.magnitude, thickness), Texture2D.whiteTexture);
        GUI.matrix = matrix;
        GUI.color = previous;
    }

    private void EnsureStyles()
    {
        if (panelStyle != null) return;
        background = new Texture2D(1, 1);
        background.SetPixel(0, 0, new Color(.045f, .06f, .09f, .97f));
        background.Apply();
        panelStyle = new GUIStyle(GUI.skin.box); panelStyle.normal.background = background;
        textStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        textStyle.normal.textColor = new Color(.88f, .92f, .96f);
        titleStyle = new GUIStyle(textStyle) { fontSize = 13, fontStyle = FontStyle.Bold };
        titleStyle.normal.textColor = new Color(.35f, .88f, 1f);
        valueStyle = new GUIStyle(textStyle) { fontStyle = FontStyle.Bold };
        valueStyle.normal.textColor = Color.white;
        mutedStyle = new GUIStyle(textStyle); mutedStyle.normal.textColor = new Color(.63f, .7f, .77f);
        successStyle = new GUIStyle(valueStyle) { fontSize = 13, wordWrap = true };
        successStyle.normal.textColor = new Color(.2f, 1f, .38f);
        failureStyle = new GUIStyle(successStyle);
        failureStyle.normal.textColor = new Color(1f, .32f, .24f);
    }
}
