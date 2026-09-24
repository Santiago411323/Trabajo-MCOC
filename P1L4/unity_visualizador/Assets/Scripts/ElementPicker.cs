using UnityEngine;

public class ElementPicker : MonoBehaviour
{
    public Camera cam;
    public float maxDistance = 500f;
    public LayerMask selectableLayer = ~0;

    public ElementSelectable Selected { get; private set; }
    private Vector3 lastHitPoint;

    [Header("Info Panel")]
    public Vector2 panelOffset = new Vector2(24f, 18f);
    public Vector2 panelMinSize = new Vector2(380f, 0f);
    public float panelMaxWidthRatio = 0.42f;
    public int panelFontSize = 11;
    public float panelPaddingX = 18f;
    public float panelLineSpacing = 2f;
    public float panelSectionSpacing = 12f;

    private GUIStyle boxStyle;
    private GUIStyle labelStyle;
    private GUIStyle titleStyle;
    private GUIStyle headerStyle;
    private Vector2 scroll;

    void Awake()
    {
        if (cam == null)
        {
            cam = Camera.main;
        }
    }

    void Update()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        if (Input.GetMouseButtonDown(0) && IsMouseOverViewerGui())
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, selectableLayer, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            ElementSelectable selectable = null;
            RaycastHit selectableHit = default(RaycastHit);
            foreach (RaycastHit candidate in hits)
            {
                selectable = candidate.collider.GetComponent<ElementSelectable>();
                if (selectable == null)
                {
                    selectable = candidate.collider.GetComponentInParent<ElementSelectable>();
                }
                if (selectable != null)
                {
                    selectableHit = candidate;
                    break;
                }
            }

            if (selectable != null)
            {
                Selected = selectable;
                lastHitPoint = selectableHit.point;
                scroll = Vector2.zero;

                if (selectedElement != null)
                {
                    selectedElement.OnDeselected();
                }
                selectedElement = selectable;
                selectable.OnSelected();

                SetInfoSelection(null);

                if (!string.IsNullOrEmpty(selectedElement.pmSectionId))
                {
                    var pmPanel = FindObjectOfType<PMPanel>();
                    if (pmPanel != null)
                    {
                        pmPanel.ShowPMForElement(selectedElement);
                    }
                }
                var mobileLoad = FindObjectOfType<MobileLoadController>();
                if (mobileLoad != null)
                {
                    mobileLoad.SetSelectedElement(selectedElement);
                }
                return;
            }

foreach (RaycastHit candidate in hits)
            {
                var info = candidate.collider.GetComponent<InfoSelectable>();
                if (info == null)
                {
                    info = candidate.collider.GetComponentInParent<InfoSelectable>();
                }
                if (info != null)
                {
                    SetElementSelection(null);
                    selectedInfo = info;
                    scroll = Vector2.zero;
                    if (candidate.collider.name.StartsWith("Losa_"))
                    {
                        var mobileLoad = FindObjectOfType<MobileLoadController>();
                        if (mobileLoad != null)
                        {
                            mobileLoad.SetLoadOnSlabPanel(candidate.collider.gameObject, candidate.point);
                        }
                    }
                    return;
                }
            }

            Vector3 screenHit;
            ElementSelectable nearby = FindSelectableNearScreen(Input.mousePosition, out screenHit);
            if (nearby != null)
            {
                lastHitPoint = screenHit;
                scroll = Vector2.zero;
                SetInfoSelection(null);
                SetElementSelection(nearby);
                nearby.OnSelected();

                var pmPanel = FindObjectOfType<PMPanel>();
                if (pmPanel != null && !string.IsNullOrEmpty(nearby.pmSectionId))
                {
                    pmPanel.ShowPMForElement(nearby);
                }
                var mobileLoad = FindObjectOfType<MobileLoadController>();
                if (mobileLoad != null)
                {
                    mobileLoad.SetSelectedElement(nearby);
                }
                return;
            }

