using UnityEngine;
using System.Linq;

public class PMPanel : MonoBehaviour
{
    public Vector2 panelOffset = new Vector2(24f, 20f);
    public Vector2 panelSize = new Vector2(440f, 400f);
    public int fontSize = 14;
    public float padding = 14f;
    public float diagramMargin = 50f;

    private ElementSelectable currentElement;
    private PMCurveData currentCurve;
    private bool visible;
    private ElementSelectable[] allElements;

    private GUIStyle boxStyle;
    private GUIStyle labelStyle;
    private GUIStyle titleStyle;
    private GUIStyle demandStyle;
    private Texture2D whiteTex;
    private Texture2D bgTex;

    private Color curveColor = new Color(0.2f, 0.75f, 0.35f);
    private Color demandColor = new Color(1f, 0.25f, 0.25f);
    private Color gridColor = new Color(0.3f, 0.3f, 0.35f);
    private Color axisColor = new Color(0.6f, 0.6f, 0.65f);
    private Color bgColor = new Color(0.05f, 0.05f, 0.12f, 0.92f);

    public void ShowPMForElement(ElementSelectable element)
    {
        if (element == null || string.IsNullOrEmpty(element.pmSectionId))
        {
            Hide();
            return;
        }

        string sectionId = element.pmSectionId;
        currentCurve = UnityData.GetPMCurve(sectionId);
        currentElement = element;
        allElements = FindObjectsOfType<ElementSelectable>();
        visible = (currentCurve != null && currentCurve.points != null && currentCurve.points.Length > 0);
    }

    public void Hide()
    {
        visible = false;
        currentElement = null;
        currentCurve = null;
    }

    void OnGUI()
    {
        if (!visible || currentCurve == null || currentElement == null) return;
        if (currentCurve.points.Length < 2) return;

        EnsureStyles();

        float pw = Mathf.Min(panelSize.x, Screen.width * 0.42f);
        float ph = Mathf.Min(panelSize.y, Mathf.Max(300f, Screen.height * 0.48f));
        float px = panelOffset.x;
        float py = Screen.height - panelOffset.y - ph;

        GUI.Box(new Rect(px - 2, py - 2, pw + 4, ph + 4), GUIContent.none, boxStyle);

        float innerW = pw - padding * 2f;
        float y = py + padding;
        float lineH = fontSize + 3f;
        float diagSize = Mathf.Min(innerW, ph - 120f);

        string header = $"DIAGRAMA P-M - {currentCurve.sectionId}";
        GUI.Label(new Rect(px + padding, y, innerW, lineH + 4), header, titleStyle);
        y += lineH + 10f;

        string matInfo = $"fc' = {currentCurve.fc_MPa:0} MPa | fy = {currentCurve.fy_MPa:0} MPa";
        if (currentCurve.steelBars > 0)
        {
            matInfo += $" | {currentCurve.steelBars} bars phi {currentCurve.barDiameter_mm:0}mm";
        }
        GUI.Label(new Rect(px + padding, y, innerW, lineH), matInfo, labelStyle);
        y += lineH + 4f;

        string secInfo = $"b = {currentCurve.b_m:0.000} m | h = {currentCurve.h_m:0.000} m | Ast = {currentCurve.Ast_mm2:0} mm2 | rho = {currentCurve.rho_percent:0.###}%";
        GUI.Label(new Rect(px + padding, y, innerW, lineH), secInfo, labelStyle);
        y += lineH + 6f;

        string interpInfo = $"Po = {currentCurve.Po_kN:0.0} kN | {currentCurve.interpretation}";
        GUI.Label(new Rect(px + padding, y, innerW, lineH), interpInfo, labelStyle);
        y += lineH + 10f;

        DrawDiagram(px + padding, y, diagSize, diagSize);
        y += diagSize + 10f;

        string loadLabel = "Demanda mostrada desde: " + UnityData.GetComboLabel(UnityData.ActiveCombo);
        GUI.Label(new Rect(px + padding, y, innerW, lineH), loadLabel, labelStyle);
        y += lineH;

        bool isWallCurve = currentCurve.elementType == "muro" || currentElement.data == null;
        int sameSectionElements = isWallCurve ? CountWallsWithSection(currentCurve.sectionId) : CountColumnsWithSection(currentCurve.sectionId);
        string colLabel;
        if (isWallCurve)
        {
            colLabel = sameSectionElements + (sameSectionElements == 1 ? " muro con esta curva. Puntos rojos = demanda por combo." :
                                                                         " muros con esta curva. Puntos rojos = demanda por combo.");
        }
        else if (sameSectionElements > 0)
        {
            colLabel = sameSectionElements + (sameSectionElements == 1 ? " columna de esta seccion. Puntos naranjas = demanda por columna." :
                                                                       " columnas de esta seccion. Puntos naranjas = demanda por columna.");
        }
        else
        {
            colLabel = "Sin columnas de esta seccion en el modelo.";
        }
        GUI.Label(new Rect(px + padding, y, innerW, lineH), colLabel, labelStyle);
        y += lineH;

        DemandRecord[] visibleDemands = GetVisibleDemands();
        if (visibleDemands != null && visibleDemands.Length > 0)
        {
            foreach (var d in visibleDemands)
            {
                if (d == null) continue;
                float phi = Mathf.Atan2(d.M_kN_m, Mathf.Max(d.P_kN, 0.01f)) * Mathf.Rad2Deg;
                string dInfo = $"  {UnityData.GetComboLabel(d.combo)} -> P={d.P_kN:0.0} kN | M={d.M_kN_m:0.0} kN*m | phi={phi:0.0} deg";
                GUI.Label(new Rect(px + padding, y, innerW, lineH), dInfo, demandStyle);
                y += lineH;
            }
        }
    }

