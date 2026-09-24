using UnityEngine;

// A separate window: existing information, P-M and mobile-load panels keep their layout.
public class SelectedBeamDiagramPanel : MonoBehaviour
{
    private static int nextWindowId = 41050;
    private readonly int windowId = nextWindowId++;
    private readonly float[,] samples = new float[5, 61];
    private readonly string[] names = { "My", "Mz", "Vy", "Vz", "Axial N" };
    private readonly Color[] colors = {
        new Color(1f, 0.45f, 0.85f), new Color(0.7f, 0.6f, 1f),
        new Color(1f, 0.7f, 0.25f), new Color(0.3f, 0.85f, 1f),
        new Color(0.5f, 1f, 0.6f)
    };
    private ElementPicker picker;
    private DiagramController diagrams;
    private ElementSelectable selected;
    private int selectedDiagram;
    private bool expanded = true;
    private bool positioned;
    private Rect panel;
    private Vector2 scroll;
    private GUIStyle textStyle;
    private GUIStyle titleStyle;
    private GUIStyle windowStyle;
    private Texture2D background;

    private ElementSelectable SelectedBeam()
    {
        if (picker == null) picker = FindObjectOfType<ElementPicker>();
        ElementSelectable beam = picker != null ? picker.Selected : null;
        return beam != null && beam.gameObject.activeInHierarchy && beam.data != null &&
            beam.data.type == "viga" ? beam : null;
    }

    public bool ContainsMouse(Vector2 mouse)
    {
        return isActiveAndEnabled && SelectedBeam() != null && GetPanelRect().Contains(mouse);
    }

    public static bool BlocksPointer()
    {
        SelectedBeamDiagramPanel panel = FindObjectOfType<SelectedBeamDiagramPanel>();
        return panel != null && panel.ContainsMouse(
            new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
    }

    private Rect GetPanelRect()
    {
        float width = Mathf.Min(420f, Screen.width - 16f);
        float height = expanded ? Mathf.Min(320f, Screen.height - 160f) : 62f;
        height = Mathf.Max(62f, height);
        if (!positioned)
        {
            panel = new Rect((Screen.width - width) * 0.5f, 144f, width, height);
            positioned = true;
        }
        panel.width = width;
        panel.height = height;
        panel.x = Mathf.Clamp(panel.x, 0f, Mathf.Max(0f, Screen.width - width));
        panel.y = Mathf.Clamp(panel.y, 0f, Mathf.Max(0f, Screen.height - height));
        return panel;
    }

    private void OnGUI()
    {
        if (!Application.isPlaying) return;
        ElementSelectable beam = SelectedBeam();
        if (beam == null) return;
        if (selected != beam)
        {
            selected = beam;
            scroll = Vector2.zero;
        }
        if (diagrams == null) diagrams = GetComponent<DiagramController>();
        EnsureStyles();
        int previousDepth = GUI.depth;
        GUI.depth = -20;
        string tag = string.IsNullOrEmpty(beam.data.elementTag) ? beam.data.id.ToString() : beam.data.elementTag;
        panel = GUI.Window(windowId, GetPanelRect(), DrawWindow,
            "Diagramas de viga - " + tag, windowStyle);
        GUI.depth = previousDepth;
    }

    private void DrawWindow(int id)
    {
        if (GUI.Button(new Rect(12f, 28f, panel.width - 24f, 26f),
            expanded ? "Ocultar diagramas" : "Mostrar diagramas"))
            expanded = !expanded;

        if (expanded && panel.height > 110f)
        {
            selectedDiagram = GUI.Toolbar(new Rect(12f, 62f, panel.width - 24f, 26f),
                selectedDiagram, names);
            float width = panel.width - 42f;
            const float chartHeight = 106f;
            scroll = GUI.BeginScrollView(new Rect(10f, 96f, panel.width - 20f, panel.height - 106f),
                scroll, new Rect(0f, 0f, width, 80f + chartHeight));
            GUI.Label(new Rect(0f, 0f, width, 36f), UnityData.GetActiveLoadLabel(), textStyle);
            GUI.Label(new Rect(0f, 36f, width, 38f),
                "Eje I → J | N: tracción+ | escala propia\nCombinación OpenSees; carga móvil aparte", textStyle);
            if (diagrams != null && diagrams.TryGetSelectedDiagramSamples(selected, samples))
            {
                DrawChart(new Rect(0f, 80f, width, chartHeight - 6f), selectedDiagram);
            }
            else
                GUI.Label(new Rect(0f, 80f, width, 52f),
                    "No hay fuerzas disponibles para esta viga y combinación.", textStyle);
            GUI.EndScrollView();
        }
        GUI.DragWindow(new Rect(0f, 0f, panel.width, 25f));
    }

    private void DrawChart(Rect rect, int row)
    {
        string unit = row < 2 ? "kN·m" : "kN";
        int count = samples.GetLength(1);
        float min = samples[row, 0];
        float max = min;
        for (int i = 1; i < count; i++)
        {
            min = Mathf.Min(min, samples[row, i]);
            max = Mathf.Max(max, samples[row, i]);
        }
        GUI.Label(new Rect(rect.x, rect.y, rect.width, 19f),
            $"{names[row]} [{unit}]   mín {min:0.##} / máx {max:0.##}", titleStyle);
        Rect plot = new Rect(rect.x + 12f, rect.y + 23f, rect.width - 24f, 48f);
        float bound = Mathf.Max(Mathf.Abs(min), Mathf.Abs(max), 0.000001f);
        float zero = plot.center.y;
        DrawLine(new Vector2(plot.x, zero), new Vector2(plot.xMax, zero), Color.gray, 1f);
        Vector2 previous = Vector2.zero;
        for (int i = 0; i < count; i++)
        {
            Vector2 point = new Vector2(plot.x + plot.width * i / (count - 1),
                zero - samples[row, i] / bound * (plot.height * 0.45f));
            if (i > 0) DrawLine(previous, point, colors[row], 2f);
            if (i % 5 == 0)
                DrawLine(new Vector2(point.x, zero), point, new Color(colors[row].r,
                    colors[row].g, colors[row].b, 0.25f), 1f);
            previous = point;
        }
        GUI.Label(new Rect(rect.x, rect.y + 76f, rect.width, 22f),
            $"I: {samples[row, 0]:0.##}   Centro: {samples[row, count / 2]:0.##}   J: {samples[row, count - 1]:0.##}", textStyle);
    }

    private static void DrawLine(Vector2 a, Vector2 b, Color color, float thickness)
    {
        if (Event.current.type != EventType.Repaint) return;
        Vector2 delta = b - a;
        if (delta.sqrMagnitude < 0.000001f) return;
        Matrix4x4 matrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.color = color;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, a);
        GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, delta.magnitude, thickness), Texture2D.whiteTexture);
        GUI.matrix = matrix;
        GUI.color = previousColor;
    }

    private void EnsureStyles()
    {
        if (textStyle != null) return;
        textStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        textStyle.normal.textColor = Color.white;
        titleStyle = new GUIStyle(textStyle) { fontStyle = FontStyle.Bold };
        background = new Texture2D(1, 1);
        background.SetPixel(0, 0, new Color(0.06f, 0.07f, 0.11f, 1f));
        background.Apply();
        windowStyle = new GUIStyle(GUI.skin.window);
        windowStyle.normal.background = background;
        windowStyle.onNormal.background = background;
        windowStyle.normal.textColor = Color.white;
        windowStyle.onNormal.textColor = Color.white;
    }

    private void OnDestroy()
    {
        if (background != null) Destroy(background);
    }
}
