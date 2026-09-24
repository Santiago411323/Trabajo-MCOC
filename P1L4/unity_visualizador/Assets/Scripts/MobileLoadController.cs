using System.Collections.Generic;
using UnityEngine;

public class MobileLoadController : MonoBehaviour
{
    public static MobileLoadController Instance { get; private set; }

    public float loadKN = 100f;
    public bool visible = true;
    public float diagramScale = 0.018f;
    public float posX01 = 0.5f;
    public float posZ01 = 0.5f;
    public bool showAxial = true;
    public bool showShear = true;
    public bool showMoment = true;
    // Caminata automatica: sobre una viga seleccionada recorre la viga de I a J
    // y vuelve; en otro caso recorre la losa. Cualquier ajuste manual la detiene.
    public bool autoWalk = false;
    public float autoWalkSpeed01 = 0.12f; // fraccion del recorrido por segundo
    public bool logDiagnostics = false;    // logs [MobileLoadDiagnostics] en consola

    private const float LEVEL_TOL = 0.12f;
    private const int MAX_SUPPORT_COLUMNS = 6;

    private ElementSelectable selectedElement;
    private bool slabBoundsOverride;
    private DiagramController diagramController;
    private GameObject personRoot;
    private Transform personBody;
    private Transform personHead;
    private Transform personLeftArm;
    private Transform personRightArm;
    private Transform personLeftLeg;
    private Transform personRightLeg;
    private float walkPhase;
    private float walkSpeed;
    private bool personMoving;
    private Vector2 lastPersonPlan = new Vector2(float.NaN, float.NaN);
    private Vector2 personFacing = new Vector2(0f, 1f);
    private GameObject axialLine;
    private GameObject shearLine;
    private GameObject momentLine;
    private GameObject axialBaseLine;
    private GameObject shearBaseLine;
    private GameObject momentBaseLine;
    private readonly List<GameObject> reactionArrows = new List<GameObject>();
    private readonly List<ColumnSupport> supports = new List<ColumnSupport>();
    private GUIStyle boxStyle;
    private GUIStyle labelStyle;
    private GUIStyle valueStyle;
    private Texture2D panelBg;
    private Texture2D valueBg;

    private float levelY;
    private string levelLabel = "";
    private float planXMin, planXMax, planZMin, planZMax;
    private bool boundsReady;
    private bool hasLevel;
    private bool lastAutoWalkState;
    private float walkClockX;
    private float walkClockZ;
    private float autoWalkClock;
    private float beamLoadFraction = 1f;
    private float beamLoadDistance;
    private ElementPicker cachedPicker;
    private ElementSelectable lastPickerSelection;
    private bool loadOnSlab;          // la persona esta sobre una losa del nivel
    private bool placeOnBeamPending;  // ubicar a la persona en el centro de la viga recien seleccionada
    private readonly List<ElementSelectable> cachedColumns = new List<ElementSelectable>();
    private float nextColumnCacheTime;
    private bool staleObjectsCleared;
    private int diagnosticUpdateCount;
    private float nextDiagnosticLogTime;
    private Vector3 lastDiagnosticLoadPosition = new Vector3(float.NaN, float.NaN, float.NaN);

    // Reparto de la carga de la persona desde la losa a sus apoyos (vigas o
    // muros). Se busca el apoyo mas cercano en cada direccion (+X, -X, +Z, -Z)
    // y la carga se reparte por franjas cruzadas (ver ComputeSlabDistribution).
    private class SlabShare
    {
        public string label;
        public ElementSelectable beam;   // null si el apoyo es un muro
        public float fraction;
        public float t;                  // posicion de la carga sobre la viga (0..1)
        public float distance;
    }

    private readonly List<SlabShare> slabShares = new List<SlabShare>();
    private float wallReaction;
    private float otherColumnsReaction;
    private bool slabSharesValid;
    private readonly List<ElementSelectable> cachedBeams = new List<ElementSelectable>();
    private float nextBeamCacheTime;
    private List<Vector4> wallPlanSegments;   // (x0, z0, x1, z1) en coordenadas Unity

    private class ColumnSupport
    {
        public Vector3 top;
        public Vector2 plan;
        public string tag;
        public float reaction;
    }

    public static Rect PanelRect()
    {
        return PanelLayout.Get("MobileLoad", new Rect(360f, 150f, 380f, 440f));
    }

    public bool IsPanelVisible()
    {
        return visible;
    }

    private void OnEnable()
    {
        if (Instance == null || Instance == this)
        {
            Instance = this;
        }
        else
        {
            enabled = false;
        }
    }

    private void OnDisable()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool IsPanelReady()
    {
        return visible && hasLevel && boundsReady;
    }

    public bool SameElement(ElementSelectable check)
    {
        return selectedElement != null && check != null && selectedElement == check;
    }

    public bool IsActiveFor(ElementSelectable check)
    {
        return IsPanelReady() && SameElement(check);
    }

    public bool TryGetLocalBeamContribution(ElementSelectable element, float sectionPosition01,
        out float extraVz, out float extraMy)
    {
        extraVz = 0f;
        extraMy = 0f;
        if (!IsActiveFor(element) || element.data == null || element.data.type != "viga")
            return false;

        float length = UnityData.TryGetFrameGeometry(element.data.id, out var frame)
            ? (float)frame.Length : Mathf.Max((element.endPoint - element.startPoint).magnitude, 0.001f);
        extraVz = ExtraAt(element, "Shear", sectionPosition01, length);
        extraMy = ExtraAt(element, "Moment", sectionPosition01, length);
        return true;
    }

    public void SetSelectedElement(ElementSelectable element)
    {
        if (element != selectedElement)
            placeOnBeamPending = true;
        selectedElement = element;
        slabBoundsOverride = false;
    }

    // Ubica a la persona sobre un punto (click en la viga seleccionada).
    public void PlaceLoadAt(Vector3 worldPoint, ElementSelectable beam)
    {
        if (beam != null) selectedElement = beam;
        levelY = beam != null ? 0.5f * (beam.startPoint.y + beam.endPoint.y) : worldPoint.y;
        if (!ComputePlanBounds(levelY)) return;
        posX01 = Mathf.Clamp01((worldPoint.x - planXMin) / Mathf.Max(planXMax - planXMin, 1e-4f));
        posZ01 = Mathf.Clamp01((worldPoint.z - planZMin) / Mathf.Max(planZMax - planZMin, 1e-4f));
        StopAutoWalk();
        placeOnBeamPending = false;
        slabBoundsOverride = true;
        boundsReady = true;
        hasLevel = true;
    }

    // Al seleccionar una viga la persona aparece en su centro: asi el efecto en
    // el diagrama se ve de inmediato (antes quedaba donde estaba, a menudo lejos).
    private void PlaceOnSelectedBeamCenter()
    {
        placeOnBeamPending = false;
        if (selectedElement == null || selectedElement.data == null || selectedElement.data.type != "viga") return;
        Vector3 mid = 0.5f * (selectedElement.startPoint + selectedElement.endPoint);
        posX01 = Mathf.Clamp01((mid.x - planXMin) / Mathf.Max(planXMax - planXMin, 1e-4f));
        posZ01 = Mathf.Clamp01((mid.z - planZMin) / Mathf.Max(planZMax - planZMin, 1e-4f));
        StopAutoWalk();
    }

