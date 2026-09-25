using System;
using System.Collections.Generic;
using UnityEngine;

// Phase 1 live interface. Movement and analysis remain owned by MobileLoadController.
public class MobileLoadLivePanel : MonoBehaviour
{
    private static MobileLoadLivePanel activePanel;
    public static bool OwnsMobilePanel => activePanel != null && activePanel.isActiveAndEnabled;
    public static bool BlocksPointer()
    {
        if (!OwnsMobilePanel || activePanel.mobile == null || !activePanel.mobile.visible) return false;
        Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        return activePanel.panelRect.Contains(mouse);
    }

    private readonly string[] resultNames = { "N", "Vy", "Vz", "My", "Mz", "Deformed" };
    private readonly string[] scopeNames = { "AFFECTED MEMBERS", "CURRENT FLOOR", "WHOLE STRUCTURE" };
    private readonly List<ElementSelectable> members = new List<ElementSelectable>();
    private readonly Dictionary<int, ElementSelectable> membersById = new Dictionary<int, ElementSelectable>();
    private readonly Dictionary<Renderer, Color> baseColors = new Dictionary<Renderer, Color>();
    private MobileLoadController mobile;
    private DiagramController diagrams;
    private ElementPicker picker;
    private Rect panelRect;
    private Vector2 scroll;
    private int activeResult = 4;
    private int scope;
    private bool showTributaryAreas;
    private bool showMovementSetup;
    private bool resultsDirty = true;
    private int lastSequence = int.MinValue;
    private string lastLoadState = "";
    private string currentSlabId = "";
    private string previousSlabId = "";
    private float slabTransitionUntil;
    private ElementSelectable critical;
    private float criticalValue;
    private GameObject tributaryRoot;

    private GUIStyle panelStyle, cardStyle, titleStyle, sectionStyle, labelStyle, valueStyle, mutedStyle;
    private GUIStyle passStyle, failStyle, statusStyle, barLabelStyle;
    private Texture2D panelTexture, cardTexture, passTexture, failTexture, cyanTexture, trackTexture;

    private void OnEnable() { activePanel = this; Bind(); }
    private void OnDisable()
    {
        if (activePanel == this) activePanel = null;
        if (mobile != null) mobile.ResponseApplied -= OnResponseApplied;
        if (diagrams != null) diagrams.ClearMobileComponentDiagrams();
        RestoreMemberColors();
        if (tributaryRoot != null) Destroy(tributaryRoot);
        DestroyTexture(panelTexture); DestroyTexture(cardTexture); DestroyTexture(passTexture);
        DestroyTexture(failTexture); DestroyTexture(cyanTexture); DestroyTexture(trackTexture);
        panelStyle = null;
    }

    private void Bind()
    {
        MobileLoadController next = MobileLoadController.Instance ?? GetComponent<MobileLoadController>();
        if (next != mobile)
        {
            if (mobile != null) mobile.ResponseApplied -= OnResponseApplied;
            mobile = next;
            if (mobile != null) mobile.ResponseApplied += OnResponseApplied;
        }
        if (diagrams == null) diagrams = GetComponent<DiagramController>();
        if (picker == null) picker = FindObjectOfType<ElementPicker>();
    }

    private void OnResponseApplied(MobileLoadController.Response response)
    {
        lastSequence = response != null ? response.seq : int.MinValue;
        resultsDirty = true;
    }

    private void Update()
    {
        Bind();
        if (mobile == null) return;
        SlabData slab = mobile.CurrentSlab;
        string slabId = slab != null ? slab.id : "";
        if (slabId != currentSlabId)
        {
            previousSlabId = currentSlabId;
            currentSlabId = slabId;
            slabTransitionUntil = Time.unscaledTime + 1.8f;
            resultsDirty = true;
            RebuildTributaryVisuals();
        }
        int sequence = mobile.CurrentResponse != null && mobile.CurrentResponse.ok ? mobile.CurrentResponse.seq : int.MinValue;
        if (sequence != lastSequence) { lastSequence = sequence; resultsDirty = true; }
        string loadState = UnityData.GetActiveLoadLabel();
        if (loadState != lastLoadState) { lastLoadState = loadState; resultsDirty = true; }
        if (resultsDirty) RebuildStructuralView();
        UpdateCriticalHighlight();
    }

