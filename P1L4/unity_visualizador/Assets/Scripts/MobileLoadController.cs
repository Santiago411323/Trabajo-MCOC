using UnityEngine;

public class MobileLoadController : MonoBehaviour
{
    public float loadKN = 100f;
    public float position01 = 0.5f;
    public bool visible = true;
    public float diagramScale = 0.018f;
    public bool showAxial = false;
    public bool showShear = false;
    public bool showMoment = false;

    private ElementSelectable selectedBeam;
    private GameObject marker;
    private GameObject arrow;
    private GameObject axialLine;
    private GameObject shearLine;
    private GameObject momentLine;
    private GUIStyle boxStyle;
    private GUIStyle labelStyle;
    private GUIStyle valueStyle;
    private Texture2D panelBg;
    private Texture2D valueBg;

    public static Rect PanelRect()
    {
        return new Rect(360f, 92f, 330f, 204f);
    }

    public void SetSelectedElement(ElementSelectable element)
    {
        if (element != null && element.data != null && element.data.type == "viga")
        {
            selectedBeam = element;
        }
    }

    private void Update()
    {
        SyncSelectedBeam();

        if (!visible || selectedBeam == null)
        {
            ClearVisuals();
            return;
        }

        UpdateVisuals();
    }

    private void OnGUI()
    {
        EnsureStyles();

        Rect panel = PanelRect();
        float x = panel.x;
        float y = panel.y;
        float w = panel.width;
        float h = panel.height;
        GUI.Box(new Rect(x, y, w, h), "Sidequest | Carga movil", boxStyle);

        float iy = y + 26f;
        visible = GUI.Toggle(new Rect(x + 12f, iy, 180f, 20f), visible, "Activar carga movil");
        iy += 24f;

        GUI.Label(new Rect(x + 12f, iy, 90f, 20f), "Carga P [kN]", labelStyle);
        if (GUI.Button(new Rect(x + 102f, iy, 26f, 20f), "-")) loadKN = Mathf.Max(0f, loadKN - 10f);
        loadKN = GUI.HorizontalSlider(new Rect(x + 132f, iy + 5f, 125f, 18f), loadKN, 0f, 300f);
        GUI.Box(new Rect(x + 260f, iy, 44f, 20f), GUIContent.none, valueStyle);
        GUI.Label(new Rect(x + 263f, iy + 1f, 40f, 18f), loadKN.ToString("0"), valueStyle);
        if (GUI.Button(new Rect(x + 304f, iy, 22f, 20f), "+")) loadKN = Mathf.Min(300f, loadKN + 10f);
        iy += 24f;

        GUI.Label(new Rect(x + 12f, iy, 90f, 20f), "Posicion x/L", labelStyle);
        if (GUI.Button(new Rect(x + 102f, iy, 26f, 20f), "-")) position01 = Mathf.Max(0f, position01 - 0.05f);
        position01 = GUI.HorizontalSlider(new Rect(x + 132f, iy + 5f, 125f, 18f), position01, 0f, 1f);
        GUI.Box(new Rect(x + 260f, iy, 44f, 20f), GUIContent.none, valueStyle);
        GUI.Label(new Rect(x + 263f, iy + 1f, 40f, 18f), (position01 * 100f).ToString("0") + "%", valueStyle);
        if (GUI.Button(new Rect(x + 304f, iy, 22f, 20f), "+")) position01 = Mathf.Min(1f, position01 + 0.05f);
        iy += 26f;

        GUI.Label(new Rect(x + 12f, iy, 70f, 20f), "Diagramas", labelStyle);
        showAxial = GUI.Toggle(new Rect(x + 82f, iy, 62f, 20f), showAxial, "Axial");
        showShear = GUI.Toggle(new Rect(x + 146f, iy, 62f, 20f), showShear, "Corte");
        showMoment = GUI.Toggle(new Rect(x + 210f, iy, 82f, 20f), showMoment, "Momento");
        iy += 24f;

        if (selectedBeam == null)
        {
            GUI.Label(new Rect(x + 12f, iy, w - 24f, 44f), "Selecciona una viga para mover la carga.", labelStyle);
            return;
        }

        float ri = loadKN * (1f - position01);
        float rj = loadKN * position01;
        float error = Mathf.Abs(loadKN - ri - rj);
        string beamTag = !string.IsNullOrEmpty(selectedBeam.data.elementTag)
            ? selectedBeam.data.elementTag
            : selectedBeam.data.id.ToString();

        GUI.Label(new Rect(x + 12f, iy, w - 24f, 18f), "Viga: " + beamTag, labelStyle);
        iy += 18f;
        GUI.Label(new Rect(x + 12f, iy, w - 24f, 18f), $"Reparto: I={ri:0.00} kN | J={rj:0.00} kN", labelStyle);
        iy += 18f;
        GUI.Label(new Rect(x + 12f, iy, w - 24f, 18f), $"Conservacion: I+J={ri + rj:0.00} kN | error={error:0.000}", labelStyle);
        iy += 18f;
        GUI.Label(new Rect(x + 12f, iy, w - 24f, 18f), "Respuesta visual local de la viga seleccionada.", labelStyle);
    }

