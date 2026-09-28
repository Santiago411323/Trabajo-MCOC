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
    private int curvaturePresentation;
    private Vector2 resultsScroll;
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
            if (active.view == ResultView.MomentCurvature)
                return Mathf.Min(440f, Mathf.Max(340f, Screen.height * .42f));
            if (active.view == ResultView.StressStrain)
                return Mathf.Min(420f, Mathf.Max(330f, Screen.height * .40f));
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
        curvaturePresentation = 0;
        resultsScroll = Vector2.zero;
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
        // The Game view can be much shorter than the requested results panel
        // (for example when it is docked below the Scene view).  Keep the
        // complete panel on screen instead of letting its upper part start at
        // a negative Y coordinate.
        float bottom = Mathf.Min(baseRect.yMax, Screen.height - 4f);
        float height = Mathf.Min(ReservedHeight, Mathf.Max(120f, bottom - 4f));
        float width = expanded && (view == ResultView.Interaction || view == ResultView.MomentCurvature ||
            view == ResultView.StressStrain)
            ? Mathf.Min(940f, Screen.width - 32f)
            : baseRect.width;
        panelRect = new Rect(Screen.width - picker.panelOffset.x - width,
            Mathf.Max(4f, bottom - height), width, height);
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

        Rect viewport = new Rect(rect.x + 6f, rect.y + 35f, rect.width - 12f, rect.height - 41f);
        float contentHeight = ResultsContentHeight(element);
        float contentWidth = Mathf.Max(220f, viewport.width - 22f);
        resultsScroll = GUI.BeginScrollView(viewport, resultsScroll,
            new Rect(0f, 0f, contentWidth, contentHeight), false, true);

        float x = 8f;
        float y = 2f;
        float width = contentWidth - 16f;
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
                DrawStressStrain(element, x, y, width);
                break;
            case ResultView.MomentCurvature:
                DrawMomentCurvature(element, x, y, width);
                break;
            default:
                GUI.Label(new Rect(x, y + 8f, width, 42f),
                    "Elija un resultado. Ningún diagrama se abre automáticamente al seleccionar el elemento.", textStyle);
                break;
        }
        GUI.EndScrollView();
    }

    private float ResultsContentHeight(ElementSelectable element)
    {
        float optionHeight = IsBeam(element) ? 31f : 60f;
        switch (view)
        {
            case ResultView.Interaction: return optionHeight + 350f;
            case ResultView.Fibers: return optionHeight + 290f;
            case ResultView.MomentCurvature:
                return optionHeight + (curvaturePresentation == 0 ? 390f : 315f);
            case ResultView.StressStrain: return optionHeight + 430f;
            case ResultView.Forces:
            case ResultView.Deformed: return optionHeight + 190f;
            default: return optionHeight + 115f;
        }
    }

    private float DrawResultOptions(ElementSelectable element, float x, float y, float width)
    {
        if (IsBeam(element))
        {
            string[] labels = { "DIAGRAMAS DE ESFUERZOS", "MOMENTO–CURVATURA", "DEFORMADA" };
            ResultView[] views = { ResultView.Forces, ResultView.MomentCurvature, ResultView.Deformed };
            DrawGrid(x, y, width, labels, views, 3);
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
        resultsScroll = Vector2.zero;
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
        float availableHeight = 285f;
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
        string sectionId = !string.IsNullOrEmpty(element.pmSectionId) && UnityData.GetMaterial(element.pmSectionId) != null
            ? element.pmSectionId
            : element.data != null
                ? (!string.IsNullOrEmpty(element.data.sectionId) ? element.data.sectionId : element.data.seccion)
                : element.pmSectionId;
        SectionMaterialData material = UnityData.GetMaterial(sectionId);
        PMCurveData curve = UnityData.GetPMCurve(element.pmSectionId);
        float b = material != null ? material.b_m : curve != null ? curve.b_m : 0f;
        float h = material != null ? material.h_m : curve != null ? curve.h_m : 0f;
        int bars = material != null ? material.steelBars : curve != null ? curve.steelBars : 0;
        float diameter = material != null ? material.barDiameter_mm : curve != null ? curve.barDiameter_mm : 0f;
        GUI.Label(new Rect(x, y, width, 22f), "SECCIÓN DE FIBRAS — " + sectionId, valueStyle);
        if (material == null || b <= 0f || h <= 0f || material.topBars <= 0 || material.bottomBars <= 0)
        {
            GUI.Label(new Rect(x, y + 27f, width, 64f),
                $"Dimensiones exportadas: {b:0.###} × {h:0.###} m\n" +
                $"Armadura resumida: {bars} barras Ø{diameter:0.#} mm\n" +
                "No existe una distribución individual de barras exportada para esta sección.", textStyle);
            return;
        }

        float plotSize = 190f;
        Rect section = new Rect(x + 8f, y + 31f, plotSize, plotSize);
        DrawFiberSection(section, material);
        float infoX = section.xMax + 22f;
        float infoWidth = width - (infoX - x);
        GUI.Label(new Rect(infoX, y + 30f, infoWidth, 112f),
            $"Sección: {b:0.###} × {h:0.###} m\n" +
            $"Hormigón: {material.concreteFibersX} × {material.concreteFibersY} = {material.concreteFibersX * material.concreteFibersY} fibras\n" +
            $"Acero: {bars} barras Ø{diameter:0.#} mm\n" +
            $"As = {material.Ast_mm2:0.0} mm²   ρ = {material.rho_percent:0.###}%",
            textStyle);
        GUI.Label(new Rect(infoX, y + 147f, infoWidth, 76f),
            $"Superior: {material.topBars} barras\n" +
            $"Inferior: {material.bottomBars} barras\n" +
            $"Laterales: {material.sideBarsEach} por lado\n" +
            $"Recubrimiento al centro: {material.cover_mm:0.#} mm", valueStyle);
        GUI.Label(new Rect(x + 8f, section.yMax + 7f, width - 16f, 34f),
            "Azul: fibras de hormigón · Rojo: fibras de acero. Las barras de esquina pertenecen a las filas superior e inferior y no se duplican.", mutedStyle);
    }

    private void DrawFiberSection(Rect rect, SectionMaterialData material)
    {
        Color old = GUI.color;
        GUI.color = new Color(.18f, .26f, .34f, 1f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = old;

        int nx = Mathf.Max(1, material.concreteFibersX);
        int ny = Mathf.Max(1, material.concreteFibersY);
        Color grid = new Color(.30f, .55f, .72f, .46f);
        for (int i = 1; i < nx; i++)
        {
            float px = Mathf.Lerp(rect.x, rect.xMax, i / (float)nx);
            DrawLine(new Vector2(px, rect.y), new Vector2(px, rect.yMax), grid, .7f);
        }
        for (int i = 1; i < ny; i++)
        {
            float py = Mathf.Lerp(rect.y, rect.yMax, i / (float)ny);
            DrawLine(new Vector2(rect.x, py), new Vector2(rect.xMax, py), grid, .7f);
        }

        float coverX = material.b_m > 0f ? material.cover_mm / (material.b_m * 1000f) * rect.width : 0f;
        float coverY = material.h_m > 0f ? material.cover_mm / (material.h_m * 1000f) * rect.height : 0f;
        float left = rect.x + coverX;
        float right = rect.xMax - coverX;
        float top = rect.y + coverY;
        float bottom = rect.yMax - coverY;
        Color steel = new Color(.95f, .22f, .18f);
        DrawBarRow(left, right, top, material.topBars, steel);
        DrawBarRow(left, right, bottom, material.bottomBars, steel);
        for (int i = 1; i <= material.sideBarsEach; i++)
        {
            float py = Mathf.Lerp(top, bottom, i / (float)(material.sideBarsEach + 1));
            DrawMarker(new Vector2(left, py), steel, 8f);
            DrawMarker(new Vector2(right, py), steel, 8f);
        }
        DrawLine(new Vector2(rect.x, rect.y), new Vector2(rect.xMax, rect.y), Color.white, 1.4f);
        DrawLine(new Vector2(rect.xMax, rect.y), new Vector2(rect.xMax, rect.yMax), Color.white, 1.4f);
        DrawLine(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.x, rect.yMax), Color.white, 1.4f);
        DrawLine(new Vector2(rect.x, rect.yMax), new Vector2(rect.x, rect.y), Color.white, 1.4f);
    }

    private void DrawStressStrain(ElementSelectable element, float x, float y, float width)
    {
        string sectionId = !string.IsNullOrEmpty(element.pmSectionId) && UnityData.GetMaterial(element.pmSectionId) != null
            ? element.pmSectionId
            : element.data != null
                ? (!string.IsNullOrEmpty(element.data.sectionId) ? element.data.sectionId : element.data.seccion)
                : element.pmSectionId;
        SectionMaterialData material = UnityData.GetMaterial(sectionId);
        if (material == null || material.fc_MPa <= 0f || material.fy_MPa <= 0f)
        {
            DrawUnavailable(x, y, width, "TENSIÓN–DEFORMACIÓN",
                "La sección no contiene propiedades constitutivas suficientes de hormigón y acero.");
            return;
        }

        float fc = material.fc_MPa;
        float epsc0 = Mathf.Abs(material.epsc0) > .000001f ? Mathf.Abs(material.epsc0) : .002f;
        float epscu = Mathf.Abs(material.epscu) > epsc0 ? Mathf.Abs(material.epscu) : .003f;
        float fcu = Mathf.Abs(material.fcu_MPa) > .001f ? Mathf.Abs(material.fcu_MPa) : .85f * fc;
        float es = material.Es_MPa > 0f ? material.Es_MPa : 200000f;
        float fy = material.fy_MPa;
        float epsY = material.steelYieldStrain > 0f ? material.steelYieldStrain : fy / es;
        float hardening = material.steelHardeningRatio > 0f ? material.steelHardeningRatio : .01f;

        GUI.Label(new Rect(x, y, width, 22f),
            "CURVAS CONSTITUTIVAS — " + sectionId, titleStyle);
        float gap = 34f;
        float column = (width - gap) * .5f;
        float plotWidth = Mathf.Max(230f, column - 68f);
        float plotHeight = 215f;
        Rect concretePlot = new Rect(x + 53f, y + 48f, plotWidth, plotHeight);
        Rect steelPlot = new Rect(x + column + gap + 43f, y + 48f, plotWidth, plotHeight);

        DrawStressGrid(concretePlot, 0f, epscu * 1000f, 0f, fc * 1.12f, false);
        Vector2 previous = MapStressPoint(concretePlot, 0f, 0f, 0f, epscu * 1000f, 0f, fc * 1.12f);
        const int concreteSamples = 90;
        for (int i = 1; i <= concreteSamples; i++)
        {
            float strain = epscu * i / concreteSamples;
            float stress = Concrete01Stress(strain, fc, epsc0, fcu, epscu);
            Vector2 point = MapStressPoint(concretePlot, strain * 1000f, stress,
                0f, epscu * 1000f, 0f, fc * 1.12f);
            DrawLine(previous, point, strain <= epsc0
                ? new Color(.20f, .78f, 1f) : new Color(1f, .55f, .20f), 2.6f);
            previous = point;
        }
        Vector2 peakConcrete = MapStressPoint(concretePlot, epsc0 * 1000f, fc,
            0f, epscu * 1000f, 0f, fc * 1.12f);
        Vector2 ultimateConcrete = MapStressPoint(concretePlot, epscu * 1000f, fcu,
            0f, epscu * 1000f, 0f, fc * 1.12f);
        DrawMarker(peakConcrete, new Color(.25f, 1f, .52f), 8f);
        DrawMarker(ultimateConcrete, new Color(1f, .28f, .22f), 8f);
        GUI.Label(new Rect(peakConcrete.x - 43f, peakConcrete.y - 23f, 90f, 18f), "f'c", valueStyle);
        GUI.Label(new Rect(ultimateConcrete.x - 82f, ultimateConcrete.y + 4f, 82f, 18f), "εcu", valueStyle);

        float steelLimit = Mathf.Max(.008f, epsY * 3.8f);
        float steelMax = Steel01Stress(steelLimit, fy, es, hardening) * 1.12f;
        DrawStressGrid(steelPlot, -steelLimit * 1000f, steelLimit * 1000f,
            -steelMax, steelMax, true);
        previous = Vector2.zero;
        const int steelSamples = 120;
        for (int i = 0; i <= steelSamples; i++)
        {
            float strain = Mathf.Lerp(-steelLimit, steelLimit, i / (float)steelSamples);
            float stress = Steel01Stress(strain, fy, es, hardening);
            Vector2 point = MapStressPoint(steelPlot, strain * 1000f, stress,
                -steelLimit * 1000f, steelLimit * 1000f, -steelMax, steelMax);
            if (i > 0)
                DrawLine(previous, point, Mathf.Abs(strain) <= epsY
                    ? new Color(.25f, .82f, 1f) : new Color(1f, .64f, .16f), 2.6f);
            previous = point;
        }
        Vector2 positiveYield = MapStressPoint(steelPlot, epsY * 1000f, fy,
            -steelLimit * 1000f, steelLimit * 1000f, -steelMax, steelMax);
        Vector2 negativeYield = MapStressPoint(steelPlot, -epsY * 1000f, -fy,
            -steelLimit * 1000f, steelLimit * 1000f, -steelMax, steelMax);
        DrawMarker(positiveYield, new Color(.25f, 1f, .52f), 8f);
        DrawMarker(negativeYield, new Color(.25f, 1f, .52f), 8f);
        GUI.Label(new Rect(positiveYield.x + 5f, positiveYield.y - 19f, 90f, 18f), "+fy", valueStyle);
        GUI.Label(new Rect(negativeYield.x - 40f, negativeYield.y + 2f, 40f, 18f), "-fy", valueStyle);

        GUI.Label(new Rect(concretePlot.x, y + 25f, concretePlot.width, 20f),
            $"HORMIGÓN · {(string.IsNullOrEmpty(material.concreteModel) ? "Concrete01" : material.concreteModel)}", titleStyle);
        GUI.Label(new Rect(steelPlot.x, y + 25f, steelPlot.width, 20f),
            $"ACERO · {(string.IsNullOrEmpty(material.steelModel) ? "Steel01" : material.steelModel)}", titleStyle);
        GUI.Label(new Rect(concretePlot.x, concretePlot.yMax + 21f, concretePlot.width, 18f),
            "Deformación de compresión |εc| [‰]", mutedStyle);
        GUI.Label(new Rect(steelPlot.x, steelPlot.yMax + 21f, steelPlot.width, 18f),
            "Deformación εs [‰]  (− compresión / + tracción)", mutedStyle);
        GUI.Label(new Rect(x, concretePlot.y - 28f, 48f, 38f), "σc\n[MPa]", mutedStyle);
        GUI.Label(new Rect(x + column + gap, steelPlot.y - 28f, 42f, 38f), "σs\n[MPa]", mutedStyle);

        float infoY = concretePlot.yMax + 47f;
        GUI.Label(new Rect(x + 6f, infoY, column - 12f, 45f),
            $"f'c = {fc:0.###} MPa   εc0 = {epsc0:0.0000}\n" +
            $"fcu = {fcu:0.###} MPa   εcu = {epscu:0.0000}", textStyle);
        GUI.Label(new Rect(x + column + gap, infoY, column - 12f, 45f),
            $"fy = {fy:0.###} MPa   Es = {es:0} MPa\n" +
            $"εy = {epsY:0.000000}   b = {hardening:0.###}", textStyle);
        GUI.Label(new Rect(x + 6f, infoY + 48f, width - 12f, 34f),
            "Curvas de los materiales de la sección de fibras. No se representan agregados ni pasta de cemento por separado porque no existen como materiales independientes en el modelo.", mutedStyle);
    }

    private void DrawStressGrid(Rect plot, float xMin, float xMax, float yMin, float yMax, bool signed)
    {
        GUI.Box(new Rect(plot.x - 8f, plot.y - 8f, plot.width + 16f, plot.height + 16f), GUIContent.none);
        for (int i = 0; i <= 4; i++)
        {
            float t = i / 4f;
            float px = Mathf.Lerp(plot.x, plot.xMax, t);
            float py = Mathf.Lerp(plot.yMax, plot.y, t);
            DrawLine(new Vector2(px, plot.y), new Vector2(px, plot.yMax), new Color(.23f, .30f, .38f), 1f);
            DrawLine(new Vector2(plot.x, py), new Vector2(plot.xMax, py), new Color(.23f, .30f, .38f), 1f);
            GUI.Label(new Rect(px - 32f, plot.yMax + 3f, 64f, 17f),
                Mathf.Lerp(xMin, xMax, t).ToString("0.##"), mutedStyle);
            GUI.Label(new Rect(plot.x - 51f, py - 9f, 47f, 17f),
                Mathf.Lerp(yMin, yMax, t).ToString("0.#"), mutedStyle);
        }
        if (signed)
        {
            Vector2 zeroX0 = MapStressPoint(plot, 0f, yMin, xMin, xMax, yMin, yMax);
            Vector2 zeroX1 = MapStressPoint(plot, 0f, yMax, xMin, xMax, yMin, yMax);
            Vector2 zeroY0 = MapStressPoint(plot, xMin, 0f, xMin, xMax, yMin, yMax);
            Vector2 zeroY1 = MapStressPoint(plot, xMax, 0f, xMin, xMax, yMin, yMax);
            DrawLine(zeroX0, zeroX1, new Color(.70f, .78f, .84f), 1.4f);
            DrawLine(zeroY0, zeroY1, new Color(.70f, .78f, .84f), 1.4f);
        }
    }

    private static Vector2 MapStressPoint(Rect plot, float strain, float stress,
        float xMin, float xMax, float yMin, float yMax)
    {
        float tx = Mathf.InverseLerp(xMin, xMax, strain);
        float ty = Mathf.InverseLerp(yMin, yMax, stress);
        return new Vector2(Mathf.Lerp(plot.x, plot.xMax, tx), Mathf.Lerp(plot.yMax, plot.y, ty));
    }

    private static float Concrete01Stress(float compressionStrain, float fc, float epsc0, float fcu, float epscu)
    {
        if (compressionStrain <= 0f) return 0f;
        if (compressionStrain <= epsc0)
        {
            float ratio = compressionStrain / Mathf.Max(.0000001f, epsc0);
            return fc * (2f * ratio - ratio * ratio);
        }
        if (compressionStrain <= epscu)
        {
            float t = (compressionStrain - epsc0) / Mathf.Max(.0000001f, epscu - epsc0);
            return Mathf.Lerp(fc, fcu, t);
        }
        return fcu;
    }

    private static float Steel01Stress(float strain, float fy, float es, float hardening)
    {
        float epsY = fy / Mathf.Max(1f, es);
        if (strain > epsY) return fy + es * hardening * (strain - epsY);
        if (strain < -epsY) return -fy + es * hardening * (strain + epsY);
        return es * strain;
    }

    private static void DrawBarRow(float left, float right, float y, int count, Color color)
    {
        if (count <= 0) return;
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? .5f : i / (float)(count - 1);
            DrawMarker(new Vector2(Mathf.Lerp(left, right, t), y), color, 8f);
        }
    }

    private sealed class MomentCurvatureResult
    {
        public string sectionId;
        public float bMm, hMm, dMm, fc, fy, ec, es, asMm2, beta1;
        public float mCr, phiCr, mY, phiY, mN, phiN;
        public bool reinforcementExported, effectiveDepthExported;
    }

    private void DrawMomentCurvature(ElementSelectable element, float x, float y, float width)
    {
        PMCurveData fiberCurve = UnityData.GetPMCurve(element.pmSectionId);
        y = DrawMomentCurvaturePresentationSelector(x, y, width);
        if (curvaturePresentation > 0)
        {
            DrawFlexuralFailureAnimation(element, fiberCurve, x, y, width, curvaturePresentation);
            return;
        }
        if (IsColumn(element) && fiberCurve != null && fiberCurve.momentCurvature != null &&
            fiberCurve.momentCurvature.Length > 1)
        {
            DrawFiberMomentCurvature(element, fiberCurve, x, y, width);
            return;
        }
        if (!IsBeam(element))
        {
            DrawUnavailable(x, y, width, "MOMENTO–CURVATURA",
                "La formulación disponible corresponde a una viga rectangular simplemente armada en flexión pura. " +
                "No se aplica a columnas o muros porque requieren carga axial y distribución real de fibras.");
            return;
        }

        MomentCurvatureResult result;
        string error;
        if (!TryBuildMomentCurvature(element, out result, out error))
        {
            DrawUnavailable(x, y, width, "MOMENTO–CURVATURA", error);
            return;
        }

        float demandMoment = CurrentBeamMoment(element);
        float phiMax = Mathf.Max(result.phiN * 1.08f, .000001f);
        float momentMax = Mathf.Max(result.mN, result.mY, result.mCr, .001f) * 1.12f;
        float plotColumn = Mathf.Max(430f, width * .58f);
        float plotHeight = 255f;
        Rect plot = new Rect(x + 58f, y + 34f, plotColumn - 82f, plotHeight);
        GUI.Box(new Rect(plot.x - 9f, plot.y - 9f, plot.width + 18f, plot.height + 18f), GUIContent.none);
        DrawMomentCurvatureGrid(plot, phiMax, momentMax);

        Vector2 previous = MapMomentCurvature(plot, 0f, 0f, phiMax, momentMax);
        const int sampleCount = 72;
        for (int i = 1; i <= sampleCount; i++)
        {
            float phi = result.phiN * i / sampleCount;
            float moment = EvaluateMomentCurvature(result, phi);
            Vector2 point = MapMomentCurvature(plot, phi, moment, phiMax, momentMax);
            Color phaseColor = phi <= result.phiCr
                ? new Color(.25f, .78f, 1f)
                : phi <= result.phiY ? new Color(1f, .73f, .18f) : new Color(1f, .34f, .28f);
            DrawLine(previous, point, phaseColor, 2.5f);
            previous = point;
        }

        DrawMomentCurvaturePoint(plot, result.phiCr, result.mCr, phiMax, momentMax, "1  Mcr", new Color(.25f, .78f, 1f));
        DrawMomentCurvaturePoint(plot, result.phiY, result.mY, phiMax, momentMax, "2  My", new Color(1f, .73f, .18f));
        DrawMomentCurvaturePoint(plot, result.phiN, result.mN, phiMax, momentMax, "3  Mn", new Color(1f, .34f, .28f));

        if (demandMoment > 0f)
        {
            float demandPhi = CurvatureForMoment(result, demandMoment);
            float plottedMoment = Mathf.Min(demandMoment, result.mN);
            Vector2 demand = MapMomentCurvature(plot, Mathf.Min(demandPhi, result.phiN), plottedMoment, phiMax, momentMax);
            DrawMarker(demand, new Color(.25f, 1f, .45f), 9f);
            GUI.Label(new Rect(demand.x + 7f, demand.y - 10f, 88f, 18f), "DEMANDA", valueStyle);
        }

        GUI.Label(new Rect(plot.x, y, plot.width, 22f),
            "MOMENTO–CURVATURA · " + result.sectionId, titleStyle);
        GUI.Label(new Rect(plot.x, plot.yMax + 23f, plot.width, 19f), "Curvatura Φ [1/m]", mutedStyle);
        GUI.Label(new Rect(x, plot.y - 27f, 54f, 38f), "M\n[kN·m]", mutedStyle);

        float detailX = x + plotColumn + 10f;
        float detailWidth = width - plotColumn - 10f;
        GUI.Label(new Rect(detailX, y, detailWidth, 22f), "ESTADOS CARACTERÍSTICOS", titleStyle);
        GUI.Label(new Rect(detailX, y + 29f, detailWidth, 76f),
            $"1  Agrietamiento:  Mcr = {result.mCr:0.###} kN·m   Φcr = {result.phiCr:0.000000} 1/m\n" +
            $"2  Fluencia:          My = {result.mY:0.###} kN·m   Φy = {result.phiY:0.000000} 1/m\n" +
            $"3  Último:             Mn = {result.mN:0.###} kN·m   Φn = {result.phiN:0.000000} 1/m",
            textStyle);

        float ratio = result.mN > .0001f ? demandMoment / result.mN : 0f;
        GUI.Label(new Rect(detailX, y + 112f, detailWidth, 22f), "DEMANDA ACTUAL — EJE FUERTE My", titleStyle);
        GUI.Label(new Rect(detailX, y + 139f, detailWidth, 52f),
            $"|My|max = {demandMoment:0.###} kN·m\nM/Mn = {ratio:0.###}   {(ratio <= 1f ? "[ DENTRO ]" : "[ SUPERA Mn ]")}",
            ratio <= 1f ? successStyle : failureStyle);

        string reinforcementText = result.reinforcementExported
            ? $"As exportada = {result.asMm2:0.#} mm²"
            : $"As adoptada = As,min = {result.asMm2:0.#} mm²";
        string depthText = result.effectiveDepthExported
            ? $"d exportado = {result.dMm:0.#} mm"
            : $"d estimado = 0,90h = {result.dMm:0.#} mm";
        GUI.Label(new Rect(detailX, y + 198f, detailWidth, 72f),
            $"b×h = {result.bMm:0.#}×{result.hMm:0.#} mm   f'c={result.fc:0.#} MPa   fy={result.fy:0.#} MPa\n" +
            reinforcementText + "   ·   " + depthText + $"\nEc={result.ec:0.#} MPa   Es={result.es:0} MPa   β1={result.beta1:0.###}", textStyle);

        GUI.Label(new Rect(detailX, y + 276f, detailWidth, 76f),
            result.reinforcementExported && result.effectiveDepthExported
                ? "Curva teórica de sección con propiedades exportadas. La transición entre estados es una interpolación visual; no es una historia de fibras OpenSees."
                : "ESTIMACIÓN: el modelo no exporta la armadura longitudinal completa de la viga. Se usa As,min y/o d≈0,9h. " +
                  "La curva no debe presentarse como ensayo de fibras OpenSees.", mutedStyle);
    }

    private float DrawMomentCurvaturePresentationSelector(float x, float y, float width)
    {
        string[] modes = { "CURVA", "FASE 1", "FASE 2", "FASE 3", "FALLA" };
        int next = GUI.Toolbar(new Rect(x, y, width, 24f), curvaturePresentation, modes);
        if (next != curvaturePresentation)
        {
            curvaturePresentation = next;
            resultsScroll = Vector2.zero;
        }
        return y + 31f;
    }

    private void DrawFlexuralFailureAnimation(ElementSelectable element, PMCurveData fiberCurve,
        float x, float y, float width, int phase)
    {
        float plotColumn = Mathf.Max(430f, width * .64f);
        float animationHeight = 235f;
        Rect animationRect = new Rect(x + 4f, y + 4f, plotColumn - 18f, animationHeight);
        GUI.Box(animationRect, GUIContent.none);

        float cycle = Mathf.PingPong(Time.time / 2.0f, 1f);
        float smooth = cycle * cycle * (3f - 2f * cycle);
        float stageStart = phase == 1 ? 0f : phase == 2 ? .24f : phase == 3 ? .56f : .84f;
        float stageEnd = phase == 1 ? .24f : phase == 2 ? .56f : phase == 3 ? .84f : 1f;
        float state = Mathf.Lerp(stageStart, stageEnd, smooth);
        DrawAnimatedFlexuralMember(animationRect, state, phase);

        float infoX = x + plotColumn + 8f;
        float infoWidth = width - plotColumn - 8f;
        string phaseTitle;
        string behavior;
        string materials;
        Color phaseColor;
        switch (phase)
        {
            case 1:
                phaseTitle = "FASE 1 — SIN AGRIETAR";
                behavior = "La sección completa resiste. Las deformaciones y esfuerzos se mantienen aproximadamente lineales.";
                materials = "HORMIGÓN: compresión y tracción\nACERO: elástico, trabajando en conjunto";
                phaseColor = new Color(.25f, .78f, 1f);
                break;
            case 2:
                phaseTitle = "FASE 2 — SECCIÓN AGRIETADA";
                behavior = "Las grietas nacen en la cara traccionada y avanzan hacia el eje neutro. Disminuye la rigidez flexural.";
                materials = "HORMIGÓN: compresión; tracción despreciada\nACERO: tracción elástica creciente";
                phaseColor = new Color(1f, .75f, .18f);
                break;
            case 3:
                phaseTitle = "FASE 3 — FLUENCIA";
                behavior = "El acero de tracción alcanza la fluencia. La curvatura y la abertura de grietas crecen rápidamente.";
                materials = "HORMIGÓN: compresión no lineal\nACERO: en fluencia, grandes deformaciones";
                phaseColor = new Color(1f, .42f, .18f);
                break;
            default:
                phaseTitle = "FALLA — ESTADO LÍMITE";
                behavior = "Se alcanza el límite analizado: gran curvatura, grietas profundas y daño en la zona comprimida.";
                materials = "HORMIGÓN: aplastamiento en compresión\nACERO: fluencia en la zona traccionada";
                phaseColor = new Color(1f, .22f, .20f);
                break;
        }

        Color old = GUI.color;
        GUI.color = phaseColor;
        GUI.Label(new Rect(infoX, y + 2f, infoWidth, 20f), phaseTitle, titleStyle);
        GUI.color = old;
        GUI.Label(new Rect(infoX, y + 24f, infoWidth, 35f), behavior, textStyle);
        GUI.Label(new Rect(infoX, y + 62f, infoWidth, 34f), materials, valueStyle);

        string values = BuildFlexuralPhaseValues(element, fiberCurve, phase);
        GUI.Label(new Rect(infoX, y + 99f, infoWidth, 55f), values, textStyle);
        GUI.Label(new Rect(animationRect.x + 8f, animationRect.yMax - 22f,
            animationRect.width - 16f, 18f),
            "Animación cualitativa · los valores estructurales no se alteran", mutedStyle);
    }

    private string BuildFlexuralPhaseValues(ElementSelectable element, PMCurveData fiberCurve, int phase)
    {
        if (fiberCurve != null && fiberCurve.momentCurvature != null && fiberCurve.momentCurvature.Length > 1)
        {
            MomentCurvaturePoint yield = fiberCurve.momentCurvatureFirstYield;
            MomentCurvaturePoint maximum = null;
            foreach (MomentCurvaturePoint point in fiberCurve.momentCurvature)
                if (point != null && (maximum == null || point.M_kN_m > maximum.M_kN_m)) maximum = point;

            if (phase == 1)
                return "Estado previo al agrietamiento\nCurva de fibras: tramo inicial";
            if (phase == 2)
                return yield != null
                    ? $"Antes de fluencia\nΦ < {yield.phi_1_m:0.000000} 1/m"
                    : "Acero previo a fluencia";
            if (phase == 3 && yield != null)
                return $"Inicio de fluencia\nM = {yield.M_kN_m:0.###} kN·m\nΦ = {yield.phi_1_m:0.000000} 1/m";
            if (maximum != null)
                return $"Máximo analizado\nM = {maximum.M_kN_m:0.###} kN·m\nΦ = {maximum.phi_1_m:0.000000} 1/m";
        }

        MomentCurvatureResult result;
        string error;
        if (TryBuildMomentCurvature(element, out result, out error))
        {
            if (phase == 1) return $"Mcr = {result.mCr:0.###} kN·m\nΦcr = {result.phiCr:0.000000} 1/m";
            if (phase == 2) return $"Mcr → My\nMy = {result.mY:0.###} kN·m";
            if (phase == 3) return $"My = {result.mY:0.###} kN·m\nΦy = {result.phiY:0.000000} 1/m";
            return $"Mn = {result.mN:0.###} kN·m\nΦn = {result.phiN:0.000000} 1/m";
        }
        return "Valores característicos no disponibles";
    }

    private void DrawAnimatedFlexuralMember(Rect area, float state, int phase)
    {
        Rect member = new Rect(area.x + 34f, area.y + 35f,
            Mathf.Max(180f, area.width - 68f), Mathf.Max(64f, area.height - 86f));
        const int slices = 72;
        float bend = Mathf.Lerp(0f, Mathf.Min(28f, member.height * .25f), state);
        Color concrete = Color.Lerp(new Color(.60f, .64f, .68f), new Color(.47f, .50f, .53f), state);
        Color old = GUI.color;

        for (int i = 0; i < slices; i++)
        {
            float t0 = i / (float)slices;
            float t1 = (i + 1f) / slices;
            float centerT = (t0 + t1) * .5f;
            float deflection = bend * 4f * centerT * (1f - centerT);
            float sliceX = Mathf.Lerp(member.x, member.xMax, t0);
            float sliceW = member.width / slices + 1f;
            GUI.color = concrete;
            GUI.DrawTexture(new Rect(sliceX, member.y + deflection, sliceW, member.height), Texture2D.whiteTexture);

            GUI.color = new Color(.25f, .62f, .92f, .18f + .26f * state);
            GUI.DrawTexture(new Rect(sliceX, member.y + deflection, sliceW, member.height * .22f), Texture2D.whiteTexture);
        }
        GUI.color = old;

        Vector2 previousTop = Vector2.zero;
        Vector2 previousBottom = Vector2.zero;
        Vector2 previousSteel = Vector2.zero;
        for (int i = 0; i <= slices; i++)
        {
            float t = i / (float)slices;
            float px = Mathf.Lerp(member.x, member.xMax, t);
            float dy = bend * 4f * t * (1f - t);
            Vector2 top = new Vector2(px, member.y + dy);
            Vector2 bottom = new Vector2(px, member.y + member.height + dy);
            Vector2 steel = new Vector2(px, member.y + member.height * .82f + dy);
            if (i > 0)
            {
                DrawLine(previousTop, top, new Color(.82f, .88f, .92f), 1.4f);
                DrawLine(previousBottom, bottom, new Color(.82f, .88f, .92f), 1.4f);
                Color steelColor = phase >= 3 ? new Color(1f, .43f, .12f) : new Color(.95f, .72f, .18f);
                DrawLine(previousSteel, steel, steelColor, phase >= 3 ? 3.2f : 2.2f);
            }
            previousTop = top;
            previousBottom = bottom;
            previousSteel = steel;
        }

        int crackCount = phase == 1 ? 1 : phase == 2 ? 7 : phase == 3 ? 11 : 14;
        float crackActivation = Mathf.InverseLerp(.18f, 1f, state);
        float crackLimit = phase == 1 ? .10f : phase == 2 ? .52f : phase == 3 ? .76f : .92f;
        for (int i = 0; i < crackCount; i++)
        {
            float t = (i + 1f) / (crackCount + 1f);
            float localDelay = i / (float)Mathf.Max(1, crackCount) * .24f;
            float growth = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(localDelay, localDelay + .48f, crackActivation));
            float individual = .72f + .28f * Mathf.Abs(Mathf.Sin((i + 2) * 1.73f));
            DrawAnimatedCrack(member, bend, t, crackLimit * growth * individual, i);
        }

        if (phase == 4)
        {
            float damage = Mathf.InverseLerp(.84f, 1f, state);
            for (int i = 0; i < 7; i++)
            {
                float t = .34f + i * .055f;
                float px = Mathf.Lerp(member.x, member.xMax, t);
                float dy = bend * 4f * t * (1f - t);
                DrawLine(new Vector2(px - 7f, member.y + dy + 3f),
                    new Vector2(px + 6f, member.y + dy + 13f * damage), new Color(1f, .25f, .18f), 1.8f);
            }
        }

        GUI.Label(new Rect(member.x, area.y + 8f, member.width, 20f),
            phase == 1 ? "SECCIÓN ÍNTEGRA" : phase == 2 ? "GRIETAS EN TRACCIÓN" :
            phase == 3 ? "ACERO EN FLUENCIA" : "APLASTAMIENTO + GRANDES GRIETAS", titleStyle);
        GUI.Label(new Rect(member.x + 5f, member.y + 5f, 160f, 18f), "HORMIGÓN EN COMPRESIÓN", mutedStyle);
        GUI.Label(new Rect(member.x + 5f, member.yMax - 20f, 170f, 18f), "ACERO EN TRACCIÓN", mutedStyle);
    }

    private static void DrawAnimatedCrack(Rect member, float bend, float t, float heightFraction, int seed)
    {
        if (heightFraction <= .005f) return;
        float baseX = Mathf.Lerp(member.x, member.xMax, t);
        float dy = bend * 4f * t * (1f - t);
        float bottom = member.y + member.height + dy;
        float crackHeight = member.height * heightFraction;
        Vector2 previous = new Vector2(baseX, bottom);
        const int segments = 5;
        for (int j = 1; j <= segments; j++)
        {
            float progress = j / (float)segments;
            float wobble = Mathf.Sin(seed * 2.17f + j * 2.31f) * (2.5f + 2f * progress);
            Vector2 next = new Vector2(baseX + wobble, bottom - crackHeight * progress);
            DrawLine(previous, next, new Color(.08f, .09f, .11f), 1.5f + heightFraction);
            previous = next;
        }
    }

    private void DrawFiberMomentCurvature(ElementSelectable element, PMCurveData curve, float x, float y, float width)
    {
        float phiMax = 0f;
        float momentMax = 0f;
        MomentCurvaturePoint maximum = curve.momentCurvature[0];
        foreach (MomentCurvaturePoint point in curve.momentCurvature)
        {
            if (point == null) continue;
            phiMax = Mathf.Max(phiMax, point.phi_1_m);
            if (point.M_kN_m > momentMax) { momentMax = point.M_kN_m; maximum = point; }
        }
        phiMax = Mathf.Max(phiMax * 1.03f, .000001f);
        float plotMomentMax = Mathf.Max(momentMax * 1.10f, .001f);
        float plotColumn = Mathf.Max(430f, width * .58f);
        float plotHeight = 255f;
        Rect plot = new Rect(x + 58f, y + 34f, plotColumn - 82f, plotHeight);
        GUI.Box(new Rect(plot.x - 9f, plot.y - 9f, plot.width + 18f, plot.height + 18f), GUIContent.none);
        DrawMomentCurvatureGrid(plot, phiMax, plotMomentMax);

        MomentCurvaturePoint previousPoint = null;
        Vector2 previous = Vector2.zero;
        int stride = Mathf.Max(1, curve.momentCurvature.Length / 180);
        for (int i = 0; i < curve.momentCurvature.Length; i += stride)
        {
            MomentCurvaturePoint point = curve.momentCurvature[i];
            if (point == null) continue;
            Vector2 mapped = MapMomentCurvature(plot, point.phi_1_m, point.M_kN_m, phiMax, plotMomentMax);
            if (previousPoint != null)
            {
                Color color = point.steel_yielded ? new Color(1f, .38f, .22f) : new Color(.22f, .76f, 1f);
                DrawLine(previous, mapped, color, 2.4f);
            }
            previousPoint = point;
            previous = mapped;
        }
        MomentCurvaturePoint last = curve.momentCurvature[curve.momentCurvature.Length - 1];
        if (last != null && previousPoint != last)
            DrawLine(previous, MapMomentCurvature(plot, last.phi_1_m, last.M_kN_m, phiMax, plotMomentMax),
                new Color(1f, .38f, .22f), 2.4f);

        MomentCurvaturePoint yield = curve.momentCurvatureFirstYield;
        if (yield != null)
            DrawMomentCurvaturePoint(plot, yield.phi_1_m, yield.M_kN_m, phiMax, plotMomentMax,
                "FLUENCIA", new Color(1f, .76f, .18f));
        DrawMomentCurvaturePoint(plot, maximum.phi_1_m, maximum.M_kN_m, phiMax, plotMomentMax,
            "MÁX", new Color(1f, .25f, .25f));

        float demandMoment = CurrentColumnMoment(element);
        MomentCurvaturePoint closest = ClosestMomentCurvaturePoint(curve.momentCurvature, demandMoment);
        if (closest != null && demandMoment > 0f)
        {
            Vector2 demand = MapMomentCurvature(plot, closest.phi_1_m, Mathf.Min(demandMoment, momentMax), phiMax, plotMomentMax);
            DrawMarker(demand, new Color(.25f, 1f, .45f), 9f);
            GUI.Label(new Rect(demand.x + 6f, demand.y - 10f, 88f, 18f), "DEMANDA", valueStyle);
        }

        GUI.Label(new Rect(plot.x, y, plot.width, 22f), "MOMENTO–CURVATURA FIBER · P ≈ 0", titleStyle);
        GUI.Label(new Rect(plot.x, plot.yMax + 23f, plot.width, 19f), "Curvatura Φ [1/m]", mutedStyle);
        GUI.Label(new Rect(x, plot.y - 27f, 54f, 38f), "M\n[kN·m]", mutedStyle);

        SectionMaterialData material = UnityData.GetMaterial(curve.sectionId);
        float detailX = x + plotColumn + 10f;
        float detailWidth = width - plotColumn - 10f;
        GUI.Label(new Rect(detailX, y, detailWidth, 22f), "RESPUESTA DE LA SECCIÓN", titleStyle);
        GUI.Label(new Rect(detailX, y + 29f, detailWidth, 86f),
            $"Primera fluencia: Φ={(yield != null ? yield.phi_1_m : 0f):0.000000} 1/m\n" +
            $"M fluencia: {(yield != null ? yield.M_kN_m : 0f):0.###} kN·m\n" +
            $"M máximo analizado: {maximum.M_kN_m:0.###} kN·m\n" +
            $"Φ en M máximo: {maximum.phi_1_m:0.000000} 1/m", valueStyle);
        float ratio = momentMax > .0001f ? demandMoment / momentMax : 0f;
        GUI.Label(new Rect(detailX, y + 122f, detailWidth, 60f),
            $"Demanda actual |M|: {demandMoment:0.###} kN·m\n" +
            $"M/Mmáx = {ratio:0.###}   {(ratio <= 1f ? "[ DENTRO ]" : "[ SUPERA CURVA ]")}",
            ratio <= 1f ? successStyle : failureStyle);
        GUI.Label(new Rect(detailX, y + 190f, detailWidth, 72f),
            material != null
                ? $"Sección {material.b_m:0.###}×{material.h_m:0.###} m\n18 barras Ø25 · As={material.Ast_mm2:0.0} mm²\n5 superior · 5 inferior · 4 por lado"
                : "Sección COL70/70_FIBER · 18 barras Ø25", textStyle);
        GUI.Label(new Rect(detailX, y + 270f, detailWidth, 82f),
            "Curva calculada fibra a fibra con compatibilidad de deformaciones, Concrete01/Steel01 y P objetivo aproximadamente nulo. " +
            "La demanda del elemento puede incluir carga axial; para ella la verificación completa sigue siendo el diagrama P–M.", mutedStyle);
    }

    private static float CurrentColumnMoment(ElementSelectable element)
    {
        if (element == null || element.data == null) return 0f;
        float[] forces = UnityData.GetElementForces(UnityData.ActiveCombo, element.data.id);
        if (forces == null || forces.Length < 12) return 0f;
        float atI = Mathf.Sqrt(forces[4] * forces[4] + forces[5] * forces[5]);
        float atJ = Mathf.Sqrt(forces[10] * forces[10] + forces[11] * forces[11]);
        return Mathf.Max(atI, atJ);
    }

    private static MomentCurvaturePoint ClosestMomentCurvaturePoint(MomentCurvaturePoint[] points, float moment)
    {
        MomentCurvaturePoint best = null;
        float bestDistance = float.MaxValue;
        foreach (MomentCurvaturePoint point in points)
        {
            if (point == null) continue;
            float distance = Mathf.Abs(point.M_kN_m - moment);
            if (distance < bestDistance) { best = point; bestDistance = distance; }
        }
        return best;
    }

    private bool TryBuildMomentCurvature(ElementSelectable element, out MomentCurvatureResult result, out string error)
    {
        result = null;
        error = "No hay propiedades suficientes para construir la curva.";
        if (element == null || element.data == null) return false;

        string sectionId = !string.IsNullOrEmpty(element.data.sectionId)
            ? element.data.sectionId : element.data.seccion;
        SectionMaterialData material = UnityData.GetMaterial(sectionId);
        float bM = material != null && material.b_m > 0f ? material.b_m : element.data.width_m;
        float hM = material != null && material.h_m > 0f ? material.h_m : element.data.height_m;
        float fc = material != null ? material.fc_MPa : 0f;
        float fy = material != null ? material.fy_MPa : 0f;
        float ec = material != null ? material.E_MPa : 0f;
        if (bM <= 0f || hM <= 0f || fc <= 0f || fy <= 0f)
        {
            error = "Faltan dimensiones, f'c o fy en los metadatos de la sección " + sectionId + ".";
            return false;
        }
        if (ec <= 0f) ec = 4700f * Mathf.Sqrt(fc);

        float b = bM * 1000f;
        float h = hM * 1000f;
        bool depthExported = material != null && material.effectiveDepth_mm > 0f;
        float d = depthExported ? material.effectiveDepth_mm : .90f * h;
        d = Mathf.Clamp(d, .55f * h, .96f * h);
        bool reinforcementExported = material != null && material.Ast_mm2 > 0f;
        float asMinA = .25f * Mathf.Sqrt(fc) / fy * b * d;
        float asMinB = 1.4f / fy * b * d;
        float asMm2 = reinforcementExported ? material.Ast_mm2 : Mathf.Max(asMinA, asMinB);
        float es = material != null && material.Es_MPa > 0f ? material.Es_MPa : 200000f;

        float fr = .63f * Mathf.Sqrt(fc);
        float ig = b * h * h * h / 12f;
        float mCrNmm = fr * ig / (h * .5f);
        float phiCr = mCrNmm / (ec * ig) * 1000f;
        float n = es / ec;
        float rho = asMm2 / (b * d);
        float rhoN = rho * n;
        float cElastic = d * (-rhoN + Mathf.Sqrt(rhoN * rhoN + 2f * rhoN));
        float iCr = b * cElastic * cElastic * cElastic / 3f + n * asMm2 * (d - cElastic) * (d - cElastic);
        float mYNmm = fy * iCr / Mathf.Max(.001f, n * (d - cElastic));
        float phiY = mYNmm / (ec * iCr) * 1000f;
        float beta1 = Mathf.Clamp(.85f - .05f * Mathf.Max(0f, fc - 28f) / 7f, .65f, .85f);
        float a = asMm2 * fy / (.85f * fc * b);
        a = Mathf.Clamp(a, 1f, d * .95f);
        float cUltimate = a / beta1;
        float mNNmm = asMm2 * fy * (d - a * .5f);
        float phiN = .003f / Mathf.Max(1f, cUltimate) * 1000f;

        result = new MomentCurvatureResult {
            sectionId = sectionId, bMm = b, hMm = h, dMm = d, fc = fc, fy = fy, ec = ec, es = es,
            asMm2 = asMm2, beta1 = beta1, mCr = mCrNmm / 1e6f, phiCr = phiCr,
            mY = mYNmm / 1e6f, phiY = Mathf.Max(phiY, phiCr * 1.01f),
            mN = mNNmm / 1e6f, phiN = Mathf.Max(phiN, phiY * 1.05f),
            reinforcementExported = reinforcementExported, effectiveDepthExported = depthExported
        };
        return true;
    }

    private static float EvaluateMomentCurvature(MomentCurvatureResult r, float phi)
    {
        if (phi <= r.phiCr)
            return r.mCr * phi / Mathf.Max(.0000001f, r.phiCr);
        if (phi <= r.phiY)
        {
            float t = (phi - r.phiCr) / Mathf.Max(.0000001f, r.phiY - r.phiCr);
            return Mathf.Lerp(r.mCr, r.mY, t);
        }
        float u = Mathf.Clamp01((phi - r.phiY) / Mathf.Max(.0000001f, r.phiN - r.phiY));
        return Mathf.Lerp(r.mY, r.mN, 1f - (1f - u) * (1f - u));
    }

    private static float CurvatureForMoment(MomentCurvatureResult r, float moment)
    {
        if (moment <= r.mCr)
            return r.phiCr * moment / Mathf.Max(.0001f, r.mCr);
        if (moment <= r.mY)
            return Mathf.Lerp(r.phiCr, r.phiY, (moment - r.mCr) / Mathf.Max(.0001f, r.mY - r.mCr));
        if (moment <= r.mN)
        {
            float normalized = (moment - r.mY) / Mathf.Max(.0001f, r.mN - r.mY);
            float u = 1f - Mathf.Sqrt(Mathf.Max(0f, 1f - normalized));
            return Mathf.Lerp(r.phiY, r.phiN, u);
        }
        return r.phiN;
    }

    private static float CurrentBeamMoment(ElementSelectable element)
    {
        if (element == null || element.data == null) return 0f;
        float[] forces = UnityData.GetElementForces(UnityData.ActiveCombo, element.data.id);
        if (forces == null || forces.Length < 11) return 0f;
        return Mathf.Max(Mathf.Abs(forces[4]), Mathf.Abs(forces[10]));
    }

    private void DrawMomentCurvatureGrid(Rect plot, float phiMax, float momentMax)
    {
        for (int i = 0; i <= 4; i++)
        {
            float t = i / 4f;
            float px = Mathf.Lerp(plot.x, plot.xMax, t);
            float py = Mathf.Lerp(plot.yMax, plot.y, t);
            DrawLine(new Vector2(px, plot.y), new Vector2(px, plot.yMax), new Color(.23f, .3f, .38f), 1f);
            DrawLine(new Vector2(plot.x, py), new Vector2(plot.xMax, py), new Color(.23f, .3f, .38f), 1f);
            GUI.Label(new Rect(px - 36f, plot.yMax + 3f, 72f, 17f),
                (phiMax * t).ToString("0.0000"), mutedStyle);
            GUI.Label(new Rect(plot.x - 55f, py - 9f, 50f, 17f),
                (momentMax * t).ToString("0.#"), mutedStyle);
        }
    }

    private static Vector2 MapMomentCurvature(Rect plot, float phi, float moment, float phiMax, float momentMax)
    {
        return new Vector2(plot.x + phi / Mathf.Max(.0000001f, phiMax) * plot.width,
            plot.yMax - moment / Mathf.Max(.0001f, momentMax) * plot.height);
    }

    private void DrawMomentCurvaturePoint(Rect plot, float phi, float moment, float phiMax, float momentMax,
        string label, Color color)
    {
        Vector2 point = MapMomentCurvature(plot, phi, moment, phiMax, momentMax);
        DrawMarker(point, color, 8f);
        GUI.Label(new Rect(point.x + 6f, point.y - 18f, 80f, 18f), label, valueStyle);
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