    private void DrawDiagram(float x, float y, float w, float h)
    {
        GUI.DrawTexture(new Rect(x, y, w, h), bgTex);

        float[] pVals = new float[currentCurve.points.Length];
        float[] mVals = new float[currentCurve.points.Length];
        for (int i = 0; i < currentCurve.points.Length; i++)
        {
            pVals[i] = currentCurve.points[i].P_kN;
            mVals[i] = currentCurve.points[i].M_kN_m;
        }

        float pMax = Mathf.Max(pVals.Max() * 1.15f, 100f);
        float pMin = Mathf.Min(pVals.Min() * 1.1f, -pMax * 0.3f);
        float mMax = Mathf.Max(mVals.Max() * 1.15f, 100f);
        float mMin = 0f;

        float rangeP = pMax - pMin;
        float rangeM = mMax - mMin;

        DrawGridAndAxes(x, y, w, h, pMin, pMax, mMin, mMax, rangeP, rangeM);

        var screenPoints = new Vector2[currentCurve.points.Length + 1];
        for (int i = 0; i < currentCurve.points.Length; i++)
        {
            float sx = x + ((mVals[i] - mMin) / rangeM) * w;
            float sy = y + h - ((pVals[i] - pMin) / rangeP) * h;
            screenPoints[i] = new Vector2(sx, sy);
        }

        screenPoints[currentCurve.points.Length] = screenPoints[0];

        GUI.color = curveColor;
        for (int i = 0; i < screenPoints.Length - 1; i++)
        {
            DrawLine(screenPoints[i], screenPoints[i + 1], 2f);
        }

        if (currentElement.data != null)
        {
            Vector2 demandPt = currentElement.GetDemandPoint();
            float dP = demandPt.x;
            float dM = demandPt.y;
            if (dP > pMin && dP < pMax && dM > mMin && dM < mMax)
            {
                float dx = x + ((dM - mMin) / rangeM) * w;
                float dy = y + h - ((dP - pMin) / rangeP) * h;

                GUI.color = demandColor;
                float r = 6f;
                GUI.DrawTexture(new Rect(dx - r, dy - r, r * 2, r * 2), whiteTex);
                GUI.color = Color.white;
                GUI.Label(new Rect(dx + 8f, dy - 10f, 120f, 20f), string.IsNullOrEmpty(UnityData.ActiveCombo) ? "demanda" : UnityData.ActiveCombo, labelStyle);
            }
        }
        else
        {
            DrawCurveDemandPoints(x, y, w, h, pMin, pMax, mMin, mMax, rangeP, rangeM);
        }

        DrawOtherColumnPoints(x, y, w, h, pMin, pMax, mMin, mMax, rangeP, rangeM);

        GUI.color = Color.white;
    }

    private void DrawCurveDemandPoints(float x, float y, float w, float h,
        float pMin, float pMax, float mMin, float mMax,
        float rangeP, float rangeM)
    {
        DemandRecord[] demands = GetVisibleDemands();
        if (demands == null || demands.Length == 0) return;

        foreach (DemandRecord demand in demands)
        {
            if (demand == null) continue;
            if (!string.IsNullOrEmpty(UnityData.ActiveCombo) && demand.combo != UnityData.ActiveCombo) continue;
            float p = demand.P_kN;
            float m = demand.M_kN_m;
            if (p <= pMin || p >= pMax || m <= mMin || m >= mMax) continue;

            float dx = x + ((m - mMin) / rangeM) * w;
            float dy = y + h - ((p - pMin) / rangeP) * h;

            GUI.color = demandColor;
            float r = 5.5f;
            GUI.DrawTexture(new Rect(dx - r, dy - r, r * 2f, r * 2f), whiteTex);
            GUI.color = Color.white;
            GUI.Label(new Rect(dx + 8f, dy - 10f, 120f, 20f), demand.combo, labelStyle);
        }

        GUI.color = Color.white;
    }

    private DemandRecord[] GetVisibleDemands()
    {
        if (currentElement != null && currentElement.data == null && currentElement.pmDemands != null && currentElement.pmDemands.Length > 0)
        {
            return currentElement.pmDemands;
        }
        return currentCurve.demands;
    }