    private void Update()
    {
        diagnosticUpdateCount++;
        SyncSelectedElement();

        if (!visible)
        {
            LogDiagnostics("visible=false");
            ClearVisuals();
            return;
        }

        if (selectedElement != null && !slabBoundsOverride)
        {
            ComputeLevelAndBounds();
            if (boundsReady)
            {
                hasLevel = true;
                if (placeOnBeamPending) PlaceOnSelectedBeamCenter();
            }
        }
        else if (!boundsReady && !hasLevel)
        {
            InitDefaultLevel();
        }

        if (!boundsReady || !hasLevel)
        {
            LogDiagnostics("sin bounds/level");
            ClearVisuals();
            return;
        }

        UpdateManualPositionFromMouse();
        UpdateManualDiagramButtonsFromMouse();
        UpdateVisuals();
        LogDiagnostics("actualizando");
    }

    private void UpdateManualDiagramButtonsFromMouse()
    {
        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        Rect panel = PanelRect();
        Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        if (SelectedBeamDiagramPanel.BlocksPointer() && !panel.Contains(mouse))
        {
            return;
        }

        float rowY = panel.y + 180f;
        string selected = selectedElement == null
            ? "null"
            : (selectedElement.data == null ? selectedElement.name : selectedElement.data.type);
        if (logDiagnostics)
            Debug.Log($"[MobileDiagramDiagnostics] click mouse={mouse} panel={panel} rowY={rowY:0.0} " +
                      $"selected={selected} states=A:{showAxial} S:{showShear} M:{showMoment}");

        if (selectedElement == null || selectedElement.data == null)
        {
            return;
        }

        bool isViga = selectedElement.data.type == "viga";
        bool isColumna = selectedElement.data.type == "columna";
        if (!isViga && !isColumna)
        {
            return;
        }

        if (new Rect(panel.x + 82f, rowY, 62f, 20f).Contains(mouse))
        {
            showAxial = !showAxial;
            Debug.Log($"[MobileDiagramDiagnostics] axial changed to {showAxial}");
        }
        else if (isViga && new Rect(panel.x + 146f, rowY, 62f, 20f).Contains(mouse))
        {
            showShear = !showShear;
            Debug.Log($"[MobileDiagramDiagnostics] shear changed to {showShear}");
        }
        else if (isViga && new Rect(panel.x + 210f, rowY, 82f, 20f).Contains(mouse))
        {
            showMoment = !showMoment;
            Debug.Log($"[MobileDiagramDiagnostics] moment changed to {showMoment}");
        }
    }

    private void UpdateManualPositionFromMouse()
    {
        if (!Input.GetMouseButton(0))
        {
            return;
        }

        Rect panel = PanelRect();
        Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        if (SelectedBeamDiagramPanel.BlocksPointer() && !panel.Contains(mouse))
        {
            return;
        }

        float sliderX = panel.x + 132f;
        float sliderWidth = 125f;
        float sliderY = panel.y + 130f;

        bool onX = new Rect(sliderX, sliderY, sliderWidth, 20f).Contains(mouse);
        bool onZ = new Rect(sliderX, sliderY + 24f, sliderWidth, 20f).Contains(mouse);
        if (!onX && !onZ)
        {
            return;
        }

        float value = Mathf.Clamp01((mouse.x - sliderX) / sliderWidth);
        if (onX)
        {
            posX01 = value;
        }
        else
        {
            posZ01 = value;
        }
        StopAutoWalk();
    }

    private void LogDiagnostics(string reason)
    {
        if (!logDiagnostics || Time.unscaledTime < nextDiagnosticLogTime)
        {
            return;
        }

        nextDiagnosticLogTime = Time.unscaledTime + 1f;
        string selected = selectedElement == null
            ? "null"
            : (selectedElement.data == null ? selectedElement.name : selectedElement.data.type);
        string person = personRoot == null ? "null" : personRoot.transform.position.ToString("F3");
        string load = float.IsNaN(lastDiagnosticLoadPosition.x)
            ? "NaN"
            : lastDiagnosticLoadPosition.ToString("F3");
        Debug.Log($"[MobileLoadDiagnostics] reason={reason} updates={diagnosticUpdateCount} " +
                  $"visible={visible} bounds={boundsReady} level={hasLevel} " +
                  $"selected={selected} pos01=({posX01:0.000},{posZ01:0.000}) " +
                  $"loadWorld={load} person={person} " +
                  $"diagrams=A:{showAxial}/S:{showShear}/M:{showMoment}");
    }

    private void InitDefaultLevel()
    {
        StructureData structure = UnityData.Structure;
        if (structure == null || structure.slabs == null || structure.slabs.Length == 0)
        {
            return;
        }

        List<float> levels = new List<float>();
        foreach (SlabData slab in structure.slabs)
        {
            if (slab == null || levels.Contains(slab.z))
            {
                continue;
            }
            levels.Add(slab.z);
        }
        levels.Sort();

        foreach (float level in levels)
        {
            if (ComputePlanBounds(level))
            {
                levelY = level;
                levelLabel = "Nivel " + level.ToString("0.##");
                boundsReady = true;
                hasLevel = true;
                return;
            }
        }
    }

    private void SyncSelectedElement()
    {
        if (cachedPicker == null) cachedPicker = FindObjectOfType<ElementPicker>();
        ElementPicker picker = cachedPicker;
        if (picker == null || picker.Selected == null)
        {
            return;
        }

        ElementSelectable sel = picker.Selected;
        if (sel == lastPickerSelection)
        {
            return;
        }
        lastPickerSelection = sel;
        if (sel.data == null && !sel.isWall)
        {
            return;
        }
        if (sel != selectedElement) placeOnBeamPending = true;
        selectedElement = sel;
        slabBoundsOverride = false;
    }

    private void ComputeLevelAndBounds()
    {
        ElementSelectable sel = selectedElement;
        Vector3 a = sel.startPoint;
        Vector3 b = sel.endPoint;

        if (sel.isWall)
        {
            levelY = Mathf.Max(a.y, b.y);
            levelLabel = "Nivel " + (string.IsNullOrEmpty(sel.wallTop) ? levelY.ToString("0.##") : sel.wallTop);
        }
        else if (sel.data != null && sel.data.type == "columna")
        {
            levelY = Mathf.Max(a.y, b.y);
            levelLabel = !string.IsNullOrEmpty(sel.visualFloor) ? sel.visualFloor : "z=" + levelY.ToString("0.##") + " m";
        }
        else
        {
            levelY = 0.5f * (a.y + b.y);
            levelLabel = !string.IsNullOrEmpty(sel.visualFloor) ? sel.visualFloor : "z=" + levelY.ToString("0.##") + " m";
        }

        boundsReady = ComputePlanBounds(levelY);
    }