            SetElementSelection(null);
            SetInfoSelection(null);
        }
    }

    private ElementSelectable FindSelectableNearScreen(Vector3 mousePosition, out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;
        ElementSelectable best = null;
        float bestDistance = 22f;
        Vector2 mouse = new Vector2(mousePosition.x, Screen.height - mousePosition.y);

        foreach (ElementSelectable candidate in FindObjectsOfType<ElementSelectable>())
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 a = cam.WorldToScreenPoint(candidate.startPoint);
            Vector3 b = cam.WorldToScreenPoint(candidate.endPoint);
            if (a.z <= 0f && b.z <= 0f)
            {
                continue;
            }

            Vector2 av = new Vector2(a.x, Screen.height - a.y);
            Vector2 bv = new Vector2(b.x, Screen.height - b.y);
            Vector2 ab = bv - av;
            float denominator = ab.sqrMagnitude;
            float t = denominator > 0.0001f
                ? Mathf.Clamp01(Vector2.Dot(mouse - av, ab) / denominator)
                : 0.5f;
            float distance = Vector2.Distance(mouse, Vector2.Lerp(av, bv, t));
            if (distance >= bestDistance)
            {
                continue;
            }

            best = candidate;
            worldPoint = Vector3.Lerp(candidate.startPoint, candidate.endPoint, t);
            bestDistance = distance;
        }

        return best;
    }

    private ElementSelectable selectedElement;
    private InfoSelectable selectedInfo;

    private bool IsMouseOverViewerGui()
    {
        Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

StructureViewer viewer = FindObjectOfType<StructureViewer>();
        if (viewer != null && viewer.IsTopBarVisible() && viewer.GetTopBarRect().Contains(guiMouse))
        {
            return true;
        }
        if (viewer != null && viewer.IsLeftPanelVisible() && viewer.GetLeftPanelRect().Contains(guiMouse))
        {
            return true;
        }

        MobileLoadController mobileLoad = FindObjectOfType<MobileLoadController>();
        if (mobileLoad != null && mobileLoad.IsPanelVisible() && MobileLoadController.PanelRect().Contains(guiMouse))
        {
            return true;
        }

        PMPanel pmPanel = FindObjectOfType<PMPanel>();
        if (pmPanel != null && pmPanel.IsPanelVisible() && pmPanel.GetPanelRect().Contains(guiMouse))
        {
            return true;
        }
        if (SelectedDiagramPanel.ButtonRect().Contains(guiMouse) || SelectedDiagramPanel.PanelRect().Contains(guiMouse))
        {
            return true;
        }
        return false;
    }

    public void SelectElement(ElementSelectable sel, bool centerCamera)
    {
        if (sel == null)
        {
            SetElementSelection(null);
            return;
        }

        lastHitPoint = (sel.startPoint + sel.endPoint) * 0.5f;
        scroll = Vector2.zero;
        SetInfoSelection(null);
        SetElementSelection(sel);
        sel.OnSelected();

        var pmPanel = FindObjectOfType<PMPanel>();
        if (!string.IsNullOrEmpty(sel.pmSectionId))
        {
            if (pmPanel != null)
            {
                pmPanel.ShowPMForElement(sel);
            }
        }
        var mobileLoad = FindObjectOfType<MobileLoadController>();
        if (mobileLoad != null)
        {
            mobileLoad.SetSelectedElement(sel);
        }

        if (centerCamera && cam != null)
        {
            var orbit = cam.GetComponent<OrbitCamera>();
            if (orbit != null)
            {
                float length = Mathf.Max((sel.endPoint - sel.startPoint).magnitude, 8f);
                orbit.FocusOn(lastHitPoint, Mathf.Clamp(length * 4f, 18f, 90f));
            }
        }
    }

    private void SetElementSelection(ElementSelectable sel)
    {
        if (selectedElement != null)
        {
            selectedElement.OnDeselected();
        }
        selectedElement = sel;
        Selected = sel;

        if (sel == null)
        {
            var pmPanel = FindObjectOfType<PMPanel>();
            if (pmPanel != null)
            {
                pmPanel.Hide();
            }
        }
    }

    private void SetInfoSelection(InfoSelectable info)
    {
        selectedInfo = info;
        if (info != null)
        {
            scroll = Vector2.zero;
        }
    }

    void OnGUI()
    {
        if (Selected == null && selectedInfo == null) return;

        EnsureStyles();

        string info = Selected != null
            ? Selected.GetValuesAt(lastHitPoint)
            : $"==={selectedInfo.name}===\n{selectedInfo.GetInfo()}";
        float pmZone = Mathf.Min(440f, Screen.width * 0.42f) + 24f;
        float maxW = Screen.width - panelOffset.x * 2f - pmZone;
        float panelW = Mathf.Max(panelMinSize.x, Mathf.Min(Screen.width * 0.36f, maxW));
        float maxPanelH = Mathf.Max(280f, Screen.height - panelOffset.y * 2f - 82f);
        float panelH = Mathf.Min(Mathf.Max(480f, Screen.height * 0.86f), maxPanelH);

        float px = Screen.width - panelOffset.x - panelW;
        float py = Screen.height - panelOffset.y - panelH;

        GUI.Box(new Rect(px, py, panelW, panelH), GUIContent.none, boxStyle);

        float innerW = panelW - panelPaddingX * 2f;
        float contentH = CalculateContentHeight(info, innerW);
        if (contentH < panelH - 60f) contentH = panelH - 60f;

        var rect = new Rect(panelPaddingX, 0f, innerW, contentH);
        scroll = GUI.BeginScrollView(new Rect(px, py + 8f, panelW, panelH - 16f), scroll,
            new Rect(0f, 0f, panelW - 20f, contentH + 80f));

        int prevSize = labelStyle.fontSize;
        labelStyle.fontSize = panelFontSize;

        DrawLabel(info, rect, innerW, ref contentH);

        labelStyle.fontSize = prevSize;
        GUI.EndScrollView();
    }

    private void DrawLabel(string text, Rect area, float width, ref float yOffset)
    {
        string[] sections = text.Split('\n');
        float y = area.y;
        float lineH = panelFontSize + panelLineSpacing;

        foreach (string raw in sections)
        {
            string line = raw.TrimEnd('\r');
            bool isTitle = line.StartsWith("===");
            bool isHeader = line.StartsWith("---") && line.EndsWith("---");

            GUIStyle style = isTitle ? titleStyle : isHeader ? headerStyle : labelStyle;
            float styleLineH = isTitle || isHeader ? lineH + 2f : lineH;

            var content = new GUIContent(line);
            float h = style.CalcHeight(content, width);
            if (h < styleLineH) h = styleLineH;

            GUI.Label(new Rect(area.x, y, width, h), content, style);

            if (line == "" || line.Contains("---"))
            {
                y += h + panelSectionSpacing;
            }
            else
            {
                y += h + panelLineSpacing;
            }
        }

        yOffset = y - area.y;
    }

    private float CalculateContentHeight(string text, float width)
    {
        EnsureStyles();
        string[] sections = text.Split('\n');
        float y = 0f;
        float lineH = panelFontSize + panelLineSpacing;

        int prevLabelSize = labelStyle.fontSize;
        int prevTitleSize = titleStyle.fontSize;
        labelStyle.fontSize = panelFontSize;
        titleStyle.fontSize = panelFontSize + 2;
        headerStyle.fontSize = panelFontSize;

        foreach (string raw in sections)
        {
            string line = raw.TrimEnd('\r');
            bool isTitle = line.StartsWith("===");
            bool isHeader = line.StartsWith("---") && line.EndsWith("---");
            GUIStyle style = isTitle ? titleStyle : isHeader ? headerStyle : labelStyle;
            float styleLineH = isTitle || isHeader ? lineH + 2f : lineH;
            float h = style.CalcHeight(new GUIContent(line), width);
            if (h < styleLineH) h = styleLineH;
            y += h + ((line == "" || line.Contains("---")) ? panelSectionSpacing : panelLineSpacing);
        }

        labelStyle.fontSize = prevLabelSize;
        titleStyle.fontSize = prevTitleSize;
        headerStyle.fontSize = prevLabelSize;
        return y + 60f;
    }

    private void EnsureStyles()
    {
        if (boxStyle != null) return;

        boxStyle = new GUIStyle(GUI.skin.box);
        Texture2D bg = new Texture2D(1, 1);
        bg.SetPixel(0, 0, new Color(0.07f, 0.07f, 0.15f, 0.92f));
        bg.Apply();
        boxStyle.normal.background = bg;
        boxStyle.border = new RectOffset(2, 2, 2, 2);
        boxStyle.padding = new RectOffset(8, 8, 8, 8);

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = panelFontSize;
        labelStyle.normal.textColor = Color.white;
        labelStyle.wordWrap = true;
        labelStyle.richText = false;
        labelStyle.alignment = TextAnchor.UpperLeft;
        labelStyle.padding = new RectOffset(0, 0, 0, 0);

        titleStyle = new GUIStyle(labelStyle);
        titleStyle.fontSize = panelFontSize + 2;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.normal.textColor = new Color(0.3f, 0.8f, 1f);

        headerStyle = new GUIStyle(labelStyle);
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.normal.textColor = new Color(0.7f, 0.85f, 0.95f);
    }
}