    private void SyncSelectedBeam()
    {
        ElementPicker picker = FindObjectOfType<ElementPicker>();
        if (picker == null || picker.Selected == null || picker.Selected.data == null)
        {
            return;
        }

        if (picker.Selected.data.type == "viga")
        {
            selectedBeam = picker.Selected;
        }
    }

    private void UpdateVisuals()
    {
        EnsureObjects();

        Vector3 a = selectedBeam.startPoint;
        Vector3 b = selectedBeam.endPoint;
        Vector3 axis = b - a;
        float length = Mathf.Max(axis.magnitude, 0.001f);
        Vector3 dir = axis / length;
        Vector3 pos = a + axis * position01;
        Vector3 up = Vector3.up;

        marker.transform.position = pos + up * 0.28f;
        marker.transform.localScale = Vector3.one * 0.28f;

        arrow.transform.position = pos + up * 1.0f;
        arrow.transform.rotation = Quaternion.FromToRotation(Vector3.up, Vector3.down);
        arrow.transform.localScale = new Vector3(0.08f, 0.45f, 0.08f);

        float ri = loadKN * (1f - position01);
        float rj = loadKN * position01;
        float mMax = ri * position01 * length;
        Vector3 lateral = Vector3.Cross(dir, Vector3.up).normalized;
        if (lateral.sqrMagnitude < 0.01f) lateral = Vector3.right;

        axialLine.SetActive(showAxial);
        shearLine.SetActive(showShear);
        momentLine.SetActive(showMoment);

        if (showAxial)
        {
            var axial = axialLine.GetComponent<LineRenderer>();
            axial.positionCount = 2;
            axial.SetPosition(0, a + lateral * 0.18f + up * 0.08f);
            axial.SetPosition(1, b + lateral * 0.18f + up * 0.08f);
        }

        if (showShear)
        {
            var shear = shearLine.GetComponent<LineRenderer>();
            shear.positionCount = 4;
            shear.SetPosition(0, a + lateral * 0.35f);
            shear.SetPosition(1, pos + lateral * 0.35f + up * (ri * diagramScale));
            shear.SetPosition(2, pos + lateral * 0.35f - up * (rj * diagramScale));
            shear.SetPosition(3, b + lateral * 0.35f);
        }

        if (showMoment)
        {
            var moment = momentLine.GetComponent<LineRenderer>();
            moment.positionCount = 3;
            moment.SetPosition(0, a + lateral * 0.70f);
            moment.SetPosition(1, pos + lateral * 0.70f + up * (mMax * diagramScale));
            moment.SetPosition(2, b + lateral * 0.70f);
        }
    }

    private void EnsureObjects()
    {
        if (marker == null)
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "CargaMovil_Punto";
            marker.GetComponent<Renderer>().material = CreateMaterial(Color.red);
            DestroyCollider(marker);
        }
        marker.SetActive(true);
        if (arrow == null)
        {
            arrow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arrow.name = "CargaMovil_Flecha";
            arrow.GetComponent<Renderer>().material = CreateMaterial(Color.red);
            DestroyCollider(arrow);
        }
        arrow.SetActive(true);
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
    }

    private GameObject CreateLineObject(string name, Color color)
    {
        GameObject go = new GameObject(name);
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

    private void ClearVisuals()
    {
        if (marker != null) marker.SetActive(false);
        if (arrow != null) arrow.SetActive(false);
        if (shearLine != null) shearLine.SetActive(false);
        if (momentLine != null) momentLine.SetActive(false);
        if (axialLine != null) axialLine.SetActive(false);
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