    private bool ComputePlanBounds(float y)
    {
        bool found = false;
        planXMin = float.PositiveInfinity;
        planXMax = float.NegativeInfinity;
        planZMin = float.PositiveInfinity;
        planZMax = float.NegativeInfinity;

        StructureData structure = UnityData.Structure;
        if (structure != null && structure.slabs != null)
        {
            foreach (SlabData slab in structure.slabs)
            {
                if (slab == null || Mathf.Abs(slab.z - y) > 0.25f)
                {
                    continue;
                }
                float zx0 = Mathf.Min(slab.x0, slab.x1);
                float zx1 = Mathf.Max(slab.x0, slab.x1);
                float zz0 = Mathf.Min(slab.y0, slab.y1);
                float zz1 = Mathf.Max(slab.y0, slab.y1);
                planXMin = Mathf.Min(planXMin, zx0);
                planXMax = Mathf.Max(planXMax, zx1);
                planZMin = Mathf.Min(planZMin, zz0);
                planZMax = Mathf.Max(planZMax, zz1);
                found = true;
            }
        }

        if (!found)
        {
            foreach (ElementSelectable col in ColumnCandidates())
            {
                if (col == null || col.data == null || col.data.type != "columna")
                {
                    continue;
                }
                float colTopY = Mathf.Max(col.startPoint.y, col.endPoint.y);
                if (Mathf.Abs(colTopY - y) > LEVEL_TOL)
                {
                    continue;
                }
                Vector2 topPlan = TopPlan(col);
                planXMin = Mathf.Min(planXMin, topPlan.x);
                planXMax = Mathf.Max(planXMax, topPlan.x);
                planZMin = Mathf.Min(planZMin, topPlan.y);
                planZMax = Mathf.Max(planZMax, topPlan.y);
                found = true;
            }
        }

        if (!found)
        {
            return false;
        }

        if (planXMax - planXMin < 1e-4f || planZMax - planZMin < 1e-4f)
        {
            planXMax = planXMin + 4f;
            planZMax = planZMin + 4f;
        }

        return true;
    }

    public void SetLoadOnSlabPanel(GameObject slabPanel, Vector3 worldPoint)
    {
        if (slabPanel == null)
        {
            return;
        }
        Transform t = slabPanel.transform;
        Vector3 c = t.position;
        Vector3 s = t.localScale;
        levelY = c.y;
        levelLabel = (!string.IsNullOrEmpty(slabPanel.name) ? slabPanel.name : "Losa") + $" (z={levelY:0.##} m)";
        if (!ComputePlanBounds(levelY))
        {
            planXMin = c.x - s.x * 0.5f;
            planXMax = c.x + s.x * 0.5f;
            planZMin = c.z - s.z * 0.5f;
            planZMax = c.z + s.z * 0.5f;
        }
        posX01 = Mathf.Clamp01((worldPoint.x - planXMin) / Mathf.Max(planXMax - planXMin, 1e-4f));
        posZ01 = Mathf.Clamp01((worldPoint.z - planZMin) / Mathf.Max(planZMax - planZMin, 1e-4f));

        autoWalk = false;
        lastAutoWalkState = false;

        slabBoundsOverride = true;
        boundsReady = true;
        hasLevel = true;
    }

    private Vector2 TopPlan(ElementSelectable col)
    {
        if (col.startPoint.y >= col.endPoint.y)
        {
            return new Vector2(col.startPoint.x, col.startPoint.z);
        }
        return new Vector2(col.endPoint.x, col.endPoint.z);
    }

    private Vector2 LoadPlanPos()
    {
        return new Vector2(
            Mathf.Lerp(planXMin, planXMax, Mathf.Clamp01(posX01)),
            Mathf.Lerp(planZMin, planZMax, Mathf.Clamp01(posZ01)));
    }

    private void UpdateVisuals()
    {
        EnsureObjects();
        AdvanceAutoWalk();
        ComputeSlabDistribution();
        ComputeSupports();

        Vector2 loadPlan = LoadPlanPos();
        Vector3 loadPos = new Vector3(loadPlan.x, levelY, loadPlan.y);
        lastDiagnosticLoadPosition = loadPos;

        UpdatePerson(loadPos);
        UpdateReactionArrows();
        UpdateBeamDiagrams(loadPos);
        // Los diagramas globales muestran solo la combinacion OpenSees: la carga
        // movil no los modifica, por lo que no se regeneran en cada cuadro.
    }

    private List<ElementSelectable> BeamCandidates()
    {
        if (cachedBeams.Count == 0 || Time.unscaledTime >= nextBeamCacheTime)
        {
            cachedBeams.Clear();
            foreach (ElementSelectable e in FindObjectsOfType<ElementSelectable>())
            {
                if (e != null && e.data != null && e.data.type == "viga") cachedBeams.Add(e);
            }
            nextBeamCacheTime = Time.unscaledTime + 2f;
        }
        return cachedBeams;
    }

    private List<Vector4> WallPlanSegments()
    {
        if (wallPlanSegments != null) return wallPlanSegments;
        wallPlanSegments = new List<Vector4>();
        StructureData structure = UnityData.Structure;
        if (structure == null || structure.walls == null || structure.nodes == null) return wallPlanSegments;
        var nodes = new Dictionary<int, NodeData>();
        foreach (NodeData n in structure.nodes) nodes[n.id] = n;
        var seen = new HashSet<string>();
        foreach (WallData w in structure.walls)
        {
            if (w == null || !nodes.TryGetValue(w.nodeI, out NodeData a) || !nodes.TryGetValue(w.nodeJ, out NodeData b)) continue;
            string key = $"{a.x:0.00}|{a.y:0.00}|{b.x:0.00}|{b.y:0.00}";
            if (!seen.Add(key)) continue;   // mismo muro en todos los pisos (z = -4..16)
            wallPlanSegments.Add(new Vector4(a.x, a.y, b.x, b.y));
        }
        return wallPlanSegments;
    }

    // Apoyo mas cercano en cada direccion: 0:+Z, 1:-Z, 2:+X, 3:-X (plano Unity x,z).
    private bool IsOnSlab(Vector2 p)
    {
        StructureData structure = UnityData.Structure;
        if (structure == null || structure.slabs == null) return false;
        foreach (SlabData slab in structure.slabs)
        {
            if (slab == null || Mathf.Abs(slab.z - levelY) > 0.25f) continue;
            if (p.x >= Mathf.Min(slab.x0, slab.x1) - 0.05f && p.x <= Mathf.Max(slab.x0, slab.x1) + 0.05f &&
                p.y >= Mathf.Min(slab.y0, slab.y1) - 0.05f && p.y <= Mathf.Max(slab.y0, slab.y1) + 0.05f)
                return true;
        }
        return false;
    }

    private void ComputeSlabDistribution()
    {
        slabShares.Clear();
        slabSharesValid = false;
        Vector2 p = LoadPlanPos();
        loadOnSlab = IsOnSlab(p);
        if (!loadOnSlab)
        {
            beamLoadFraction = 0f;
            return;
        }
        var best = new SlabShare[4];

        void Consider(Vector2 a, Vector2 b, ElementSelectable beam, string label)
        {
            Vector2 dir = b - a;
            if (dir.magnitude < 0.01f) return;
            bool alongX = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y);
            if (alongX)
            {
                if (Mathf.Abs(a.y - b.y) > 0.05f) return;            // solo apoyos alineados con los ejes
                if (p.x < Mathf.Min(a.x, b.x) - 0.02f || p.x > Mathf.Max(a.x, b.x) + 0.02f) return;
                float d = 0.5f * (a.y + b.y) - p.y;
                int k = d >= 0f ? 0 : 1;
                float ad = Mathf.Abs(d);
                if (best[k] == null || ad < best[k].distance)
                    best[k] = new SlabShare { label = label, beam = beam, distance = ad, t = Mathf.InverseLerp(a.x, b.x, p.x) };
            }
            else
            {
                if (Mathf.Abs(a.x - b.x) > 0.05f) return;
                if (p.y < Mathf.Min(a.y, b.y) - 0.02f || p.y > Mathf.Max(a.y, b.y) + 0.02f) return;
                float d = 0.5f * (a.x + b.x) - p.x;
                int k = d >= 0f ? 2 : 3;
                float ad = Mathf.Abs(d);
                if (best[k] == null || ad < best[k].distance)
                    best[k] = new SlabShare { label = label, beam = beam, distance = ad, t = Mathf.InverseLerp(a.y, b.y, p.y) };
            }
        }

