using UnityEngine;

public class SelectedDiagramPanel : MonoBehaviour
{
    private readonly string[] componentOptions = new string[] { "Axial", "Vy", "Vz", "My", "Mz" };
    private int componentIndex = 0;
    private bool visible = false;
    private GUIStyle boxStyle;
    private GUIStyle labelStyle;
    private GUIStyle titleStyle;
    private Texture2D whiteTex;
    private Texture2D bgTex;

    public static Rect ButtonRect()
    {
        return new Rect(360f, 330f, 172f, 24f);
    }

    public static Rect PanelRect()
    {
        return new Rect(360f, 360f, 410f, 310f);
    }

    private void OnGUI()
    {
        EnsureStyles();
        int oldDepth = GUI.depth;
        GUI.depth = -80;

        Rect button = ButtonRect();
        if (GUI.Button(button, visible ? "Ocultar diagrama" : "Diagrama seleccionado"))
        {
            visible = !visible;
        }

        if (!visible)
        {
            GUI.depth = oldDepth;
            return;
        }

        ElementPicker picker = FindObjectOfType<ElementPicker>();
        ElementSelectable selected = picker != null ? picker.Selected : null;

        Rect panel = PanelRect();
        GUI.Box(panel, GUIContent.none, boxStyle);

        float x = panel.x + 14f;
        float y = panel.y + 12f;
        float w = panel.width - 28f;

        GUI.Label(new Rect(x, y, w, 22f), "Diagrama del elemento seleccionado", titleStyle);
        y += 28f;

        componentIndex = GUI.Toolbar(new Rect(x, y, w, 24f), componentIndex, componentOptions);
        y += 32f;

        if (selected == null)
        {
            GUI.Label(new Rect(x, y, w, 44f), "Selecciona una viga, columna o muro para ver su diagrama.", labelStyle);
            GUI.depth = oldDepth;
            return;
        }

        string name = selected.data != null
            ? (!string.IsNullOrEmpty(selected.data.elementTag) ? selected.data.elementTag : selected.data.id.ToString())
            : "Muro " + selected.wallId;
        GUI.Label(new Rect(x, y, w, 20f), $"Elemento: {name} | {UnityData.GetActiveLoadLabel()}", labelStyle);
        y += 24f;

        Rect graph = new Rect(x, y, w, 205f);
        DrawGraph(graph, selected, componentOptions[componentIndex]);
        GUI.depth = oldDepth;
    }

    private void DrawGraph(Rect r, ElementSelectable selected, string component)
    {
        GUI.DrawTexture(r, bgTex);
        DrawGrid(r);

        int count = 21;
        float[] values = new float[count];
        float maxAbs = 0.001f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            values[i] = GetValue(selected, component, t);
            maxAbs = Mathf.Max(maxAbs, Mathf.Abs(values[i]));
        }

        Vector2 prev = Vector2.zero;
        for (int i = 0; i < count; i++)
        {
            float tx = i / (float)(count - 1);
            float ty = 0.5f - values[i] / (maxAbs * 2.2f);
            Vector2 p = new Vector2(r.x + tx * r.width, r.y + Mathf.Clamp01(ty) * r.height);

            if (i > 0)
            {
                DrawLine(prev, p, Color.cyan, 2f);
            }

            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(p.x - 2f, p.y - 2f, 4f, 4f), whiteTex);

            if (i == 0 || i == count / 2 || i == count - 1)
            {
                GUI.Label(new Rect(p.x - 38f, p.y - 18f, 76f, 18f), values[i].ToString("0.##"), labelStyle);
            }
            prev = p;
        }

        GUI.color = Color.white;
        string unit = component.StartsWith("M") ? "kN*m" : "kN";
        GUI.Label(new Rect(r.x + 8f, r.y + r.height - 22f, r.width - 16f, 18f), $"{component} [{unit}] | max abs = {maxAbs:0.##}", labelStyle);
    }

    private float GetValue(ElementSelectable selected, string component, float t)
    {
        if (selected == null)
        {
            return 0f;
        }

        if (selected.data == null)
        {
            DemandRecord demand = selected.GetActiveWallDemand();
            if (demand == null) return 0f;
            if (component == "Axial") return demand.P_kN;
            if (component == "My" || component == "Mz") return demand.M_kN_m;
            return 0f;
        }

        float[] f = UnityData.GetElementForces(UnityData.ActiveCombo, selected.data.id);
        if (f == null || f.Length < 12)
        {
            return 0f;
        }

        if (component == "Axial") return Mathf.Lerp(-f[0], f[6], t);
        if (component == "Vy") return Mathf.Lerp(f[1], -f[7], t);
        if (component == "Vz") return Mathf.Lerp(f[2], -f[8], t);
        if (component == "My") return Mathf.Lerp(f[4], -f[10], t);
        if (component == "Mz") return Mathf.Lerp(f[5], -f[11], t);
        return 0f;
    }

    private void DrawGrid(Rect r)
    {
        Color grid = new Color(0.35f, 0.38f, 0.42f, 0.65f);
        for (int i = 0; i <= 4; i++)
        {
            float x = r.x + r.width * i / 4f;
            DrawLine(new Vector2(x, r.y), new Vector2(x, r.y + r.height), grid, 1f);
            float y = r.y + r.height * i / 4f;
            DrawLine(new Vector2(r.x, y), new Vector2(r.x + r.width, y), grid, 1f);
        }
        DrawLine(new Vector2(r.x, r.y + r.height * 0.5f), new Vector2(r.x + r.width, r.y + r.height * 0.5f), Color.white, 1.5f);
    }

    private void DrawLine(Vector2 a, Vector2 b, Color color, float width)
    {
        Matrix4x4 matrix = GUI.matrix;
        GUI.color = color;
        float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
        float length = (b - a).magnitude;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, length, width), whiteTex);
        GUI.matrix = matrix;
        GUI.color = Color.white;
    }

    private void EnsureStyles()
    {
        if (boxStyle != null) return;
        whiteTex = new Texture2D(1, 1);
        whiteTex.SetPixel(0, 0, Color.white);
        whiteTex.Apply();
        bgTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, new Color(0.04f, 0.05f, 0.07f, 0.93f));
        bgTex.Apply();

        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = bgTex;
        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = Color.white;
        labelStyle.fontSize = 12;
        titleStyle = new GUIStyle(labelStyle);
        titleStyle.fontSize = 14;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.normal.textColor = new Color(0.75f, 0.95f, 1f);
    }
}
