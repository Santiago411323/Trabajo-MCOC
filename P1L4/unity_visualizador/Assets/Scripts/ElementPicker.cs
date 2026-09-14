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
    public int panelFontSize = 15;
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

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, selectableLayer))
            {
                var selectable = hit.collider.GetComponent<ElementSelectable>();
                if (selectable != null)
                {
                    Selected = selectable;
                    lastHitPoint = hit.point;
                    scroll = Vector2.zero;

                    if (selectedElement != null)
                    {
                        selectedElement.OnDeselected();
                    }
                    selectedElement = selectable;
                    selectable.OnSelected();

                    if (!string.IsNullOrEmpty(selectedElement.pmSectionId))
                    {
                        var pmPanel = FindObjectOfType<PMPanel>();
                        if (pmPanel != null)
                        {
                            pmPanel.ShowPMForElement(selectedElement);
                        }
                    }
                    return;
                }
            }
            if (selectedElement != null)
            {
                selectedElement.OnDeselected();
                selectedElement = null;
                Selected = null;

                var pmPanel = FindObjectOfType<PMPanel>();
                if (pmPanel != null)
                {
                    pmPanel.Hide();
                }
            }
        }
    }

    private ElementSelectable selectedElement;

    void OnGUI()
    {
        if (Selected == null) return;

        EnsureStyles();

        string info = Selected.GetValuesAt(lastHitPoint);
        float panelW = Mathf.Max(panelMinSize.x, Screen.width * panelMaxWidthRatio);
        float panelH = panelFontSize * 20 + panelSectionSpacing * 4 + 80f;

        float px = Screen.width - panelOffset.x - panelW;
        float py = Screen.height - panelOffset.y - panelH;

        GUI.Box(new Rect(px, py, panelW, panelH), GUIContent.none, boxStyle);

        float innerW = panelW - panelPaddingX * 2f;
        var content = new GUIContent(info);
        float contentH = labelStyle.CalcHeight(content, innerW);
        if (contentH < panelH - 60f) contentH = panelH - 60f;

        var rect = new Rect(px + panelPaddingX, py + 8f, innerW, contentH);
        scroll = GUI.BeginScrollView(new Rect(px, py + 8f, panelW, panelH - 16f), scroll,
            new Rect(px, py, panelW, contentH + 16f));

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
