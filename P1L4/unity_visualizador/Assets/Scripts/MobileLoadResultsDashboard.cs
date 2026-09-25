using System;
using System.Collections.Generic;
using UnityEngine;

// Results-only companion for MobileLoadController. It never advances, places or
// transfers the person; it reads the existing controller and presents traceable data.
public class MobileLoadResultsDashboard : MonoBehaviour
{
    private MobileLoadController mobile;
    private Rect headerRect;
    private Rect detailRect;
    private Rect summaryRect;
    private Vector2 detailScroll;
    private float shownLoad;
    private float shownTransferred;
    private float shownError;
    private string shownSlab = "—";
    private bool showDetails = true;
    private bool showTributaryAreas;
    private bool showLoadTransfer;
    private bool colorStructure = true;
    private bool showGhostTrail = true;
    private bool showCompletion;
    private bool completionSeen;
    private bool wasMoving;
    private int responseMode;
    private readonly string[] responseModes = { "CURRENT", "ENVELOPE" };
    private int activeResult = 4;
    private readonly string[] resultNames = { "Axial N", "Shear Vy", "Shear Vz", "Moment My", "Moment Mz", "Deformed Shape" };
    private readonly string[] resultCodes = { "N", "Vy", "Vz", "My", "Mz", "Deformed" };
    private readonly int[] envelopeForceComponents = { 0, 1, 2, 4, 5 };
    private readonly List<LiveMember> liveMembers = new List<LiveMember>();
    private readonly Dictionary<int, ElementSelectable> membersById = new Dictionary<int, ElementSelectable>();
    private readonly Dictionary<Renderer, Color> structuralBaseColors = new Dictionary<Renderer, Color>();
    private readonly Dictionary<Renderer, Color> structuralTargetColors = new Dictionary<Renderer, Color>();
    private ElementSelectable criticalMember;
    private float criticalValue;
    private float currentMinimum;
    private float currentMaximum;
    private float shownCritical;
    private int lastResponseSequence = int.MinValue;
    private int lastResult = -1;
    private int lastResponseMode = -1;
    private int lastEnvelopeSequence = int.MinValue;
    private Vector2 lastRoutePosition;
    private bool hasRoutePosition;
    private float routeDistance;
    private float routeClock;
    private float lastRouteRecordTime;
    private float maximumConservationError;
    private readonly HashSet<string> slabsCrossed = new HashSet<string>();
    private readonly Dictionary<int, MemberEnvelope> envelopes = new Dictionary<int, MemberEnvelope>();
    private readonly List<GraphPoint> selectedGraph = new List<GraphPoint>();
    private int graphElementId = int.MinValue;
    private int graphResult = -1;
    private GameObject tributaryRoot;
    private GameObject transferRoot;
    private GameObject trailRoot;
    private GameObject maxMarker;
    private readonly List<Vector3> routePoints = new List<Vector3>();
    private string routeStartSlab;
    private Vector2 routeStartPosition;
    private Vector2 routeDirection = Vector2.right;
    private string visualSlab = "";
    private int visualResponseSequence = int.MinValue;
    private GUIStyle rank;

    private class LiveMember
    {
        public ElementSelectable element;
        public float value;
    }

    private class Extreme
    {
        public bool valid;
        public float value, time, distance;
        public Vector2 position;
        public string slab;
    }

    private class MemberEnvelope
    {
        public readonly Extreme[] minimum = NewExtremes();
        public readonly Extreme[] maximum = NewExtremes();
        private static Extreme[] NewExtremes()
        {
            var values = new Extreme[5];
            for (int i = 0; i < values.Length; i++) values[i] = new Extreme();
            return values;
        }
    }

    private struct GraphPoint
    {
        public float distance, value;
        public GraphPoint(float d, float v) { distance = d; value = v; }
    }

    private GUIStyle panel;
    private GUIStyle title;
    private GUIStyle eyebrow;
    private GUIStyle metric;
    private GUIStyle label;
    private GUIStyle muted;
    private GUIStyle pass;
    private GUIStyle fail;
    private GUIStyle section;
    private Texture2D panelTexture;
    private Texture2D cardTexture;
    private Texture2D passTexture;
    private Texture2D failTexture;

    private static MobileLoadResultsDashboard active;

    public static bool BlocksPointer()
    {
        if (active == null || !active.isActiveAndEnabled || active.mobile == null || !active.mobile.visible) return false;
        Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        return active.headerRect.Contains(mouse) || (active.showDetails && active.detailRect.Contains(mouse)) ||
            (active.showCompletion && active.summaryRect.Contains(mouse));
    }

    private void OnEnable() { active = this; }
    private void OnDisable() { if (active == this) active = null; }

    private void Update()
    {
        if (mobile == null) mobile = MobileLoadController.Instance ?? GetComponent<MobileLoadController>();
        if (mobile == null) return;
        float blend = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
        float targetLoad = mobile.loadKN;
        float targetTransferred = mobile.CurrentResponse != null && mobile.CurrentResponse.ok ? mobile.CurrentResponse.transferred : 0f;
        float targetError = mobile.CurrentResponse != null && mobile.CurrentResponse.ok ? mobile.CurrentResponse.error : 0f;
        shownLoad = Mathf.Lerp(shownLoad, targetLoad, blend);
        shownTransferred = Mathf.Lerp(shownTransferred, targetTransferred, blend);
        shownError = Mathf.Lerp(shownError, targetError, blend);
        if (mobile.CurrentSlab != null) shownSlab = mobile.CurrentSlab.id;
        if (mobile.IsMoving && !wasMoving)
        {
            routeStartSlab = mobile.CurrentSlab != null ? mobile.CurrentSlab.id : null;
            routeStartPosition = mobile.CurrentPosition;
            if (mobile.CurrentWalkDirection.sqrMagnitude > .5f) routeDirection = mobile.CurrentWalkDirection;
        }
        wasMoving = mobile.IsMoving;
        int responseSequence = mobile.CurrentResponse != null && mobile.CurrentResponse.ok ? mobile.CurrentResponse.seq : int.MinValue;
        if (responseSequence != lastEnvelopeSequence && responseSequence != int.MinValue)
        {
            lastEnvelopeSequence = responseSequence;
            RecordEnvelopeFrame();
        }
        if (responseSequence != lastResponseSequence || activeResult != lastResult || responseMode != lastResponseMode)
        {
            lastResponseSequence = responseSequence;
            lastResult = activeResult;
            lastResponseMode = responseMode;
            RebuildLiveResults();
        }
        shownCritical = Mathf.Lerp(shownCritical, criticalValue, blend);
        UpdateStructuralColors(blend);
        UpdateLoadPathVisuals(responseSequence);
        if (mobile.RouteComplete && mobile.HasCurrentResults && !completionSeen) { completionSeen = true; showCompletion = true; }
        if (!mobile.RouteComplete) completionSeen = false;
    }