    private void DrawOtherColumnPoints(float x, float y, float w, float h,
        float pMin, float pMax, float mMin, float mMax,
        float rangeP, float rangeM)
    {
        ElementSelectable[] all = allElements;
        if (all == null || all.Length == 0) return;

        foreach (ElementSelectable sel in all)
        {
            if (sel == null || sel == currentElement) continue;
            if (sel.data == null || string.IsNullOrEmpty(sel.pmSectionId)) continue;
            if (sel.pmSectionId != currentCurve.sectionId) continue;

            Vector2 pt = sel.GetDemandPoint();
            float p = pt.x;
            float m = pt.y;
            if (p <= pMin || p >= pMax || m <= mMin || m >= mMax) continue;

            float dx = x + ((m - mMin) / rangeM) * w;
            float dy = y + h - ((p - pMin) / rangeP) * h;

            GUI.color = new Color(1f, 0.85f, 0.2f);
            float r = 3.5f;
            GUI.DrawTexture(new Rect(dx - r, dy - r, r * 2, r * 2), whiteTex);
        }

        GUI.color = Color.white;
    }

    private int CountColumnsWithSection(string sectionId)
    {
        ElementSelectable[] all = allElements;
        if (all == null || string.IsNullOrEmpty(sectionId)) return 0;

        int count = 0;
        foreach (ElementSelectable sel in all)
        {
            if (sel != null && sel.data != null && sel.data.type == "columna" &&
                !string.IsNullOrEmpty(sel.pmSectionId) && sel.pmSectionId == sectionId)
            {
                count++;
            }
        }
        return count;
    }

    private int CountWallsWithSection(string sectionId)
    {
        ElementSelectable[] all = allElements;
        if (all == null || string.IsNullOrEmpty(sectionId)) return 0;

        int count = 0;
        foreach (ElementSelectable sel in all)
        {
            if (sel != null && sel.data == null && !string.IsNullOrEmpty(sel.pmSectionId) && sel.pmSectionId == sectionId)
            {
                count++;
            }
        }
        return count;
    }

    private void DrawGridAndAxes(float x, float y, float w, float h,
        float pMin, float pMax, float mMin, float mMax,
        float rangeP, float rangeM)
    {
        GUI.color = gridColor;
        int pLines = 6;
        int mLines = 5;
        for (int i = 0; i <= pLines; i++)
        {
            float frac = (float)i / pLines;
            float py = y + h * (1f - frac);
            GUI.DrawTexture(new Rect(x, py - 0.5f, w, 1f), whiteTex);

            float val = pMin + rangeP * frac;
            var content = new GUIContent($"{val:0}");
            var style = new GUIStyle(labelStyle);
            style.fontSize = 10;
            style.normal.textColor = gridColor;
            GUI.Label(new Rect(x - 40f, py - 6f, 38f, 14f), content, style);
        }
        for (int i = 0; i <= mLines; i++)
        {
            float frac = (float)i / mLines;
            float px = x + w * frac;
            GUI.DrawTexture(new Rect(px - 0.5f, y, 1f, h), whiteTex);

            float val = mMin + rangeM * frac;
            var content = new GUIContent($"{val:0}");
            var style = new GUIStyle(labelStyle);
            style.fontSize = 10;
            style.normal.textColor = gridColor;
            GUI.Label(new Rect(px - 12f, y + h + 2f, 30f, 14f), content, style);
        }

        GUI.color = axisColor;
        float pZero = y + h - ((0f - pMin) / rangeP) * h;
        if (pZero >= y && pZero <= y + h)
        {
            GUI.DrawTexture(new Rect(x, pZero - 0.5f, w, 1f), whiteTex);
        }
        GUI.DrawTexture(new Rect(x, y, 1f, h), whiteTex);

        GUI.color = Color.white;
    }

    private void DrawLine(Vector2 a, Vector2 b, float thickness)
    {
        Vector2 diff = b - a;
        float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;
        float length = diff.magnitude;

        Matrix4x4 saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.DrawTexture(new Rect(a.x, a.y - thickness / 2f, length, thickness), whiteTex);
        GUI.matrix = saved;
    }

    private void EnsureStyles()
    {
        if (boxStyle != null) return;

        whiteTex = new Texture2D(1, 1);
        whiteTex.SetPixel(0, 0, Color.white);
        whiteTex.Apply();

        bgTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, bgColor);
        bgTex.Apply();

        boxStyle = new GUIStyle(GUI.skin.box);
        Texture2D boxBg = new Texture2D(1, 1);
        boxBg.SetPixel(0, 0, new Color(0.07f, 0.07f, 0.15f, 0.95f));
        boxBg.Apply();
        boxStyle.normal.background = boxBg;
        boxStyle.border = new RectOffset(2, 2, 2, 2);
        boxStyle.padding = new RectOffset(8, 8, 8, 8);

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = fontSize;
        labelStyle.normal.textColor = Color.white;
        labelStyle.wordWrap = true;
        labelStyle.richText = false;

        titleStyle = new GUIStyle(labelStyle);
        titleStyle.fontSize = fontSize + 2;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.normal.textColor = new Color(0.3f, 0.85f, 1f);

        demandStyle = new GUIStyle(labelStyle);
        demandStyle.fontSize = fontSize;
        demandStyle.normal.textColor = new Color(1f, 0.4f, 0.4f);
    }
}