        foreach (ElementSelectable beam in BeamCandidates())
        {
            if (beam == null) continue;
            if (Mathf.Abs(beam.startPoint.y - levelY) > LEVEL_TOL || Mathf.Abs(beam.endPoint.y - levelY) > LEVEL_TOL) continue;
            string tag = !string.IsNullOrEmpty(beam.data.elementTag) ? beam.data.elementTag : beam.data.id.ToString();
            Consider(new Vector2(beam.startPoint.x, beam.startPoint.z), new Vector2(beam.endPoint.x, beam.endPoint.z), beam, tag);
        }
        foreach (Vector4 w in WallPlanSegments())
        {
            Consider(new Vector2(w.x, w.y), new Vector2(w.z, w.w), null, "muro");
        }

        // Reparto por franjas cruzadas (metodo de las franjas): la persona en (x, z)
        // se apoya en una franja en X (entre los apoyos +X/-X) y otra en Z. Cada
        // franja es simplemente apoyada; su rigidez bajo la carga es k = L/(a*b)^2
        // (flecha P*a^2*b^2/(3EIL)). La carga se divide entre franjas en proporcion
        // a k y dentro de cada franja por la regla de la palanca. Asi la viga recibe
        // una fraccion que varia suave con la distancia (100 % sobre la viga, ~50 %
        // a 1 m en un pano de 5 m), en vez de caer a 0 apenas la persona se aleja
        // como pasaba con 1/d^4. En el centro de un pano da alpha = Lz^3/(Lx^3+Lz^3).
        SlabShare onSupport = null;
        foreach (SlabShare share in best)
        {
            if (share != null && share.distance < 0.02f && (onSupport == null || share.distance < onSupport.distance))
                onSupport = share;
        }
        foreach (SlabShare share in best)
        {
            if (share != null) share.fraction = 0f;
        }
        bool zPair = best[0] != null && best[1] != null;
        bool xPair = best[2] != null && best[3] != null;
        if (onSupport != null)
        {
            onSupport.fraction = 1f;
        }
        else if (zPair || xPair)
        {
            float kz = 0f;
            float kx = 0f;
            if (zPair)
            {
                float da = best[0].distance, db = best[1].distance;
                kz = (da + db) / Mathf.Max(da * da * db * db, 1e-8f);
            }
            if (xPair)
            {
                float da = best[2].distance, db = best[3].distance;
                kx = (da + db) / Mathf.Max(da * da * db * db, 1e-8f);
            }
            float alphaZ = kz / (kz + kx);
            float alphaX = 1f - alphaZ;
            if (zPair)
            {
                float lz = best[0].distance + best[1].distance;
                best[0].fraction = alphaZ * best[1].distance / lz;
                best[1].fraction = alphaZ * best[0].distance / lz;
            }
            if (xPair)
            {
                float lx = best[2].distance + best[3].distance;
                best[2].fraction = alphaX * best[3].distance / lx;
                best[3].fraction = alphaX * best[2].distance / lx;
            }
        }
        else
        {
            // Sin un par de apoyos opuestos (borde de losa en voladizo): al mas cercano.
            float sum = 0f;
            foreach (SlabShare share in best)
            {
                if (share == null) continue;
                share.fraction = 1f / Mathf.Pow(Mathf.Max(share.distance, 0.05f), 4f);
                sum += share.fraction;
            }
            if (sum <= 0f) return;
            foreach (SlabShare share in best)
            {
                if (share != null) share.fraction /= sum;
            }
        }
        foreach (SlabShare share in best)
        {
            if (share != null && share.fraction >= 0.001f) slabShares.Add(share);
        }
        slabShares.Sort((x, y) => y.fraction.CompareTo(x.fraction));
        slabSharesValid = slabShares.Count > 0;