    private void OnGUI()
    {
        if (!Application.isPlaying) return;
        Bind(); EnsureStyles();
        if (mobile == null || !mobile.visible) return;

        float width = Mathf.Min(455f, Screen.width - 20f);
        float height = Mathf.Min(930f, Screen.height - 20f);
        panelRect = PanelLayout.Apply("MobileLoadLivePhase1", new Rect(Screen.width - width - 10f, 10f, width, height), 32f);
        GUI.Box(panelRect, GUIContent.none, panelStyle);
        GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 8f, panelRect.width - 32f, 22f), "CARGA MÓVIL — LIVE ANALYSIS", titleStyle);

        float contentHeight = showMovementSetup ? 1370f : 790f;
        Rect viewport = new Rect(panelRect.x + 10f, panelRect.y + 37f, panelRect.width - 20f, panelRect.height - 47f);
        scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0f, 0f, viewport.width - 18f, contentHeight));
        float y = 2f;
        DrawHeader(ref y, viewport.width - 22f);
        DrawCurrentSlab(ref y, viewport.width - 22f);
        DrawTransfer(ref y, viewport.width - 22f);
        DrawResponse(ref y, viewport.width - 22f);
        DrawMovementSetup(ref y, viewport.width - 22f);
        GUI.EndScrollView();
    }

    private void DrawHeader(ref float y, float width)
    {
        GUI.Box(new Rect(2f, y, width, 98f), GUIContent.none, cardStyle);
        GUI.Label(new Rect(14f, y + 8f, 90f, 17f), "STATUS", mutedStyle);
        GUI.Label(new Rect(14f, y + 27f, 105f, 27f), mobile.LiveMovementStatus, statusStyle);
        GUI.Label(new Rect(132f, y + 8f, 90f, 17f), "PERSON", mutedStyle);
        GUI.Label(new Rect(132f, y + 27f, 105f, 27f), $"{mobile.loadKN:0.00} kN", valueStyle);
        GUI.Label(new Rect(250f, y + 8f, 110f, 17f), "CURRENT SLAB", mutedStyle);
        GUI.Label(new Rect(250f, y + 27f, 100f, 27f), string.IsNullOrEmpty(currentSlabId) ? "—" : currentSlabId, valueStyle);
        GUI.Label(new Rect(350f, y + 8f, 80f, 17f), "RESULT", mutedStyle);
        GUI.Label(new Rect(350f, y + 27f, 72f, 27f), resultNames[activeResult], valueStyle);
        bool nextActive = GUI.Toggle(new Rect(14f, y + 64f, 178f, 24f), mobile.LoadActive, " PERSON + LOAD ACTIVE");
        if (nextActive != mobile.LoadActive) mobile.SetLoadActive(nextActive);
        GUI.Label(new Rect(202f, y + 65f, width - 214f, 24f), ShortStatus(mobile.AnalysisStatus), mutedStyle);
        y += 108f;
    }

    private void DrawCurrentSlab(ref float y, float width)
    {
        SlabData slab = mobile.CurrentSlab;
        SlabLoadMetadata metadata = mobile.CurrentSlabMetadata;
        GUI.Label(new Rect(4f, y, width, 20f), "LOSA ACTUAL", sectionStyle); y += 23f;
        GUI.Box(new Rect(2f, y, width, 176f), GUIContent.none, cardStyle);
        if (slab == null)
        {
            GUI.Label(new Rect(14f, y + 18f, width - 24f, 48f), "Haz click sobre una losa translúcida para comenzar.", labelStyle);
            y += 186f; return;
        }
        float slabArea = SlabArea(slab), tributaryArea = TributaryArea(metadata);
        HashSet<int> supporting = SupportingBeams(metadata);
        string transition = !string.IsNullOrEmpty(previousSlabId) && Time.unscaledTime < slabTransitionUntil
            ? previousSlabId + "  →  " + slab.id : slab.id;
        GUI.Label(new Rect(14f, y + 9f, width - 28f, 25f), transition, titleStyle);
        GUI.Label(new Rect(14f, y + 36f, width - 28f, 20f), slab.nivel, mutedStyle);
        InfoPair(y + 61f, "AREA", $"{slabArea:0.00} m²", 14f);
        InfoPair(y + 61f, "TRIBUTARY AREA", $"{tributaryArea:0.00} m²", 150f);
        InfoPair(y + 61f, "SUPPORTING BEAMS", supporting.Count.ToString(), 306f);
        float difference = tributaryArea - slabArea;
        bool pass = Mathf.Abs(difference) <= Mathf.Max(.0001f, slabArea * .0001f);
        GUI.Label(new Rect(14f, y + 112f, 150f, 18f), "SUM TRIBUTARY AREA", mutedStyle);
        GUI.Label(new Rect(166f, y + 112f, 96f, 18f), $"{tributaryArea:0.000} m²", labelStyle);
        GUI.Label(new Rect(14f, y + 134f, 150f, 18f), "DIFFERENCE", mutedStyle);
        GUI.Label(new Rect(166f, y + 134f, 96f, 18f), $"{difference:0.0000} m²", labelStyle);
        GUI.Label(new Rect(width - 92f, y + 124f, 75f, 25f), pass ? "PASS" : "FAIL", pass ? passStyle : failStyle);
        bool nextShow = GUI.Toggle(new Rect(275f, y + 150f, 150f, 21f), showTributaryAreas, " SHOW TRIBUTARY AREAS");
        if (nextShow != showTributaryAreas) { showTributaryAreas = nextShow; RebuildTributaryVisuals(); }
        y += 186f;
    }

    private void DrawTransfer(ref float y, float width)
    {
        GUI.Label(new Rect(4f, y, width, 20f), "TRANSFERENCIA", sectionStyle); y += 23f;
        MobileLoadController.Response response = mobile.CurrentResponse;
        float applied = mobile.loadKN;
        float transferred = response != null && response.ok ? response.transferred : 0f;
        float error = response != null && response.ok ? response.error : 0f;
        bool pass = response != null && response.ok && error <= Mathf.Max(1e-6f, applied * 1e-6f);
        Dictionary<int, float> loads = AggregateLoads(response);
        float blockHeight = 116f + loads.Count * 38f;
        GUI.Box(new Rect(2f, y, width, blockHeight), GUIContent.none, cardStyle);
        InfoPair(y + 10f, "APPLIED", $"{applied:0.000} kN", 14f);
        InfoPair(y + 10f, "TRANSFERRED", $"{transferred:0.000} kN", 150f);
        InfoPair(y + 10f, "ERROR", $"{error:0.0000} kN", 306f);
        GUI.Label(new Rect(width - 92f, y + 56f, 75f, 25f), pass ? "PASS" : response == null ? "WAIT" : "FAIL",
            pass ? passStyle : response == null ? mutedStyle : failStyle);
        GUI.Label(new Rect(14f, y + 61f, 210f, 18f), "VIGAS RECEPTORAS", sectionStyle);
        float row = y + 84f;
        foreach (var pair in loads)
        {
            float ratio = applied > 1e-8f ? pair.Value / applied : 0f;
            GUI.Label(new Rect(14f, row, 70f, 20f), BeamTag(pair.Key), labelStyle);
            GUI.Label(new Rect(85f, row, 94f, 20f), $"{pair.Value:0.0000} kN", valueStyle);
            GUI.Label(new Rect(181f, row, 48f, 20f), $"{ratio * 100f:0}%", labelStyle);
            Rect track = new Rect(232f, row + 5f, width - 250f, 10f);
            GUI.DrawTexture(track, trackTexture);
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * Mathf.Clamp01(ratio), track.height), cyanTexture);
            row += 38f;
        }
        if (loads.Count == 0) GUI.Label(new Rect(14f, row, width - 28f, 22f), "Esperando respuesta OpenSees…", mutedStyle);
        y += blockHeight + 10f;
    }

    private void DrawResponse(ref float y, float width)
    {
        GUI.Label(new Rect(4f, y, width, 20f), "RESPUESTA EN VIVO", sectionStyle); y += 23f;
        GUI.Box(new Rect(2f, y, width, 196f), GUIContent.none, cardStyle);
        GUI.Label(new Rect(14f, y + 9f, width - 28f, 18f), "ACTIVE RESULT", mutedStyle);
        int nextResult = GUI.SelectionGrid(new Rect(14f, y + 30f, width - 28f, 48f), activeResult, resultNames, 6);
        if (nextResult != activeResult) { activeResult = nextResult; resultsDirty = true; }
        GUI.Label(new Rect(14f, y + 84f, width - 28f, 18f), "DIAGRAM SCOPE", mutedStyle);
        int nextScope = GUI.Toolbar(new Rect(14f, y + 105f, width - 28f, 27f), scope, scopeNames);
        if (nextScope != scope) { scope = nextScope; resultsDirty = true; }
        GUI.Label(new Rect(14f, y + 142f, 130f, 18f), "CRITICAL NOW", sectionStyle);
        string tag = critical != null ? MemberTag(critical) : "—";
        string type = critical != null ? MemberType(critical) : "";
        GUI.Label(new Rect(14f, y + 162f, 130f, 25f), tag, titleStyle);
        GUI.Label(new Rect(146f, y + 162f, 92f, 25f), type, mutedStyle);
        GUI.Label(new Rect(240f, y + 157f, width - 255f, 30f),
            critical != null ? $"{criticalValue:0.###} {ActiveUnits}" : "—", valueStyle);
        y += 206f;
        GUI.Label(new Rect(6f, y, width - 12f, 38f),
            "Losas: área, reparto y conservación. Los diagramas corresponden a elementos frame; no se generan esfuerzos shell ni fuerzas de muro no exportadas.", mutedStyle);
        y += 44f;
    }

    private void DrawMovementSetup(ref float y, float width)
    {
        if (GUI.Button(new Rect(2f, y, width, 29f), showMovementSetup ? "MOVEMENT SETUP  ▲" : "MOVEMENT SETUP  ▼"))
            showMovementSetup = !showMovementSetup;
        y += 35f;
        if (!showMovementSetup) return;
        GUI.Box(new Rect(2f, y, width, 555f), GUIContent.none, cardStyle);
        GUILayout.BeginArea(new Rect(14f, y + 10f, width - 28f, 535f));
        mobile.DrawMovementSetupInline();
        GUILayout.EndArea();
        y += 565f;
    }

    private void RebuildStructuralView()
    {
        resultsDirty = false;
        RefreshMembers();
        critical = null; criticalValue = 0f;
        var ranked = new List<MemberValue>();
        foreach (ElementSelectable member in members)
        {
            float value = MemberValueAtMaximum(member, resultNames[activeResult]);
            if (float.IsNaN(value) || float.IsInfinity(value)) continue;
            ranked.Add(new MemberValue(member, value));
            if (critical == null || Mathf.Abs(value) > Mathf.Abs(criticalValue)) { critical = member; criticalValue = value; }
        }
        ranked.Sort((a, b) => Mathf.Abs(b.value).CompareTo(Mathf.Abs(a.value)));
        if (diagrams == null) return;
        if (activeResult == 5) { diagrams.ShowMobileComponent("Deformed", null); return; }
        diagrams.ShowMobileComponent(resultNames[activeResult], TargetsForScope(ranked));
    }

    private List<ElementSelectable> TargetsForScope(List<MemberValue> ranked)
    {
        var result = new List<ElementSelectable>();
        if (scope == 2) { result.AddRange(members); return result; }
        if (scope == 1)
        {
            SlabData slab = mobile.CurrentSlab;
            if (slab == null) return result;
            foreach (ElementSelectable member in members)
            {
                float low = Mathf.Min(member.startPoint.y, member.endPoint.y) - .35f;
                float high = Mathf.Max(member.startPoint.y, member.endPoint.y) + .35f;
                if (slab.z >= low && slab.z <= high) result.Add(member);
            }
            return result;
        }

        float bound = ranked.Count > 0 ? Mathf.Abs(ranked[0].value) : 0f;
        var used = new HashSet<int>();
        foreach (MemberValue item in ranked)
        {
            if (result.Count >= 36) break;
            bool receivesLoad = UnityData.MobileForces.ContainsKey(item.element.data.id);
            if (!receivesLoad || (bound > 1e-8f && Mathf.Abs(item.value) < bound * .05f)) continue;
            if (used.Add(item.element.data.id)) result.Add(item.element);
        }
        MobileLoadController.Response response = mobile.CurrentResponse;
        if (response != null && response.receivers != null)
            foreach (var receiver in response.receivers)
                if (membersById.TryGetValue(receiver.beam, out ElementSelectable beam) && used.Add(receiver.beam)) result.Add(beam);
        if (critical != null && used.Add(critical.data.id)) result.Add(critical);
        return result;
    }

    private struct MemberValue
    {
        public ElementSelectable element; public float value;
        public MemberValue(ElementSelectable e, float v) { element = e; value = v; }
    }

    private float MemberValueAtMaximum(ElementSelectable element, string component)
    {
        if (component == "Deformed")
        {
            Vector3 a = UnityData.GetNodeDisplacement(UnityData.ActiveCombo, element.data.nodeI);
            Vector3 b = UnityData.GetNodeDisplacement(UnityData.ActiveCombo, element.data.nodeJ);
            return Mathf.Max(a.magnitude, b.magnitude) * 1000f;
        }
        int index = component == "N" ? 0 : component == "Vy" ? 1 : component == "Vz" ? 2 : component == "My" ? 4 : 5;
        bool found = false; float selected = 0f;
        for (int station = 0; station <= 12; station++)
            if (UnityData.TryGetSectionForces(element.data.id, UnityData.ActiveCombo, station / 12f, out FrameSectionForces forces))
            {
                float value = forces.Component(index);
                if (!found || Mathf.Abs(value) > Mathf.Abs(selected)) { selected = value; found = true; }
            }
        return found ? selected : float.NaN;
    }

    private void RefreshMembers()
    {
        members.Clear(); membersById.Clear();
        if (diagrams == null) return;
        foreach (ElementSelectable element in diagrams.StructuralElements)
            if (element != null && element.data != null &&
                (element.data.type == "viga" || element.data.type == "columna" || element.data.type == "enlace"))
            {
                members.Add(element); membersById[element.data.id] = element;
                Renderer renderer = element.GetComponent<Renderer>();
                if (renderer != null && !baseColors.ContainsKey(renderer)) baseColors[renderer] = renderer.material.color;
            }
    }

    private void UpdateCriticalHighlight()
    {
        float blend = 1f - Mathf.Exp(-7f * Time.unscaledDeltaTime);
        ElementSelectable selected = picker != null ? picker.Selected : null;
        foreach (var pair in baseColors)
        {
            if (pair.Key == null) continue;
            ElementSelectable element = pair.Key.GetComponent<ElementSelectable>();
            Color target = element == selected ? Color.yellow : element == critical ? new Color(1f, .68f, .08f) : pair.Value;
            pair.Key.material.color = Color.Lerp(pair.Key.material.color, target, blend);
        }
    }

    private void RestoreMemberColors()
    {
        foreach (var pair in baseColors) if (pair.Key != null) pair.Key.material.color = pair.Value;
        baseColors.Clear();
    }

    private void RebuildTributaryVisuals()
    {
        if (tributaryRoot != null) Destroy(tributaryRoot);
        tributaryRoot = new GameObject("Current slab tributary areas"); tributaryRoot.transform.SetParent(transform, false);
        if (!showTributaryAreas || mobile == null || mobile.CurrentSlab == null || mobile.CurrentSlabMetadata == null) return;
        SlabData slab = mobile.CurrentSlab; SlabLoadMetadata metadata = mobile.CurrentSlabMetadata;
        if (metadata.edges == null) return;
        string[] sides = Array.ConvertAll(metadata.edges, edge => edge.side);
        for (int i = 0; i < sides.Length; i++)
        {
            List<Vector2> polygon = TributaryPolygon(slab, sides, sides[i]);
            if (polygon.Count < 3) continue;
            GameObject region = new GameObject("Tributary " + sides[i]); region.transform.SetParent(tributaryRoot.transform, false);
            var mesh = new Mesh(); var vertices = new Vector3[polygon.Count];
            for (int p = 0; p < polygon.Count; p++) vertices[p] = new Vector3(polygon[p].x, slab.z + .035f, polygon[p].y);
            int[] triangles = new int[(polygon.Count - 2) * 3];
            for (int p = 0; p < polygon.Count - 2; p++) { triangles[p * 3] = 0; triangles[p * 3 + 1] = p + 1; triangles[p * 3 + 2] = p + 2; }
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals();
            region.AddComponent<MeshFilter>().mesh = mesh;
            Material material = new Material(Shader.Find("Sprites/Default")); material.color = new Color(.12f, .72f, 1f, .22f + i * .025f);
            region.AddComponent<MeshRenderer>().material = material;
        }
        foreach (int beamId in SupportingBeams(metadata))
            if (membersById.TryGetValue(beamId, out ElementSelectable beam))
            {
                GameObject lineObject = new GameObject("Tributary beam " + beamId); lineObject.transform.SetParent(tributaryRoot.transform, false);
                LineRenderer line = lineObject.AddComponent<LineRenderer>(); line.useWorldSpace = true; line.positionCount = 2;
                line.SetPositions(new[] { beam.startPoint, beam.endPoint }); line.startWidth = line.endWidth = .13f;
                line.material = new Material(Shader.Find("Sprites/Default")); line.startColor = line.endColor = new Color(.15f, .85f, 1f, .95f);
            }
    }

    private List<Vector2> TributaryPolygon(SlabData slab, string[] sides, string target)
    {
        float x0 = Mathf.Min(slab.x0, slab.x1), x1 = Mathf.Max(slab.x0, slab.x1);
        float y0 = Mathf.Min(slab.y0, slab.y1), y1 = Mathf.Max(slab.y0, slab.y1);
        var polygon = new List<Vector2> { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };
        foreach (string other in sides) if (other != target)
            polygon = Clip(polygon, point => SideDistance(point, target, x0, x1, y0, y1) - SideDistance(point, other, x0, x1, y0, y1));
        return polygon;
    }

    private float SideDistance(Vector2 p, string side, float x0, float x1, float y0, float y1)
    {
        if (side == "bottom") return p.y - y0; if (side == "top") return y1 - p.y;
        if (side == "left") return p.x - x0; return x1 - p.x;
    }

    private List<Vector2> Clip(List<Vector2> input, Func<Vector2, float> signed)
    {
        var output = new List<Vector2>(); if (input.Count == 0) return output;
        Vector2 previous = input[input.Count - 1]; float fp = signed(previous); bool previousInside = fp <= .00001f;
        foreach (Vector2 current in input)
        {
            float fc = signed(current); bool inside = fc <= .00001f;
            if (inside != previousInside) output.Add(Vector2.Lerp(previous, current, fp / (fp - fc)));
            if (inside) output.Add(current);
            previous = current; fp = fc; previousInside = inside;
        }
        return output;
    }

    private Dictionary<int, float> AggregateLoads(MobileLoadController.Response response)
    {
        var result = new Dictionary<int, float>();
        if (response == null || response.receivers == null) return result;
        foreach (var receiver in response.receivers)
            result[receiver.beam] = result.TryGetValue(receiver.beam, out float value) ? value + receiver.load : receiver.load;
        return result;
    }

    private HashSet<int> SupportingBeams(SlabLoadMetadata metadata)
    {
        var result = new HashSet<int>();
        if (metadata == null || metadata.edges == null) return result;
        foreach (SlabLoadEdge edge in metadata.edges) if (edge.beams != null) foreach (int beam in edge.beams) result.Add(beam);
        return result;
    }

    private float TributaryArea(SlabLoadMetadata metadata)
    {
        float area = 0f; if (metadata == null || metadata.edges == null) return area;
        foreach (SlabLoadEdge edge in metadata.edges) area += edge.area; return area;
    }

    private float SlabArea(SlabData slab)
    {
        float area = Mathf.Abs((slab.x1 - slab.x0) * (slab.y1 - slab.y0));
        if (slab.openings != null) foreach (SlabOpening opening in slab.openings)
            area -= Mathf.Abs((opening.x1 - opening.x0) * (opening.y1 - opening.y0));
        return Mathf.Max(0f, area);
    }

    private string BeamTag(int id) => membersById.TryGetValue(id, out ElementSelectable element) ? MemberTag(element) : "B" + id;
    private string MemberTag(ElementSelectable element) => string.IsNullOrEmpty(element.data.elementTag) ? "E" + element.data.id : element.data.elementTag;
    private string MemberType(ElementSelectable element) => element.data.type == "viga" ? "Beam" : element.data.type == "columna" ? "Column" : "Link";
    private string ActiveUnits => activeResult == 5 ? "mm" : activeResult >= 3 ? "kN·m" : "kN";
    private string ShortStatus(string text) => string.IsNullOrEmpty(text) ? "" : text.Length <= 42 ? text : text.Substring(0, 39) + "…";

    private void InfoPair(float y, string caption, string value, float x)
    {
        GUI.Label(new Rect(x, y, 130f, 17f), caption, mutedStyle);
        GUI.Label(new Rect(x, y + 18f, 136f, 24f), value, valueStyle);
    }

    private void EnsureStyles()
    {
        if (panelStyle != null) return;
        panelTexture = Solid(new Color(.025f, .035f, .055f, .97f));
        cardTexture = Solid(new Color(.065f, .085f, .12f, .98f));
        passTexture = Solid(new Color(.04f, .31f, .18f, 1f)); failTexture = Solid(new Color(.42f, .07f, .08f, 1f));
        cyanTexture = Solid(new Color(.12f, .78f, 1f, 1f)); trackTexture = Solid(new Color(.13f, .16f, .21f, 1f));
        panelStyle = new GUIStyle(GUI.skin.box); panelStyle.normal.background = panelTexture;
        cardStyle = new GUIStyle(GUI.skin.box); cardStyle.normal.background = cardTexture;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold }; titleStyle.normal.textColor = Color.white;
        sectionStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold }; sectionStyle.normal.textColor = new Color(.2f, .82f, 1f);
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 }; labelStyle.normal.textColor = new Color(.86f, .91f, .96f);
        valueStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold }; valueStyle.normal.textColor = Color.white;
        mutedStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true }; mutedStyle.normal.textColor = new Color(.57f, .67f, .76f);
        statusStyle = new GUIStyle(valueStyle); statusStyle.normal.textColor = new Color(.2f, .82f, 1f);
        passStyle = new GUIStyle(valueStyle) { alignment = TextAnchor.MiddleCenter }; passStyle.normal.background = passTexture; passStyle.normal.textColor = new Color(.55f, 1f, .72f);
        failStyle = new GUIStyle(valueStyle) { alignment = TextAnchor.MiddleCenter }; failStyle.normal.background = failTexture; failStyle.normal.textColor = new Color(1f, .62f, .62f);
        barLabelStyle = new GUIStyle(labelStyle);
    }

    private Texture2D Solid(Color color) { var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply(); return texture; }
    private void DestroyTexture(Texture2D texture) { if (texture != null) Destroy(texture); }
}