    private void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (mobile == null) mobile = MobileLoadController.Instance ?? GetComponent<MobileLoadController>();
        if (mobile == null || !mobile.visible) return;
        EnsureStyles();

        float headerWidth = Mathf.Clamp(Screen.width - 40f, 760f, 1180f);
        headerRect = PanelLayout.Apply("MobileLiveHeader", new Rect(Screen.width - headerWidth - 14f, 8f, headerWidth, 92f));
        GUI.Box(headerRect, GUIContent.none, panel);
        DrawHeader(headerRect);

        if (showDetails)
        {
            detailRect = PanelLayout.Apply("MobileLiveDetails",
                new Rect(Screen.width - 394f, 112f, 380f, Mathf.Min(610f, Screen.height - 128f)));
            GUI.Box(detailRect, GUIContent.none, panel);
            DrawDetails(detailRect);
        }
        if (showCompletion) DrawCompletionSummary();
    }

    private void DrawHeader(Rect rect)
    {
        GUI.Label(new Rect(rect.x + 16f, rect.y + 8f, 150f, 18f), "LIVE ANALYSIS", eyebrow);
        string state = mobile.IsMoving ? "● LIVE" : mobile.IsPanelReady() ? "● UPDATED" : "● READY";
        GUI.Label(new Rect(rect.xMax - 116f, rect.y + 8f, 100f, 18f), state,
            mobile.IsMoving ? pass : muted);

        float y = rect.y + 31f;
        float width = (rect.width - 32f) / 6f;
        HeaderMetric(new Rect(rect.x + 16f, y, width, 48f), "MOVING LOAD", $"{shownLoad:0.00} kN");
        HeaderMetric(new Rect(rect.x + 16f + width, y, width, 48f), "CURRENT SLAB", shownSlab);
        HeaderMetric(new Rect(rect.x + 16f + width * 2f, y, width, 48f), "ACTIVE RESULT", resultNames[activeResult]);
        string critical = criticalMember == null ? "—" : MemberTag(criticalMember) + "  " + shownCritical.ToString("0.##");
        HeaderMetric(new Rect(rect.x + 16f + width * 3f, y, width, 48f), "CRITICAL NOW", critical);
        HeaderMetric(new Rect(rect.x + 16f + width * 4f, y, width, 48f), "CURRENT MAX", $"{criticalValue:0.##} {ActiveUnits}");
        Extreme routeExtreme = GlobalExtreme(activeResult);
        HeaderMetric(new Rect(rect.x + 16f + width * 5f, y, width, 48f), "MAX ON ROUTE",
            routeExtreme == null ? "—" : $"{routeExtreme.value:0.##} {ActiveUnits}");

        if (GUI.Button(new Rect(rect.xMax - 30f, rect.y + 4f, 22f, 22f), showDetails ? "−" : "+"))
            showDetails = !showDetails;
    }

    private void HeaderMetric(Rect rect, string caption, string value)
    {
        GUI.Box(rect, GUIContent.none, new GUIStyle(panel) { normal = { background = cardTexture } });
        GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 15f), caption, muted);
        GUI.Label(new Rect(rect.x + 8f, rect.y + 19f, rect.width - 16f, 25f), value, metric);
    }

    private void DrawDetails(Rect rect)
    {
        GUI.Label(new Rect(rect.x + 14f, rect.y + 8f, rect.width - 28f, 22f), "LOAD PATH · " + responseModes[responseMode], title);
        detailScroll = GUI.BeginScrollView(new Rect(rect.x + 10f, rect.y + 36f, rect.width - 20f, rect.height - 46f),
            detailScroll, new Rect(0f, 0f, rect.width - 40f, 1420f));
        float y = 0f;
        SlabData slab = mobile.CurrentSlab;
        SlabLoadMetadata metadata = mobile.CurrentSlabMetadata;
        if (slab == null)
        {
            GUI.Label(new Rect(4f, y, rect.width - 54f, 54f), "Seleccione una losa para comenzar el análisis visual.", label);
            GUI.EndScrollView();
            return;
        }

        GUI.Label(new Rect(4f, y, rect.width - 54f, 20f), "CURRENT SLAB", section); y += 24f;
        float slabArea = Mathf.Abs((slab.x1 - slab.x0) * (slab.y1 - slab.y0));
        float tributaryArea = 0f;
        var areas = AggregateAreas(metadata, ref tributaryArea);
        DataRow(ref y, "Slab ID", slab.id);
        DataRow(ref y, "Floor", slab.nivel);
        DataRow(ref y, "Area", $"{slabArea:0.###} m²");
        DataRow(ref y, "Total tributary area", $"{tributaryArea:0.###} m²");
        y += 5f;

        GUI.Label(new Rect(4f, y, rect.width - 54f, 20f), "SUPPORTING BEAMS", section); y += 23f;
        foreach (var pair in areas)
        {
            float percentage = slabArea > 1e-6f ? pair.Value / slabArea * 100f : 0f;
            DataRow(ref y, "B" + pair.Key, $"{pair.Value:0.###} m²   {percentage:0.0}%");
        }
        DataRow(ref y, "Sum tributary areas", $"{tributaryArea:0.###} m²");
        float areaError = Mathf.Abs(tributaryArea - slabArea);
        StatusRow(ref y, "AREA CONSERVATION", areaError <= Mathf.Max(1e-4f, slabArea * 1e-4f), $"error {areaError:0.0000} m²");
        showTributaryAreas = GUI.Toggle(new Rect(4f, y, 250f, 22f), showTributaryAreas, " Show Tributary Areas"); y += 27f;

        GUI.Label(new Rect(4f, y, rect.width - 54f, 20f), "LOAD TRANSFER", section); y += 23f;
        MobileLoadController.Response response = mobile.CurrentResponse;
        DataRow(ref y, "Person load", $"{shownLoad:0.0000} kN");
        DataRow(ref y, "Transferred load", $"{shownTransferred:0.0000} kN");
        DataRow(ref y, "Conservation error", $"{shownError:0.000000} kN");
        bool loadPass = response != null && response.ok && response.error <= Mathf.Max(1e-6f, response.p * 1e-6f);
        StatusRow(ref y, "LOAD CONSERVATION", loadPass, response == null ? "waiting for OpenSees" : loadPass ? "balanced" : "review transfer");
        if (response != null && response.receivers != null)
        {
            var loads = AggregateLoads(response.receivers);
            foreach (var pair in loads)
            {
                float percentage = response.p > 1e-8f ? pair.Value / response.p * 100f : 0f;
                DataRow(ref y, "B" + pair.Key, $"{pair.Value:0.0000} kN   {percentage:0.0}%");
            }
        }
        showLoadTransfer = GUI.Toggle(new Rect(4f, y, 250f, 22f), showLoadTransfer, " Show Load Transfer"); y += 28f;
        GUI.Label(new Rect(4f, y, rect.width - 54f, 20f), "STRUCTURAL RESPONSE", section); y += 23f;
        int nextMode = GUI.Toolbar(new Rect(4f, y, 324f, 26f), responseMode, responseModes);
        if (nextMode != responseMode) { responseMode = nextMode; lastResponseMode = -1; }
        y += 31f;
        int nextResult = GUI.SelectionGrid(new Rect(4f, y, 324f, 50f), activeResult, resultNames, 3);
        if (nextResult != activeResult)
        {
            int previousResult = activeResult; activeResult = nextResult; lastResult = -1;
            ApplyResultSelection(previousResult);
        }
        y += 53f;
        colorStructure = GUI.Toggle(new Rect(4f, y, 250f, 22f), colorStructure, " Color Structure By Result"); y += 25f;
        DataRow(ref y, "Scale min", $"{currentMinimum:0.###} {ActiveUnits}");
        DataRow(ref y, "Scale max", $"{currentMaximum:0.###} {ActiveUnits}");
        if (criticalMember != null)
        {
            GUI.Box(new Rect(4f, y, 324f, 72f), GUIContent.none, new GUIStyle(panel) { normal = { background = cardTexture } });
            GUI.Label(new Rect(12f, y + 6f, 120f, 18f), "CRITICAL NOW", eyebrow);
            GUI.Label(new Rect(12f, y + 25f, 180f, 22f), MemberTag(criticalMember), title);
            GUI.Label(new Rect(188f, y + 25f, 130f, 22f), $"{criticalValue:0.###} {ActiveUnits}", metric);
            GUI.Label(new Rect(12f, y + 49f, 300f, 18f), MemberType(criticalMember) + " · " + resultCodes[activeResult], muted);
            y += 78f;
        }
        GUI.BeginGroup(new Rect(4f, y, 324f, 28f));
        if (GUI.Button(new Rect(0f, 0f, 204f, 26f), "GO TO CRITICAL POSITION")) GoToCriticalPosition();
        bool nextTrail = GUI.Toggle(new Rect(210f, 2f, 114f, 22f), showGhostTrail, " Ghost trail");
        if (nextTrail != showGhostTrail) { showGhostTrail = nextTrail; UpdateTrailVisual(); }
        GUI.EndGroup(); y += 33f;
        GUI.Label(new Rect(4f, y, rect.width - 54f, 20f), "TOP 5 — CURRENT", section); y += 22f;
        int count = Mathf.Min(5, liveMembers.Count);
        for (int i = 0; i < count; i++)
        {
            LiveMember member = liveMembers[i];
            if (GUI.Button(new Rect(4f, y, 324f, 25f), $"{i + 1}   {MemberTag(member.element),-12}  {member.value,10:0.###} {ActiveUnits}", rank))
                SelectAndFocus(member.element);
            y += 27f;
        }
        y += 4f;
        DrawSelectedMember(ref y);
        GUI.Label(new Rect(4f, y, rect.width - 54f, 52f),
            "Losas = superficies de carga. El análisis móvil actual entrega respuesta incremental de elementos frame. No se inventan esfuerzos shell ni resultados de muro no exportados por OpenSees.", muted);
        GUI.EndScrollView();
    }

    private void DrawCompletionSummary()
    {
        summaryRect = PanelLayout.Apply("MobileAnalysisComplete",
            new Rect(Mathf.Max(18f, (Screen.width - 650f) * .5f), Mathf.Max(112f, (Screen.height - 480f) * .5f), 650f, 480f));
        GUI.Box(summaryRect, GUIContent.none, panel);
        GUI.Label(new Rect(summaryRect.x + 22f, summaryRect.y + 16f, 520f, 26f), "MOVING LOAD ANALYSIS COMPLETE", title);
        if (GUI.Button(new Rect(summaryRect.xMax - 42f, summaryRect.y + 12f, 28f, 25f), "×")) showCompletion = false;
        float x = summaryRect.x + 22f, y = summaryRect.y + 55f;
        GUI.Label(new Rect(x, y, 190f, 18f), "DISTANCE TRAVELLED", muted);
        GUI.Label(new Rect(x, y + 18f, 190f, 30f), $"{routeDistance:0.00} m", title);
        GUI.Label(new Rect(x + 205f, y, 170f, 18f), "SLABS CROSSED", muted);
        GUI.Label(new Rect(x + 205f, y + 18f, 170f, 30f), slabsCrossed.Count.ToString(), title);
        GUI.Label(new Rect(x + 390f, y, 210f, 18f), "MAX CONSERVATION ERROR", muted);
        bool conservationPass = maximumConservationError <= Mathf.Max(1e-6f, shownLoad * 1e-6f);
        GUI.Label(new Rect(x + 390f, y + 18f, 220f, 30f), $"{maximumConservationError:0.000000} kN · {(conservationPass ? "PASS" : "FAIL")}", conservationPass ? pass : fail);
        y += 66f;
        GUI.Label(new Rect(x, y, 606f, 20f), "ROUTE ENVELOPE", section); y += 27f;
        DrawSummaryResult(ref y, 3, "Maximum Moment My");
        DrawSummaryResult(ref y, 4, "Maximum Moment Mz");
        DrawSummaryResult(ref y, 1, "Maximum Shear Vy");
        DrawSummaryResult(ref y, 2, "Maximum Shear Vz");
        DrawSummaryResult(ref y, 0, "Maximum Axial N");
        y += 8f;
        if (GUI.Button(new Rect(x, y, 142f, 34f), "VIEW MAX MOMENT")) ViewLargestOf(3, 4);
        if (GUI.Button(new Rect(x + 151f, y, 142f, 34f), "VIEW MAX SHEAR")) ViewLargestOf(1, 2);
        if (GUI.Button(new Rect(x + 302f, y, 142f, 34f), "VIEW MAX AXIAL")) ViewSummaryComponent(0);
        if (GUI.Button(new Rect(x + 453f, y, 142f, 34f), "REPLAY")) ReplayRoute();
    }

    private void DrawSummaryResult(ref float y, int component, string caption)
    {
        int id = GlobalExtremeElement(component, out Extreme extreme);
        string tag = id < 0 ? "—" : membersById.TryGetValue(id, out ElementSelectable element) ? MemberTag(element) : "E" + id;
        string value = extreme == null ? "—" : $"{extreme.value:0.###} {(component >= 3 ? "kN·m" : "kN")}";
        string where = extreme == null ? "" : $"{extreme.slab} · ({extreme.position.x:0.00}, {extreme.position.y:0.00}) m";
        GUI.Label(new Rect(summaryRect.x + 22f, y, 210f, 21f), caption, label);
        GUI.Label(new Rect(summaryRect.x + 235f, y, 160f, 21f), tag + "  " + value, metric);
        GUI.Label(new Rect(summaryRect.x + 405f, y, 220f, 21f), where, muted);
        y += 27f;
    }

    private void ViewLargestOf(int first, int second)
    {
        Extreme a = GlobalExtreme(first), b = GlobalExtreme(second);
        ViewSummaryComponent(b != null && (a == null || Mathf.Abs(b.value) > Mathf.Abs(a.value)) ? second : first);
    }

    private void ViewSummaryComponent(int component)
    {
        int previousResult = activeResult; activeResult = component; lastResult = -1; showCompletion = false;
        ApplyResultSelection(previousResult);
        RebuildLiveResults(); UpdateMaximumMarker(); GoToCriticalPosition();
    }

    private void ApplyResultSelection(int previousResult)
    {
        selectedGraph.Clear(); graphResult = activeResult;
        ElementSelectable selected = FindObjectOfType<ElementPicker>()?.Selected;
        graphElementId = selected != null && selected.data != null ? selected.data.id : int.MinValue;
        DiagramController diagrams = GetComponent<DiagramController>();
        if (activeResult == 5)
        {
            if (diagrams != null) diagrams.SetResultMode("Deformada");
            return;
        }
        if (previousResult == 5 && diagrams != null) diagrams.SetResultMode("None");
        FindObjectOfType<SelectedBeamDiagramPanel>()?.SelectDiagramByCode(resultCodes[activeResult]);
        UpdateMaximumMarker();
    }

    private void ReplayRoute()
    {
        string slabId = routeStartSlab; Vector2 point = routeStartPosition; Vector2 direction = routeDirection;
        ResetRouteHistory();
        showCompletion = false; completionSeen = false;
        mobile.ReplayFrom(slabId, point, direction);
    }

    private void ResetRouteHistory()
    {
        envelopes.Clear(); selectedGraph.Clear(); slabsCrossed.Clear(); routePoints.Clear();
        routeDistance = routeClock = maximumConservationError = 0f; lastRouteRecordTime = 0f; hasRoutePosition = false;
        routeStartSlab = null; wasMoving = false;
        graphElementId = int.MinValue; graphResult = -1; lastEnvelopeSequence = int.MinValue;
        if (trailRoot != null) { LineRenderer line = trailRoot.GetComponent<LineRenderer>(); if (line != null) line.positionCount = 0; }
        if (maxMarker != null) Destroy(maxMarker);
    }

    private Dictionary<int, float> AggregateAreas(SlabLoadMetadata metadata, ref float total)
    {
        var result = new Dictionary<int, float>();
        if (metadata == null || metadata.edges == null) return result;
        foreach (SlabLoadEdge edge in metadata.edges)
        {
            total += edge.area;
            if (edge.beams == null || edge.beams.Length == 0) continue;
            float share = edge.area / edge.beams.Length;
            foreach (int beam in edge.beams)
                result[beam] = result.TryGetValue(beam, out float value) ? value + share : share;
        }
        return result;
    }

    private Dictionary<int, float> AggregateLoads(MobileLoadController.Receiver[] receivers)
    {
        var result = new Dictionary<int, float>();
        foreach (var receiver in receivers)
            result[receiver.beam] = result.TryGetValue(receiver.beam, out float value) ? value + receiver.load : receiver.load;
        return result;
    }

    private void RebuildLiveResults()
    {
        liveMembers.Clear();
        membersById.Clear();
        criticalMember = null;
        criticalValue = 0f;
        currentMinimum = float.PositiveInfinity;
        currentMaximum = float.NegativeInfinity;
        foreach (ElementSelectable element in FindObjectsOfType<ElementSelectable>())
        {
            if (element == null || element.data == null ||
                (element.data.type != "viga" && element.data.type != "columna" && element.data.type != "enlace")) continue;
            membersById[element.data.id] = element;
            float value = EvaluateDisplayedMember(element);
            if (float.IsNaN(value) || float.IsInfinity(value)) continue;
            liveMembers.Add(new LiveMember { element = element, value = value });
            currentMinimum = Mathf.Min(currentMinimum, value);
            currentMaximum = Mathf.Max(currentMaximum, value);
            if (criticalMember == null || Mathf.Abs(value) > Mathf.Abs(criticalValue))
            {
                criticalMember = element;
                criticalValue = value;
            }
        }
        liveMembers.Sort((a, b) => Mathf.Abs(b.value).CompareTo(Mathf.Abs(a.value)));
        if (liveMembers.Count == 0) currentMinimum = currentMaximum = 0f;
        structuralTargetColors.Clear();
        float bound = Mathf.Max(Mathf.Abs(currentMinimum), Mathf.Abs(currentMaximum), 1e-6f);
        foreach (LiveMember member in liveMembers)
        {
            Renderer renderer = member.element.GetComponent<Renderer>();
            if (renderer == null) continue;
            if (!structuralBaseColors.ContainsKey(renderer)) structuralBaseColors[renderer] = renderer.material.color;
            float intensity = Mathf.Clamp01(Mathf.Abs(member.value) / bound);
            Color target = Color.Lerp(new Color(.12f, .22f, .30f), new Color(1f, .34f, .16f), intensity);
            if (member.element == criticalMember) target = new Color(1f, .78f, .12f);
            structuralTargetColors[renderer] = target;
        }
    }

    private float EvaluateDisplayedMember(ElementSelectable element)
    {
        if (responseMode == 0 || activeResult == 5) return EvaluateMember(element);
        if (!envelopes.TryGetValue(element.data.id, out MemberEnvelope envelope)) return float.NaN;
        Extreme minimum = envelope.minimum[activeResult];
        Extreme maximum = envelope.maximum[activeResult];
        if (!minimum.valid) return float.NaN;
        return Mathf.Abs(maximum.value) >= Mathf.Abs(minimum.value) ? maximum.value : minimum.value;
    }

    private float EvaluateMember(ElementSelectable element)
    {
        if (activeResult == 5)
        {
            Vector3 ui = UnityData.GetNodeDisplacement(UnityData.ActiveCombo, element.data.nodeI);
            Vector3 uj = UnityData.GetNodeDisplacement(UnityData.ActiveCombo, element.data.nodeJ);
            return Mathf.Max(ui.magnitude, uj.magnitude) * 1000f;
        }
        return EvaluateComponent(element, activeResult);
    }

    private float EvaluateComponent(ElementSelectable element, int resultIndex)
    {
        if (resultIndex == 5)
        {
            Vector3 ui = UnityData.GetNodeDisplacement(UnityData.ActiveCombo, element.data.nodeI);
            Vector3 uj = UnityData.GetNodeDisplacement(UnityData.ActiveCombo, element.data.nodeJ);
            return Mathf.Max(ui.magnitude, uj.magnitude) * 1000f;
        }
        int component = resultIndex <= 2 ? resultIndex : resultIndex == 3 ? 4 : 5;
        float selected = 0f;
        bool found = false;
        for (int i = 0; i <= 12; i++)
        {
            if (!UnityData.TryGetSectionForces(element.data.id, UnityData.ActiveCombo, i / 12f, out FrameSectionForces forces)) continue;
            float value = forces.Component(component);
            if (!found || Mathf.Abs(value) > Mathf.Abs(selected)) { selected = value; found = true; }
        }
        return found ? selected : float.NaN;
    }

    private void RecordEnvelopeFrame()
    {
        Vector2 at = mobile.CurrentPosition;
        if (hasRoutePosition) routeDistance += Vector2.Distance(lastRoutePosition, at);
        lastRoutePosition = at;
        hasRoutePosition = true;
        float now = Time.unscaledTime;
        if (lastRouteRecordTime > 0f) routeClock += Mathf.Max(0f, now - lastRouteRecordTime);
        lastRouteRecordTime = now;
        string slabId = mobile.CurrentSlab != null ? mobile.CurrentSlab.id : "—";
        slabsCrossed.Add(slabId);
        if (mobile.CurrentResponse != null) maximumConservationError = Mathf.Max(maximumConservationError, mobile.CurrentResponse.error);
        routeDirection = mobile.CurrentWalkDirection.sqrMagnitude > .5f ? mobile.CurrentWalkDirection : routeDirection;
        Vector3 world = new Vector3(at.x, mobile.CurrentSlab != null ? mobile.CurrentSlab.z + .08f : .08f, at.y);
        if (routePoints.Count == 0)
        {
            if (string.IsNullOrEmpty(routeStartSlab)) { routeStartSlab = slabId; routeStartPosition = at; }
            routePoints.Add(world);
        }
        else if (Vector3.Distance(routePoints[routePoints.Count - 1], world) > .04f) routePoints.Add(world);
        if (routePoints.Count > 1800) routePoints.RemoveAt(0);

        ElementSelectable selected = FindObjectOfType<ElementPicker>()?.Selected;
        foreach (ElementSelectable element in FindObjectsOfType<ElementSelectable>())
        {
            if (!IsFrame(element)) continue;
            if (!envelopes.TryGetValue(element.data.id, out MemberEnvelope envelope))
            {
                envelope = new MemberEnvelope();
                envelopes[element.data.id] = envelope;
            }
            for (int station = 0; station <= 12; station++)
            {
                if (!UnityData.TryGetSectionForces(element.data.id, UnityData.ActiveCombo, station / 12f, out FrameSectionForces forces)) continue;
                for (int component = 0; component < 5; component++)
                {
                    float value = forces.Component(envelopeForceComponents[component]);
                    if (float.IsNaN(value) || float.IsInfinity(value)) continue;
                    UpdateExtreme(envelope.minimum[component], value, false, at, slabId);
                    UpdateExtreme(envelope.maximum[component], value, true, at, slabId);
                }
            }
        }

        int selectedId = selected != null && selected.data != null ? selected.data.id : int.MinValue;
        if (selectedId != graphElementId || activeResult != graphResult)
        {
            selectedGraph.Clear(); graphElementId = selectedId; graphResult = activeResult;
        }
        if (selected != null && IsFrame(selected))
        {
            float value = EvaluateMember(selected);
            if (!float.IsNaN(value)) selectedGraph.Add(new GraphPoint(routeDistance, value));
            if (selectedGraph.Count > 600) selectedGraph.RemoveAt(0);
        }
        UpdateTrailVisual();
        UpdateMaximumMarker();
    }

    private void UpdateExtreme(Extreme extreme, float value, bool maximum, Vector2 at, string slabId)
    {
        if (extreme.valid && (maximum ? value <= extreme.value : value >= extreme.value)) return;
        extreme.valid = true; extreme.value = value; extreme.position = at; extreme.slab = slabId;
        extreme.time = routeClock; extreme.distance = routeDistance;
    }

    private bool IsFrame(ElementSelectable element) => element != null && element.data != null &&
        (element.data.type == "viga" || element.data.type == "columna" || element.data.type == "enlace");

    private void DrawSelectedMember(ref float y)
    {
        ElementSelectable selected = FindObjectOfType<ElementPicker>()?.Selected;
        GUI.Label(new Rect(4f, y, 324f, 20f), "SELECTED MEMBER", section); y += 23f;
        if (!IsFrame(selected))
        {
            GUI.Label(new Rect(4f, y, 324f, 34f), "Seleccione una viga o columna para ver su respuesta y su gráfico vivo.", muted); y += 40f;
            return;
        }
        float current = EvaluateMember(selected);
        DataRow(ref y, "ID", MemberTag(selected));
        DataRow(ref y, "Type", MemberType(selected));
        DataRow(ref y, "Section", string.IsNullOrEmpty(selected.data.sectionId) ? "—" : selected.data.sectionId);
        DataRow(ref y, "Current " + resultCodes[activeResult], $"{current:0.###} {ActiveUnits}");
        if (activeResult < 5 && envelopes.TryGetValue(selected.data.id, out MemberEnvelope envelope) && envelope.minimum[activeResult].valid)
        {
            DataRow(ref y, "Maximum encountered", $"{envelope.maximum[activeResult].value:0.###} {ActiveUnits}");
            DataRow(ref y, "Minimum encountered", $"{envelope.minimum[activeResult].value:0.###} {ActiveUnits}");
        }
        DrawLiveGraph(new Rect(4f, y, 324f, 145f), selected); y += 152f;
    }

    private void DrawLiveGraph(Rect rect, ElementSelectable selected)
    {
        GUI.Box(rect, GUIContent.none, new GUIStyle(panel) { normal = { background = cardTexture } });
        GUI.Label(new Rect(rect.x + 8f, rect.y + 5f, rect.width - 16f, 18f), $"{MemberTag(selected)} · {resultCodes[activeResult]} [{ActiveUnits}]", eyebrow);
        Rect plot = new Rect(rect.x + 28f, rect.y + 29f, rect.width - 40f, rect.height - 49f);
        GUI.DrawTexture(new Rect(plot.x, plot.yMax - 1f, plot.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(plot.x, plot.y, 1f, plot.height), Texture2D.whiteTexture);
        if (selectedGraph.Count < 2) { GUI.Label(plot, "Esperando recorrido…", muted); return; }
        float min = float.PositiveInfinity, max = float.NegativeInfinity, maxDistance = Mathf.Max(.001f, selectedGraph[selectedGraph.Count - 1].distance);
        foreach (GraphPoint point in selectedGraph) { min = Mathf.Min(min, point.value); max = Mathf.Max(max, point.value); }
        if (Mathf.Abs(max - min) < 1e-6f) { min -= 1f; max += 1f; }
        for (int i = 1; i < selectedGraph.Count; i++)
        {
            GraphPoint a = selectedGraph[i - 1], b = selectedGraph[i];
            Vector2 pa = new Vector2(plot.x + a.distance / maxDistance * plot.width, plot.yMax - (a.value - min) / (max - min) * plot.height);
            Vector2 pb = new Vector2(plot.x + b.distance / maxDistance * plot.width, plot.yMax - (b.value - min) / (max - min) * plot.height);
            DrawGuiLine(pa, pb, new Color(.2f, .82f, 1f), 2f);
        }
        GraphPoint last = selectedGraph[selectedGraph.Count - 1];
        GUI.Label(new Rect(plot.x, plot.y - 1f, 100f, 16f), $"MAX {max:0.##}", muted);
        GUI.Label(new Rect(plot.x, plot.yMax - 15f, 100f, 16f), $"MIN {min:0.##}", muted);
        GUI.Label(new Rect(plot.xMax - 100f, plot.yMax + 1f, 100f, 16f), $"{last.distance:0.0} m", muted);
    }

    private void DrawGuiLine(Vector2 a, Vector2 b, Color color, float width)
    {
        Matrix4x4 matrix = GUI.matrix; Color old = GUI.color;
        GUI.color = color;
        float angle = Vector2.SignedAngle(Vector2.right, b - a);
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.DrawTexture(new Rect(a.x, a.y - width * .5f, Vector2.Distance(a, b), width), Texture2D.whiteTexture);
        GUI.matrix = matrix; GUI.color = old;
    }

    private Extreme GlobalExtreme(int component)
    {
        if (component >= 5) return null;
        Extreme selected = null;
        foreach (MemberEnvelope envelope in envelopes.Values)
        {
            Extreme minimum = envelope.minimum[component], maximum = envelope.maximum[component];
            if (minimum.valid && (selected == null || Mathf.Abs(minimum.value) > Mathf.Abs(selected.value))) selected = minimum;
            if (maximum.valid && (selected == null || Mathf.Abs(maximum.value) > Mathf.Abs(selected.value))) selected = maximum;
        }
        return selected;
    }

    private int GlobalExtremeElement(int component, out Extreme selected)
    {
        selected = null; int elementId = -1;
        if (component >= 5) return elementId;
        foreach (var pair in envelopes)
        {
            Extreme minimum = pair.Value.minimum[component], maximum = pair.Value.maximum[component];
            if (minimum.valid && (selected == null || Mathf.Abs(minimum.value) > Mathf.Abs(selected.value))) { selected = minimum; elementId = pair.Key; }
            if (maximum.valid && (selected == null || Mathf.Abs(maximum.value) > Mathf.Abs(selected.value))) { selected = maximum; elementId = pair.Key; }
        }
        return elementId;
    }

    private void GoToCriticalPosition()
    {
        int elementId = GlobalExtremeElement(activeResult, out Extreme extreme);
        if (elementId < 0 || extreme == null) return;
        mobile.PauseFromResults();
        if (!mobile.MoveToRecordedPosition(extreme.slab, extreme.position)) return;
        if (!membersById.TryGetValue(elementId, out ElementSelectable element))
            foreach (ElementSelectable candidate in FindObjectsOfType<ElementSelectable>())
                if (candidate.data != null && candidate.data.id == elementId) { element = candidate; break; }
        if (element != null)
        {
            SelectAndFocus(element);
            FindObjectOfType<SelectedBeamDiagramPanel>()?.SelectDiagramByCode(resultCodes[activeResult]);
            DiagramController diagrams = GetComponent<DiagramController>();
            if (diagrams != null)
            {
                diagrams.SetResultMode(activeResult == 0 ? "Axial" : activeResult <= 2 ? "Corte" : activeResult <= 4 ? "Momento" : "Deformada");
                diagrams.RefreshDiagramForElement(element);
            }
        }
        UpdateMaximumMarker();
    }

    private void UpdateTrailVisual()
    {
        if (trailRoot == null)
        {
            trailRoot = new GameObject("Mobile route ghost trail"); trailRoot.transform.SetParent(transform, false);
            var line = trailRoot.AddComponent<LineRenderer>(); line.useWorldSpace = true;
            line.startWidth = .035f; line.endWidth = .02f; line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = new Color(.2f, .8f, 1f, .52f); line.endColor = new Color(.2f, .8f, 1f, .12f);
        }
        trailRoot.SetActive(showGhostTrail);
        if (!showGhostTrail) return;
        LineRenderer renderer = trailRoot.GetComponent<LineRenderer>();
        renderer.positionCount = routePoints.Count; if (routePoints.Count > 0) renderer.SetPositions(routePoints.ToArray());
    }

    private void UpdateMaximumMarker()
    {
        int elementId = GlobalExtremeElement(activeResult, out Extreme extreme);
        if (maxMarker != null) Destroy(maxMarker);
        if (!showGhostTrail || elementId < 0 || extreme == null) return;
        SlabData markerSlab = null;
        if (UnityData.Structure != null && UnityData.Structure.slabs != null)
            foreach (SlabData candidate in UnityData.Structure.slabs) if (candidate.id == extreme.slab) { markerSlab = candidate; break; }
        maxMarker = new GameObject("MAX " + resultCodes[activeResult]); maxMarker.transform.SetParent(transform, false);
        maxMarker.transform.position = new Vector3(extreme.position.x, markerSlab != null ? markerSlab.z + .16f : .16f, extreme.position.y);
        Color color = new Color(1f, .72f, .08f, .95f);
        for (int i = 0; i < 3; i++)
        {
            GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube); arm.transform.SetParent(maxMarker.transform, false);
            arm.transform.localScale = new Vector3(.7f, .05f, .11f); arm.transform.localRotation = Quaternion.Euler(0f, i * 60f, 0f);
            arm.GetComponent<Renderer>().material.color = color; Destroy(arm.GetComponent<Collider>());
        }
        GameObject labelObject = new GameObject("MAX label"); labelObject.transform.SetParent(maxMarker.transform, false);
        labelObject.transform.localPosition = Vector3.up * .25f; labelObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        TextMesh text = labelObject.AddComponent<TextMesh>(); text.text = "MAX"; text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center; text.characterSize = .16f; text.fontSize = 44; text.color = color;
    }

    private void UpdateStructuralColors(float blend)
    {
        ElementSelectable selected = FindObjectOfType<ElementPicker>()?.Selected;
        foreach (var pair in structuralBaseColors)
        {
            if (pair.Key == null) continue;
            ElementSelectable element = pair.Key.GetComponent<ElementSelectable>();
            if (element == selected) { pair.Key.material.color = Color.Lerp(pair.Key.material.color, Color.yellow, blend); continue; }
            Color target = colorStructure && structuralTargetColors.TryGetValue(pair.Key, out Color mapped) ? mapped : pair.Value;
            pair.Key.material.color = Color.Lerp(pair.Key.material.color, target, blend);
        }
    }

    private void UpdateLoadPathVisuals(int responseSequence)
    {
        string slabId = mobile.CurrentSlab != null ? mobile.CurrentSlab.id : "";
        if (slabId != visualSlab || showTributaryAreas != (tributaryRoot != null && tributaryRoot.activeSelf))
        {
            visualSlab = slabId;
            RebuildTributaryVisuals();
        }
        if (responseSequence != visualResponseSequence || showLoadTransfer != (transferRoot != null && transferRoot.activeSelf))
        {
            visualResponseSequence = responseSequence;
            RebuildTransferVisuals();
        }
    }

    private void RebuildTributaryVisuals()
    {
        if (tributaryRoot != null) Destroy(tributaryRoot);
        tributaryRoot = new GameObject("Mobile tributary regions");
        tributaryRoot.transform.SetParent(transform, false);
        tributaryRoot.SetActive(showTributaryAreas);
        SlabData slab = mobile.CurrentSlab;
        SlabLoadMetadata metadata = mobile.CurrentSlabMetadata;
        if (!showTributaryAreas || slab == null || metadata == null || metadata.edges == null) return;
        string[] activeSides = Array.ConvertAll(metadata.edges, edge => edge.side);
        Color[] colors = { new Color(.1f,.75f,1f,.24f), new Color(1f,.55f,.15f,.24f), new Color(.35f,1f,.55f,.24f), new Color(.8f,.4f,1f,.24f) };
        for (int i = 0; i < activeSides.Length; i++)
        {
            List<Vector2> polygon = TributaryPolygon(slab, activeSides, activeSides[i]);
            if (polygon.Count < 3) continue;
            var obj = new GameObject("Tributary " + activeSides[i]); obj.transform.SetParent(tributaryRoot.transform, false);
            var mesh = new Mesh(); var vertices = new Vector3[polygon.Count];
            for (int k = 0; k < polygon.Count; k++) vertices[k] = new Vector3(polygon[k].x, slab.z + .035f, polygon[k].y);
            int[] triangles = new int[(polygon.Count - 2) * 3];
            for (int k = 0; k < polygon.Count - 2; k++) { triangles[k*3]=0;triangles[k*3+1]=k+1;triangles[k*3+2]=k+2; }
            mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();
            obj.AddComponent<MeshFilter>().mesh=mesh;
            var material=new Material(Shader.Find("Sprites/Default"));material.color=colors[i%colors.Length];
            obj.AddComponent<MeshRenderer>().material=material;
        }
    }

    private List<Vector2> TributaryPolygon(SlabData slab, string[] sides, string target)
    {
        float x0=Mathf.Min(slab.x0,slab.x1),x1=Mathf.Max(slab.x0,slab.x1),y0=Mathf.Min(slab.y0,slab.y1),y1=Mathf.Max(slab.y0,slab.y1);
        var polygon=new List<Vector2>{new Vector2(x0,y0),new Vector2(x1,y0),new Vector2(x1,y1),new Vector2(x0,y1)};
        foreach(string other in sides) if(other!=target)
            polygon=Clip(polygon,p=>DistanceToSide(p,target,x0,x1,y0,y1)-DistanceToSide(p,other,x0,x1,y0,y1));
        return polygon;
    }

    private float DistanceToSide(Vector2 p,string side,float x0,float x1,float y0,float y1)
    {
        if(side=="bottom")return p.y-y0;if(side=="top")return y1-p.y;
        if(side=="left")return p.x-x0;return x1-p.x;
    }

    private List<Vector2> Clip(List<Vector2> input, Func<Vector2,float> signed)
    {
        var output=new List<Vector2>();if(input.Count==0)return output;
        Vector2 previous=input[input.Count-1];float fp=signed(previous);bool previousInside=fp<=.00001f;
        foreach(Vector2 current in input)
        {
            float fc=signed(current);bool inside=fc<=.00001f;
            if(inside!=previousInside)
            {
                float t=fp/(fp-fc);output.Add(Vector2.Lerp(previous,current,t));
            }
            if(inside)output.Add(current);
            previous=current;fp=fc;previousInside=inside;
        }
        return output;
    }

    private void RebuildTransferVisuals()
    {
        if (transferRoot != null) Destroy(transferRoot);
        if (trailRoot != null) Destroy(trailRoot);
        if (maxMarker != null) Destroy(maxMarker);
        transferRoot = new GameObject("Mobile load transfer"); transferRoot.transform.SetParent(transform, false);
        transferRoot.SetActive(showLoadTransfer);
        var response = mobile.CurrentResponse;
        if (!showLoadTransfer || response == null || !response.ok || response.receivers == null) return;
        Vector3 source = new Vector3(mobile.CurrentPosition.x, mobile.CurrentSlab.z + 1.05f, mobile.CurrentPosition.y);
        foreach (var receiver in response.receivers)
        {
            if (!membersById.TryGetValue(receiver.beam, out ElementSelectable beam)) continue;
            Vector3 target=Vector3.Lerp(beam.startPoint,beam.endPoint,Mathf.Clamp01(receiver.t));
            float ratio=response.p>1e-8f?receiver.load/response.p:0f;
            var obj=new GameObject("Transfer B"+receiver.beam);obj.transform.SetParent(transferRoot.transform,false);
            var line=obj.AddComponent<LineRenderer>();line.useWorldSpace=true;line.positionCount=3;
            Vector3 bend=Vector3.Lerp(source,target,.55f)+Vector3.up*.25f;line.SetPositions(new[]{source,bend,target+Vector3.up*.05f});
            line.startWidth=line.endWidth=.025f+.11f*Mathf.Clamp01(ratio);
            line.material=new Material(Shader.Find("Sprites/Default"));line.startColor=line.endColor=Color.Lerp(new Color(.2f,.7f,1f,.75f),new Color(1f,.72f,.1f,.95f),ratio);
            var beamLine=new GameObject("Receiver highlight B"+receiver.beam);beamLine.transform.SetParent(transferRoot.transform,false);
            var highlight=beamLine.AddComponent<LineRenderer>();highlight.useWorldSpace=true;highlight.positionCount=2;highlight.SetPositions(new[]{beam.startPoint,beam.endPoint});
            highlight.startWidth=highlight.endWidth=.08f+.13f*Mathf.Clamp01(ratio);highlight.material=new Material(Shader.Find("Sprites/Default"));highlight.startColor=highlight.endColor=line.startColor;
        }
    }

    private void SelectAndFocus(ElementSelectable element)
    {
        ElementPicker picker=FindObjectOfType<ElementPicker>();if(picker!=null)picker.SelectElement(element,true);
    }

    private string MemberTag(ElementSelectable element) => element == null || element.data == null ? "—" :
        (string.IsNullOrEmpty(element.data.elementTag) ? "E" + element.data.id : element.data.elementTag);
    private string MemberType(ElementSelectable element) => element == null || element.data == null ? "—" :
        element.data.type == "viga" ? "Beam" : element.data.type == "columna" ? "Column" : "Link";
    private string ActiveUnits => activeResult >= 3 && activeResult <= 4 ? "kN·m" : activeResult == 5 ? "mm" : "kN";

    private void DataRow(ref float y, string name, string value)
    {
        GUI.Label(new Rect(4f, y, 180f, 20f), name, label);
        GUI.Label(new Rect(178f, y, 150f, 20f), value, metric);
        y += 22f;
    }

    private void StatusRow(ref float y, string name, bool ok, string detail)
    {
        GUI.Box(new Rect(4f, y, 324f, 30f), GUIContent.none, ok ? pass : fail);
        GUI.Label(new Rect(12f, y + 5f, 190f, 20f), name + " · " + (ok ? "PASS" : "FAIL"), ok ? pass : fail);
        GUI.Label(new Rect(204f, y + 5f, 118f, 20f), detail, muted);
        y += 36f;
    }

    private void EnsureStyles()
    {
        if (panel != null) return;
        panelTexture = Texture(new Color(.025f, .035f, .055f, .96f));
        cardTexture = Texture(new Color(.075f, .095f, .13f, .96f));
        passTexture = Texture(new Color(.035f, .25f, .16f, .95f));
        failTexture = Texture(new Color(.34f, .075f, .085f, .95f));
        panel = new GUIStyle(GUI.skin.box); panel.normal.background = panelTexture;
        title = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
        title.normal.textColor = new Color(.88f, .95f, 1f);
        eyebrow = new GUIStyle(title) { fontSize = 11 }; eyebrow.normal.textColor = new Color(.25f, .82f, 1f);
        metric = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold }; metric.normal.textColor = Color.white;
        label = new GUIStyle(GUI.skin.label) { fontSize = 12 }; label.normal.textColor = new Color(.82f, .88f, .94f);
        muted = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true }; muted.normal.textColor = new Color(.58f, .68f, .77f);
        section = new GUIStyle(metric) { fontSize = 11 }; section.normal.textColor = new Color(.25f, .82f, 1f);
        rank = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = 11 };
        rank.normal.textColor = new Color(.86f,.92f,.98f); rank.hover.textColor = Color.white;
        pass = new GUIStyle(label); pass.normal.background = passTexture; pass.normal.textColor = new Color(.5f, 1f, .7f);
        fail = new GUIStyle(label); fail.normal.background = failTexture; fail.normal.textColor = new Color(1f, .55f, .58f);
    }

    private Texture2D Texture(Color color)
    {
        var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply(); return texture;
    }

    private void OnDestroy()
    {
        if (panelTexture != null) Destroy(panelTexture);
        if (cardTexture != null) Destroy(cardTexture);
        if (passTexture != null) Destroy(passTexture);
        if (failTexture != null) Destroy(failTexture);
        if (tributaryRoot != null) Destroy(tributaryRoot);
        if (transferRoot != null) Destroy(transferRoot);
        foreach (var pair in structuralBaseColors) if (pair.Key != null) pair.Key.material.color = pair.Value;
    }
}