        // Fraccion que recibe la viga seleccionada (para el texto del panel, aunque
        // los diagramas de la carga movil esten ocultos).
        beamLoadFraction = 0f;
        beamLoadDistance = 0f;
        foreach (SlabShare share in slabShares)
        {
            if (share.beam != null && share.beam == selectedElement)
            {
                beamLoadFraction = share.fraction;
                beamLoadDistance = share.distance;
                break;
            }
        }
    }

    private List<ElementSelectable> ColumnCandidates()
    {
        // FindObjectsOfType recorre toda la escena: se cachea y se refresca cada 2 s.
        if (cachedColumns.Count == 0 || Time.unscaledTime >= nextColumnCacheTime)
        {
            cachedColumns.Clear();
            foreach (ElementSelectable e in FindObjectsOfType<ElementSelectable>())
            {
                if (e != null && e.data != null && e.data.type == "columna") cachedColumns.Add(e);
            }
            nextColumnCacheTime = Time.unscaledTime + 2f;
        }
        return cachedColumns;
    }

    private void ComputeSupports()
    {
        supports.Clear();
        wallReaction = 0f;
        otherColumnsReaction = 0f;
        var candidates = new List<ColumnSupport>();
        foreach (ElementSelectable col in ColumnCandidates())
        {
            if (col == null || col.data == null || col.data.type != "columna")
            {
                continue;
            }
            float colTopY = Mathf.Max(col.startPoint.y, col.endPoint.y);
            if (Mathf.Abs(colTopY - levelY) > LEVEL_TOL)
            {
                continue;
            }
            Vector2 topPlan = TopPlan(col);
            candidates.Add(new ColumnSupport
            {
                top = new Vector3(topPlan.x, colTopY, topPlan.y),
                plan = topPlan,
                tag = !string.IsNullOrEmpty(col.data.elementTag) ? col.data.elementTag : col.data.id.ToString()
            });
        }
        if (candidates.Count == 0 || !slabSharesValid)
        {
            return;
        }

        ColumnSupport Nearest(Vector2 point)
        {
            ColumnSupport best = null;
            float bestD = float.PositiveInfinity;
            foreach (ColumnSupport c in candidates)
            {
                float d = PlanDistSqr(c.plan, point);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        foreach (SlabShare share in slabShares)
        {
            float f = loadKN * share.fraction;
            if (share.beam == null)
            {
                wallReaction += f;    // la parte que toma un muro baja por el muro
                continue;
            }
            // Reacciones de viga empotrada-empotrada con carga puntual f en a = t*L.
            ElementSelectable beam = share.beam;
            float length = UnityData.TryGetFrameGeometry(beam.data.id, out var frame)
                ? (float)frame.Length : Mathf.Max((beam.endPoint - beam.startPoint).magnitude, 0.001f);
            float a = share.t * length;
            float b = length - a;
            float rI = f * b * b * (length + 2f * a) / (length * length * length);
            float rJ = f - rI;
            ColumnSupport cI = Nearest(new Vector2(beam.startPoint.x, beam.startPoint.z));
            ColumnSupport cJ = Nearest(new Vector2(beam.endPoint.x, beam.endPoint.z));
            if (cI != null) cI.reaction += rI;
            if (cJ != null) cJ.reaction += rJ;
        }

        candidates.Sort((x, y) => y.reaction.CompareTo(x.reaction));
        foreach (ColumnSupport c in candidates)
        {
            if (c.reaction > 0.01f && supports.Count < MAX_SUPPORT_COLUMNS)
            {
                supports.Add(c);
            }
            else if (c.reaction > 0.01f)
            {
                otherColumnsReaction += c.reaction;
            }
        }
    }

    private float PlanDistSqr(Vector2 a, Vector2 b)
    {
        float dx = a.x - b.x;
        float dz = a.y - b.y;
        return dx * dx + dz * dz;
    }

    private void AdvanceAutoWalk()
    {
        if (!autoWalk)
        {
            lastAutoWalkState = false;
            return;
        }

        bool onBeam = selectedElement != null && selectedElement.data != null && selectedElement.data.type == "viga";
        if (!lastAutoWalkState)
        {
            // Arranca desde la posicion actual para que no haya saltos.
            autoWalkClock = onBeam ? ProjectOnSelectedBeam01(LoadPlanPos()) : 0f;
            walkClockX = posX01;
            walkClockZ = posZ01;
            lastAutoWalkState = true;
        }

        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        if (onBeam)
        {
            // Recorre la viga de I a J y vuelve (PingPong mantiene 0..1).
            autoWalkClock += dt * autoWalkSpeed01;
            float u = Mathf.PingPong(autoWalkClock, 1f);
            Vector3 a = selectedElement.startPoint;
            Vector3 b = selectedElement.endPoint;
            Vector2 p = Vector2.Lerp(new Vector2(a.x, a.z), new Vector2(b.x, b.z), u);
            posX01 = Mathf.Clamp01((p.x - planXMin) / Mathf.Max(planXMax - planXMin, 1e-4f));
            posZ01 = Mathf.Clamp01((p.y - planZMin) / Mathf.Max(planZMax - planZMin, 1e-4f));
        }
        else
        {
            // Recorrido en zigzag por la losa (frecuencias distintas en X y Z).
            walkClockX += dt * autoWalkSpeed01;
            walkClockZ += dt * autoWalkSpeed01 * 0.37f;
            posX01 = Mathf.PingPong(walkClockX, 1f);
            posZ01 = Mathf.PingPong(walkClockZ, 1f);
        }
    }

    private float ProjectOnSelectedBeam01(Vector2 plan)
    {
        if (selectedElement == null) return 0f;
        Vector2 i = new Vector2(selectedElement.startPoint.x, selectedElement.startPoint.z);
        Vector2 j = new Vector2(selectedElement.endPoint.x, selectedElement.endPoint.z);
        Vector2 ij = j - i;
        return ij.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(plan - i, ij) / ij.sqrMagnitude) : 0.5f;
    }

    private void StopAutoWalk()
    {
        autoWalk = false;
        lastAutoWalkState = false;
        walkClockX = posX01;
        walkClockZ = posZ01;
    }

    private void UpdatePerson(Vector3 loadPos)
    {
        if (personRoot == null)
        {
            return;
        }

        float feetY = levelY;
        Vector2 plan = new Vector2(loadPos.x, loadPos.z);

        if (!float.IsNaN(lastPersonPlan.x))
        {
            Vector2 delta = plan - lastPersonPlan;
            float moved = delta.magnitude;
            if (moved > 0.0001f)
            {
                personFacing = delta / moved;
                personMoving = true;
            }
        }
        lastPersonPlan = plan;

        Vector3 dir3 = new Vector3(personFacing.x, 0f, personFacing.y);
        if (dir3.sqrMagnitude > 0.001f)
        {
            personRoot.transform.rotation = Quaternion.LookRotation(dir3.normalized, Vector3.up);
        }

        if (personMoving)
        {
            walkSpeed = Mathf.MoveTowards(walkSpeed, 1f, Time.deltaTime * 4f);
        }
        else
        {
            walkSpeed = Mathf.MoveTowards(walkSpeed, 0f, Time.deltaTime * 6f);
        }

        personMoving = false;

        float bob = 1f + Mathf.Sin(walkPhase) * 0.03f * walkSpeed;
        personRoot.transform.position = new Vector3(loadPos.x, feetY, loadPos.z);
        personRoot.transform.localScale = new Vector3(bob, 2f - bob, bob);

        float swing = Mathf.Sin(walkPhase) * 42f * walkSpeed;
        walkPhase += Time.deltaTime * 10f * Mathf.Max(walkSpeed, 0.25f);

        if (personLeftArm != null) personLeftArm.localRotation = Quaternion.Euler(28f + swing, 0f, 16f);
        if (personRightArm != null) personRightArm.localRotation = Quaternion.Euler(-28f - swing, 0f, -16f);
        if (personLeftLeg != null) personLeftLeg.localRotation = Quaternion.Euler(-22f - swing * 0.8f, 0f, 5f);
        if (personRightLeg != null) personRightLeg.localRotation = Quaternion.Euler(22f + swing * 0.8f, 0f, -5f);
        if (personBody != null) personBody.localRotation = Quaternion.Euler(Mathf.Sin(walkPhase) * 4f * walkSpeed, 0f, 0f);
        if (personHead != null) personHead.localRotation = Quaternion.Euler(0f, Mathf.Sin(walkPhase * 0.5f) * 3f, 0f);
    }

    private void UpdateReactionArrows()
    {
        for (int i = 0; i < reactionArrows.Count; i++)
        {
            GameObject go = reactionArrows[i];
            if (i < supports.Count)
            {
                go.SetActive(true);
                ColumnSupport support = supports[i];
                float length = Mathf.Max(0.15f, support.reaction * 0.004f);
                Vector3 a = support.top + Vector3.up * 0.05f;
                Vector3 b = a - Vector3.up * length;
                LineRenderer line = go.GetComponent<LineRenderer>();
                line.positionCount = 2;
                line.SetPosition(0, a);
                line.SetPosition(1, b);
            }
            else
            {
                go.SetActive(false);
            }
        }
    }

    private void UpdateBeamDiagrams(Vector3 loadPos)
    {
        ElementSelectable el = selectedElement;
        bool isViga = el != null && el.data != null && el.data.type == "viga";
        bool isColumna = el != null && el.data != null && el.data.type == "columna";
        bool active = isViga || isColumna;

        axialLine.SetActive(active && showAxial);
        shearLine.SetActive(isViga && showShear);
        momentLine.SetActive(isViga && showMoment);
        axialBaseLine.SetActive(active && showAxial);
        shearBaseLine.SetActive(isViga && showShear);
        momentBaseLine.SetActive(isViga && showMoment);

        if (!active)
        {
            return;
        }

        Vector3 a = el.startPoint;
        Vector3 b = el.endPoint;
        Vector3 axis = b - a;
        float length = Mathf.Max(axis.magnitude, 0.001f);
        Vector3 dir = axis / length;
        Vector3 lateral = Vector3.Cross(dir, Vector3.up).normalized;
        if (lateral.sqrMagnitude < 0.01f) lateral = Vector3.right;

        if (diagramController == null)
        {
            diagramController = FindObjectOfType<DiagramController>();
        }

        DrawModePolylines(axialBaseLine, axialLine, a, axis, length, lateral, 0.18f, 0.08f, 0.6f,
            "Axial", s => ExtraAt(el, "Axial", s, length), active && showAxial);
        DrawModePolylines(shearBaseLine, shearLine, a, axis, length, lateral, 0.35f, 0f, 0.7f,
            "Shear", s => ExtraAt(el, "Shear", s, length), isViga && showShear);
        DrawModePolylines(momentBaseLine, momentLine, a, axis, length, lateral, 0.70f, 0f, 0.8f,
            "Moment", s => ExtraAt(el, "Moment", s, length), isViga && showMoment);
    }

    public float ExtraAt(ElementSelectable element, string modeName, float s, float length)
    {
        if (!visible || !boundsReady || !hasLevel)
        {
            return 0f;
        }
        if (element == null || element != selectedElement || element.data == null)
        {
            return 0f;
        }

        bool isViga = element.data.type == "viga";
        bool isColumna = element.data.type == "columna";
        if (!isViga && !isColumna)
        {
            return 0f;
        }

        Vector3 a = element.startPoint;
        Vector3 b = element.endPoint;
        float beamLength = UnityData.TryGetFrameGeometry(element.data.id, out var frame)
            ? (float)frame.Length : Mathf.Max(length, 0.001f);
        Vector2 loadPlan = LoadPlanPos();
        Vector2 beamI = new Vector2(a.x, a.z);
        Vector2 beamJ = new Vector2(b.x, b.z);
        Vector2 ab = beamJ - beamI;
        float denominator = ab.sqrMagnitude;
        float t = denominator > 0.0001f
            ? Mathf.Clamp01(Vector2.Dot(loadPlan - beamI, ab) / denominator)
            : 0.5f;

        if (modeName == "Axial")
        {
            return isColumna ? -ColumnExtraAxial() : 0f; // N is tension-positive.
        }
        if (!isViga) return 0f;
        // Solo la parte de la carga que llega a ESTA viga: regla de la palanca
        // en una franja de losa de ancho tributario b = A_trib / L (1 en la viga,
        // 0 a una distancia b). Antes toda la carga iba a la viga seleccionada
        // aunque la persona estuviera lejos de ella.
        float fraction;
        if (slabSharesValid)
        {
            // La persona esta sobre la losa: la viga recibe su parte del reparto
            // losa -> apoyos, aplicada en la proyeccion de la persona sobre la viga.
            fraction = 0f;
            float distance = 0f;
            foreach (SlabShare share in slabShares)
            {
                if (share.beam == element)
                {
                    fraction = share.fraction;
                    t = share.t;
                    distance = share.distance;
                    break;
                }
            }
            beamLoadFraction = fraction;
            beamLoadDistance = distance;
        }
        else
        {
            // Fuera de las losas del nivel: solo carga la viga si la persona esta sobre ella.
            Vector2 foot = beamI + ab * t;
            float distance = Vector2.Distance(loadPlan, foot);
            fraction = distance <= 0.3f ? 1f : 0f;
            beamLoadFraction = fraction;
            beamLoadDistance = distance;
        }
        if (fraction <= 0f) return 0f;
        FrameSectionForces contribution = FrameForces.EvaluateFixedFixedPointLoad(loadKN * fraction, beamLength, t, s);
        if (modeName == "Shear") return contribution.Vz;
        if (modeName == "Moment") return contribution.My;
        return 0f;
    }

    private float ColumnExtraAxial()
    {
        if (selectedElement == null || selectedElement.data == null)
        {
            return 0f;
        }
        string tag = !string.IsNullOrEmpty(selectedElement.data.elementTag)
            ? selectedElement.data.elementTag
            : selectedElement.data.id.ToString();
        foreach (ColumnSupport support in supports)
        {
            if (support.tag == tag)
            {
                return support.reaction;
            }
        }
        return 0f;
    }

    private void DrawModePolylines(GameObject baseObj, GameObject totalObj,
        Vector3 a, Vector3 axis, float length, Vector3 lateral,
        float lateralOffset, float upOffset, float amplitude, string modeName,
        System.Func<float, float> extraAt, bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        const int pointCount = 25;
        float[] baseVals = new float[pointCount];
        float[] totalVals = new float[pointCount];
        float maxAbs = 1e-6f;
        for (int i = 0; i < pointCount; i++)
        {
            float s = i / (float)(pointCount - 1);
            float baseVal = diagramController != null
                ? diagramController.ValueAt(selectedElement, modeName, s, length)
                : 0f;
            float extraVal = extraAt != null ? extraAt(s) : 0f;
            baseVals[i] = baseVal;
            totalVals[i] = baseVal + extraVal;
            maxAbs = Mathf.Max(maxAbs, Mathf.Abs(baseVal), Mathf.Abs(totalVals[i]));
        }
        if (maxAbs < 1e-6f)
        {
            maxAbs = 1f;
        }

        float drawSign = (modeName == "Moment") ? -1f : 1f;
        LineRenderer baseLr = baseObj.GetComponent<LineRenderer>();
        baseLr.positionCount = pointCount;
        for (int i = 0; i < pointCount; i++)
        {
            float s = i / (float)(pointCount - 1);
            Vector3 p = a + axis * s + lateral * lateralOffset + Vector3.up * (upOffset + drawSign * baseVals[i] / maxAbs * amplitude);
            baseLr.SetPosition(i, p);
        }

        LineRenderer totalLr = totalObj.GetComponent<LineRenderer>();
        totalLr.positionCount = pointCount;
        for (int i = 0; i < pointCount; i++)
        {
            float s = i / (float)(pointCount - 1);
            Vector3 p = a + axis * s + lateral * lateralOffset + Vector3.up * (upOffset + drawSign * totalVals[i] / maxAbs * amplitude);
            totalLr.SetPosition(i, p);
        }
    }

    private void OnGUI()
    {
        EnsureStyles();

        int prevDepth = GUI.depth;
        GUI.depth = -200;

        Rect panel = PanelLayout.Apply("MobileLoad", PanelRect());
        float w = panel.width;
        float h = panel.height;
        GUI.BeginGroup(panel);

        float x = 0f;
        float y = 0f;
        GUI.Label(new Rect(0f, 0f, w, h), "Sidequest | Carga movil", boxStyle);

        float iy = y + 26f;
        if (GUI.Button(new Rect(x + 12f, iy, 150f, 20f), (visible ? "[x] " : "[ ] ") + "Activar carga movil"))
        {
            visible = !visible;
        }
        if (GUI.Button(new Rect(x + 170f, iy, 196f, 20f), (autoWalk ? "[x] " : "[ ] ") + "Caminar automatico"))
        {
            if (autoWalk) StopAutoWalk();
            else autoWalk = true;
        }
        iy += 24f;

        GUI.Label(new Rect(x + 12f, iy, 90f, 20f), "Carga P [kN]", labelStyle);
        if (GUI.Button(new Rect(x + 102f, iy, 26f, 20f), "-")) loadKN = Mathf.Max(0f, loadKN - 10f);
        loadKN = GUI.HorizontalSlider(new Rect(x + 132f, iy + 5f, 125f, 18f), loadKN, 0f, 300f);
        GUI.Box(new Rect(x + 260f, iy, 44f, 20f), GUIContent.none, valueStyle);
        GUI.Label(new Rect(x + 263f, iy + 1f, 40f, 18f), loadKN.ToString("0"), valueStyle);
        if (GUI.Button(new Rect(x + 304f, iy, 22f, 20f), "+")) loadKN = Mathf.Min(300f, loadKN + 10f);
        iy += 24f;

        GUI.Label(new Rect(x + 12f, iy, w - 24f, 20f), "Nivel: " + levelLabel, labelStyle);
        iy += 22f;

        bool beamSelected = selectedElement != null && selectedElement.data != null && selectedElement.data.type == "viga";
        bool beamOtherLevel = beamSelected &&
            Mathf.Abs(0.5f * (selectedElement.startPoint.y + selectedElement.endPoint.y) - levelY) > LEVEL_TOL;
        string beamInfo = !beamSelected
            ? "Selecciona una viga y haz click en la losa para ubicar a la persona."
            : beamOtherLevel
                ? "La viga seleccionada esta en otro nivel: la persona no la carga."
                : $"Viga seleccionada (empotrada, Vz/My): recibe {beamLoadFraction * 100f:0}% de P = {loadKN * beamLoadFraction:0.0} kN. Efecto en el panel de diagramas (gris = sin persona).";
        GUI.Label(new Rect(x + 12f, iy, w - 24f, 32f), beamInfo, labelStyle);
        iy += 34f;

        if (boundsReady)
        {
            DrawPlanSliders(x, ref iy, w);
        }
        else
        {
            GUI.Label(new Rect(x + 12f, iy, w - 24f, 36f), "Click en una losa o elemento para ubicar la carga.", labelStyle);
            GUI.EndGroup();
            GUI.depth = prevDepth;
            return;
        }

        if (selectedElement != null && selectedElement.data != null && (selectedElement.data.type == "viga" || selectedElement.data.type == "columna"))
        {
            GUI.Label(new Rect(x + 12f, iy, 70f, 20f), "Diagramas", labelStyle);
            bool esViga = selectedElement.data.type == "viga";
            GUI.Label(new Rect(x + 82f, iy, 62f, 20f), (showAxial ? "[x] " : "[ ] ") + "Axial", labelStyle);
            GUI.Label(new Rect(x + 146f, iy, 62f, 20f), (showShear ? "[x] " : "[ ] ") + "Vz", labelStyle);
            GUI.Label(new Rect(x + 210f, iy, 82f, 20f), (showMoment ? "[x] " : "[ ] ") + "My", labelStyle);
            if (!esViga)
            {
                showShear = false;
                showMoment = false;
            }
            iy += 24f;
        }

        DrawDistributionInfo(x, ref iy, w);

        GUI.EndGroup();
        GUI.depth = prevDepth;
    }

    private void DrawPlanSliders(float x, ref float iy, float w)
    {
        DrawPositionSlider(x, iy, "Posicion X", ref posX01);
        iy += 24f;
        DrawPositionSlider(x, iy, "Posicion Z", ref posZ01);
        iy += 26f;
    }

    private void DrawPositionSlider(float x, float iy, string label, ref float value)
    {
        float before = value;
        Rect sliderArea = new Rect(x + 102f, iy, 224f, 20f);
        if ((Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag)
            && Event.current.button == 0 && sliderArea.Contains(Event.current.mousePosition))
        {
            StopAutoWalk();
        }
        GUI.Label(new Rect(x + 12f, iy, 90f, 20f), label, labelStyle);
        if (GUI.Button(new Rect(x + 102f, iy, 26f, 20f), "-")) value = Mathf.Max(0f, value - 0.05f);
        float next = GUI.HorizontalSlider(new Rect(x + 132f, iy + 5f, 125f, 18f), value, 0f, 1f);
        value = next;
        GUI.Box(new Rect(x + 260f, iy, 44f, 20f), GUIContent.none, valueStyle);
        GUI.Label(new Rect(x + 263f, iy + 1f, 40f, 18f), (value * 100f).ToString("0") + "%", valueStyle);
        if (GUI.Button(new Rect(x + 304f, iy, 22f, 20f), "+")) value = Mathf.Min(1f, value + 0.05f);
        if (Mathf.Abs(value - before) > 0.0001f && autoWalk)
        {
            StopAutoWalk();
        }
    }

    private void DrawDistributionInfo(float x, ref float iy, float w)
    {
        if (slabSharesValid)
        {
            GUI.Label(new Rect(x + 12f, iy, w - 24f, 18f), "Reparto losa -> apoyos (franjas cruzadas)", labelStyle);
            iy += 18f;
            float total = 0f;
            foreach (SlabShare share in slabShares)
            {
                bool selected = share.beam != null && share.beam == selectedElement;
                string mark = selected ? " <= seleccionada" : "";
                GUI.Label(new Rect(x + 12f, iy, w - 24f, 16f),
                    $"{share.label}: {share.fraction * 100f:0}% = {loadKN * share.fraction:0.0} kN (d={share.distance:0.00} m){mark}", labelStyle);
                iy += 16f;
                total += share.fraction;
            }
            GUI.Label(new Rect(x + 12f, iy, w - 24f, 16f), $"Conservacion: {total * 100f:0.0}% de P", labelStyle);
            iy += 22f;
        }

        if (!loadOnSlab)
        {
            GUI.Label(new Rect(x + 12f, iy, w - 24f, 32f), "La persona esta fuera de las losas de este nivel: no carga vigas.", labelStyle);
            iy += 34f;
            return;
        }
        if (supports.Count == 0 && wallReaction <= 0f)
        {
            GUI.Label(new Rect(x + 12f, iy, w - 24f, 18f), "Sin columnas en este nivel.", labelStyle);
            iy += 20f;
            return;
        }

        GUI.Label(new Rect(x + 12f, iy, w - 24f, 18f), "Columnas (reacciones de las vigas cargadas)", labelStyle);
        iy += 18f;
        float sum = wallReaction + otherColumnsReaction;
        foreach (ColumnSupport support in supports)
        {
            GUI.Label(new Rect(x + 12f, iy, w - 24f, 16f), $"{support.tag}: {support.reaction:0.0} kN", labelStyle);
            iy += 16f;
            sum += support.reaction;
        }
        if (otherColumnsReaction > 0.01f || wallReaction > 0.01f)
        {
            GUI.Label(new Rect(x + 12f, iy, w - 24f, 16f), $"Otras columnas: {otherColumnsReaction:0.0} kN | muros: {wallReaction:0.0} kN", labelStyle);
            iy += 16f;
        }
        GUI.Label(new Rect(x + 12f, iy, w - 24f, 16f), $"Conservacion: suma={sum:0.00} kN | error={Mathf.Abs(loadKN - sum):0.000}", labelStyle);
        iy += 20f;
    }

    private void EnsureObjects()
    {
        if (!staleObjectsCleared)
        {
            ClearStaleGeneratedObjects();
            staleObjectsCleared = true;
        }
        EnsurePerson();
        EnsureReactionArrows();
        if (shearLine == null)
        {
            shearLine = CreateLineObject("CargaMovil_Corte", new Color(1f, 0.55f, 0f));
        }
        shearLine.SetActive(showShear);
        if (momentLine == null)
        {
            momentLine = CreateLineObject("CargaMovil_Momento", Color.magenta);
        }
        momentLine.SetActive(showMoment);
        if (axialLine == null)
        {
            axialLine = CreateLineObject("CargaMovil_Axial", Color.red);
        }
        axialLine.SetActive(showAxial);

        if (shearBaseLine == null)
        {
            shearBaseLine = CreateLineObject("CargaMovil_CorteBase", new Color(1f, 0.75f, 0.45f, 0.7f));
        }
        shearBaseLine.SetActive(showShear);
        if (momentBaseLine == null)
        {
            momentBaseLine = CreateLineObject("CargaMovil_MomentoBase", new Color(1f, 0.7f, 1f, 0.7f));
        }
        momentBaseLine.SetActive(showMoment);
        if (axialBaseLine == null)
        {
            axialBaseLine = CreateLineObject("CargaMovil_AxialBase", new Color(1f, 0.6f, 0.6f, 0.7f));
        }
        axialBaseLine.SetActive(showAxial);
    }

    private void EnsurePerson()
    {
        if (personRoot != null)
        {
            personRoot.SetActive(true);
            return;
        }

        personRoot = new GameObject("CargaMovil_Persona");
        personRoot.transform.SetParent(transform);

        Material shirt = CreateMaterial(new Color(0.1f, 0.35f, 1f));
        Material skin = CreateMaterial(new Color(1f, 0.74f, 0.52f));
        Material pants = CreateMaterial(new Color(0.08f, 0.08f, 0.12f));

        personBody = CreatePersonPart("Cuerpo", PrimitiveType.Capsule, new Vector3(0f, 0.74f, 0f), new Vector3(0.28f, 0.44f, 0.28f), Quaternion.identity, shirt).transform;
        personHead = CreatePersonPart("Cabeza", PrimitiveType.Sphere, new Vector3(0f, 1.32f, 0f), new Vector3(0.34f, 0.34f, 0.34f), Quaternion.identity, skin).transform;
        personLeftArm = CreatePersonPart("Brazo_I", PrimitiveType.Cylinder, new Vector3(-0.25f, 0.80f, 0f), new Vector3(0.06f, 0.38f, 0.06f), Quaternion.Euler(25f, 0f, 14f), skin).transform;
        personRightArm = CreatePersonPart("Brazo_J", PrimitiveType.Cylinder, new Vector3(0.25f, 0.80f, 0f), new Vector3(0.06f, 0.38f, 0.06f), Quaternion.Euler(-25f, 0f, -14f), skin).transform;
        personLeftLeg = CreatePersonPart("Pierna_I", PrimitiveType.Cylinder, new Vector3(-0.11f, 0.28f, 0f), new Vector3(0.075f, 0.40f, 0.075f), Quaternion.Euler(-18f, 0f, 4f), pants).transform;
        personRightLeg = CreatePersonPart("Pierna_J", PrimitiveType.Cylinder, new Vector3(0.11f, 0.28f, 0f), new Vector3(0.075f, 0.40f, 0.075f), Quaternion.Euler(18f, 0f, -4f), pants).transform;
    }

    private GameObject CreatePersonPart(string name, PrimitiveType primitive, Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(primitive);
        part.name = "CargaMovil_" + name;
        part.transform.SetParent(personRoot.transform);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;
        part.transform.localScale = localScale;
        part.GetComponent<Renderer>().material = material;
        DestroyCollider(part);
        return part;
    }

    private void EnsureReactionArrows()
    {
        while (reactionArrows.Count < MAX_SUPPORT_COLUMNS)
        {
            GameObject arrowObj = CreateLineObject("CargaMovil_Reaccion_" + reactionArrows.Count, new Color(1f, 0.35f, 0.1f));
            reactionArrows.Add(arrowObj);
        }
    }

    private void ClearStaleGeneratedObjects()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child == null || !IsStaleGeneratedObject(child.name))
            {
                continue;
            }
            if (IsLiveGeneratedObject(child.gameObject))
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    private bool IsLiveGeneratedObject(GameObject go)
    {
        return go == personRoot || go == axialLine || go == shearLine || go == momentLine ||
               go == axialBaseLine || go == shearBaseLine || go == momentBaseLine ||
               reactionArrows.Contains(go);
    }

    private bool IsStaleGeneratedObject(string objectName)
    {
        return objectName.StartsWith("CargaMovil_Persona") ||
               objectName.StartsWith("CargaMovil_Corte") ||
               objectName.StartsWith("CargaMovil_Momento") ||
               objectName.StartsWith("CargaMovil_Axial") ||
               objectName.StartsWith("CargaMovil_Reaccion") ||
               objectName.StartsWith("CargaMovil_Cuerpo") ||
               objectName.StartsWith("CargaMovil_Cabeza") ||
               objectName.StartsWith("CargaMovil_Brazo") ||
               objectName.StartsWith("CargaMovil_Pierna") ||
               objectName.StartsWith("CargaMovil_Punto") ||
               objectName.StartsWith("CargaMovil_Flecha") ||
               objectName.StartsWith("CargaMovil_Impacto") ||
               objectName.StartsWith("CargaMovil_Posicion");
    }

    private void ClearVisuals()
    {
        if (personRoot != null) personRoot.SetActive(false);
        if (shearLine != null) shearLine.SetActive(false);
        if (momentLine != null) momentLine.SetActive(false);
        if (axialLine != null) axialLine.SetActive(false);
        if (shearBaseLine != null) shearBaseLine.SetActive(false);
        if (momentBaseLine != null) momentBaseLine.SetActive(false);
        if (axialBaseLine != null) axialBaseLine.SetActive(false);
        foreach (GameObject go in reactionArrows)
        {
            if (go != null) go.SetActive(false);
        }
        supports.Clear();
    }

    private GameObject CreateLineObject(string name, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false); // se limpia junto con el controlador
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.material = CreateMaterial(color);
        line.startWidth = 0.08f;
        line.endWidth = 0.08f;
        line.useWorldSpace = true;
        return go;
    }

    private Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        Material material = new Material(shader);
        material.color = color;
        return material;
    }

    private void DestroyCollider(GameObject go)
    {
        Collider col = go.GetComponent<Collider>();
        if (col == null) return;
        if (Application.isPlaying)
        {
            Destroy(col);
        }
        else
        {
            DestroyImmediate(col);
        }
    }

    private void EnsureStyles()
    {
        if (boxStyle != null) return;
        boxStyle = new GUIStyle(GUI.skin.box);
        panelBg = MakeTexture(new Color(0.035f, 0.04f, 0.055f, 0.96f));
        boxStyle.normal.background = panelBg;
        boxStyle.normal.textColor = new Color(0.85f, 0.95f, 1f);
        boxStyle.fontSize = 13;
        boxStyle.fontStyle = FontStyle.Bold;

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = Color.white;
        labelStyle.fontSize = 13;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.wordWrap = true;

        valueBg = MakeTexture(new Color(0.0f, 0.0f, 0.0f, 0.55f));
        valueStyle = new GUIStyle(GUI.skin.label);
        valueStyle.normal.background = valueBg;
        valueStyle.normal.textColor = Color.white;
        valueStyle.fontSize = 13;
        valueStyle.fontStyle = FontStyle.Bold;
        valueStyle.alignment = TextAnchor.MiddleCenter;
        valueStyle.padding = new RectOffset(2, 2, 0, 0);
    }

    private Texture2D MakeTexture(Color color)
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }
}
