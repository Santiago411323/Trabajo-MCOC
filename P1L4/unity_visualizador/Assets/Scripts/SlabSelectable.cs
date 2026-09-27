using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class SlabSelectable : InfoSelectable
{
    public SlabData slab;
    public SlabLoadMetadata metadata;
    public StructureData structure;

    private GameObject tributaryRoot;
    private Color originalColor;
    private bool hasOriginalColor;

    public override string GetInfo()
    {
        if (slab == null) return "Losa sin datos.";
        float dx = Mathf.Abs(slab.x1 - slab.x0);
        float dy = Mathf.Abs(slab.y1 - slab.y0);
        float area = metadata != null && metadata.area > 0f ? metadata.area : dx * dy;
        float thickness = metadata != null && metadata.thickness > 0f ? metadata.thickness : .15f;
        float density = metadata != null && metadata.density > 0f ? metadata.density : 2500f;
        float unitWeight = metadata != null && metadata.unitWeight > 0f ? metadata.unitWeight : density * 9.80665f / 1000f;
        float qG = metadata != null && metadata.qG > 0f ? metadata.qG : structure != null ? structure.q_G : 0f;
        float qQ = metadata != null && metadata.qQ > 0f ? metadata.qQ : structure != null ? structure.Q_kN_m2 : 0f;
        float selfWeight = metadata != null ? metadata.selfWeight : area * thickness * unitWeight;
        float additional = metadata != null ? metadata.permanentAdditional : area * Mathf.Max(0f, qG - thickness * unitWeight);
        float totalG = metadata != null ? metadata.totalG : area * qG;
        float totalQ = metadata != null ? metadata.totalQ : area * qQ;
        float mass = metadata != null ? metadata.mass : area * thickness * density;

        var text = new StringBuilder();
        text.AppendLine("--- LOSA SELECCIONADA ---");
        text.AppendLine($"Losa ID: {slab.id}");
        text.AppendLine($"Piso: {slab.nivel}");
        text.AppendLine($"Área geométrica: {area:0.###} m²");
        text.AppendLine($"Dimensiones: {dx:0.###} × {dy:0.###} m");
        text.AppendLine($"Espesor: {thickness:0.000} m");
        text.AppendLine();
        text.AppendLine("--- PESO DE LA LOSA · SIN MAYORAR ---");
        text.AppendLine($"Densidad: {density:0} kg/m³");
        text.AppendLine($"Masa propia: {mass:0.0} kg ({mass / 1000f:0.###} t)");
        text.AppendLine($"Peso propio: {selfWeight:0.###} kN");
        text.AppendLine($"Permanente adicional: {additional:0.###} kN");
        text.AppendLine($"G de la losa: {totalG:0.###} kN  ({qG:0.###} kN/m²)");
        text.AppendLine($"Sobrecarga Q: {totalQ:0.###} kN  ({qQ:0.###} kN/m²)");
        if (metadata != null)
        {
            string behavior = metadata.profile == "ONE_WAY" ? "1 dirección" : "2 direcciones";
            text.AppendLine($"Trabajo: {behavior} · relación b/a = {metadata.ratio:0.###}");
        }
        text.AppendLine();
        text.AppendLine("--- APORTE A CADA RECEPTOR · kN SIN MAYORAR ---");

        float sumArea = 0f, sumG = 0f, sumQ = 0f;
        if (metadata != null && metadata.receivers != null)
        {
            foreach (SlabReceiverMetadata receiver in metadata.receivers)
            {
                sumArea += receiver.area;
                sumG += receiver.G;
                sumQ += receiver.Q;
                text.AppendLine($"{BeamLabel(receiver.beam)} · área tributaria {receiver.area:0.###} m²");
                text.AppendLine($"PP: {receiver.selfWeight:0.###} · G: {receiver.G:0.###} · Q: {receiver.Q:0.###} kN");
            }
        }
        float error = Mathf.Abs(sumArea - area);
        bool pass = error <= Mathf.Max(.0001f, area * .0001f);
        text.AppendLine($"Σ receptores: área {sumArea:0.###} m² · G {sumG:0.###} kN · Q {sumQ:0.###} kN");
        text.AppendLine($"Conservación de área: {(pass ? "PASS" : "FAIL")} · error {error:0.0000} m²");
        text.AppendLine();
        text.AppendLine("PP está incluido en G. La losa es superficie tributaria y no tiene esfuerzos internos de placa.");
        return text.ToString();
    }

    public override void OnSelected()
    {
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            if (!hasOriginalColor) { originalColor = renderer.material.color; hasOriginalColor = true; }
            renderer.material.color = new Color(0.18f, 0.72f, 1f, 0.28f);
        }
        BuildTributaryVisualization();
    }

    public override void OnDeselected()
    {
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null && hasOriginalColor) renderer.material.color = originalColor;
        if (tributaryRoot != null) Destroy(tributaryRoot);
        tributaryRoot = null;
    }

    private string BeamLabel(int id)
    {
        if (structure != null && structure.elements != null)
            foreach (ElementData element in structure.elements)
                if (element.id == id)
                    return !string.IsNullOrEmpty(element.elementTag) ? element.elementTag : "Viga " + id;
        return "Viga " + id;
    }

    private void BuildTributaryVisualization()
    {
        if (tributaryRoot != null) Destroy(tributaryRoot);
        if (slab == null || metadata == null || metadata.edges == null) return;
        tributaryRoot = new GameObject("Áreas tributarias " + slab.id);
        tributaryRoot.transform.SetParent(transform.parent, false);
        tributaryRoot.hideFlags = HideFlags.DontSave;

        var activeSides = new List<string>();
        foreach (SlabLoadEdge edge in metadata.edges) if (edge.area > .00001f) activeSides.Add(edge.side);
        Color[] colors = {
            new Color(1f,.78f,.18f,.62f), new Color(.45f,1f,.42f,.62f),
            new Color(1f,.48f,.78f,.62f), new Color(.35f,.82f,1f,.62f)
        };
        var sideColors = new Dictionary<string, Color>();
        for (int i = 0; i < activeSides.Count; i++)
        {
            List<Vector2> polygon = TributaryPolygon(activeSides, activeSides[i]);
            if (polygon.Count < 3) continue;
            Color regionColor = colors[i % colors.Length];
            sideColors[activeSides[i]] = regionColor;
            var region = new GameObject("Región " + activeSides[i]);
            region.transform.SetParent(tributaryRoot.transform, false);
            var mesh = new Mesh();
            var vertices = new Vector3[polygon.Count];
            for (int p = 0; p < polygon.Count; p++) vertices[p] = new Vector3(polygon[p].x, slab.z + .085f, polygon[p].y);
            var triangles = new int[(polygon.Count - 2) * 3];
            for (int p = 0; p < polygon.Count - 2; p++) { triangles[p * 3] = 0; triangles[p * 3 + 1] = p + 1; triangles[p * 3 + 2] = p + 2; }
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals();
            region.AddComponent<MeshFilter>().mesh = mesh;
            region.AddComponent<MeshRenderer>().material = TransparentMaterial(regionColor);
        }
        // Las cuatro vigas de borde siempre quedan visibles. En losas
        // unidireccionales, los lados que no reciben carga se dibujan grises.
        foreach (SlabLoadEdge edge in metadata.edges)
            DrawBoundary(edge.side, sideColors.TryGetValue(edge.side, out Color color)
                ? color : new Color(.52f, .56f, .62f, .9f));
    }

    private List<Vector2> TributaryPolygon(List<string> activeSides, string target)
    {
        float x0 = Mathf.Min(slab.x0, slab.x1), x1 = Mathf.Max(slab.x0, slab.x1);
        float y0 = Mathf.Min(slab.y0, slab.y1), y1 = Mathf.Max(slab.y0, slab.y1);
        var polygon = new List<Vector2> { new Vector2(x0,y0), new Vector2(x1,y0), new Vector2(x1,y1), new Vector2(x0,y1) };
        foreach (string other in activeSides)
        {
            if (other == target) continue;
            polygon = Clip(polygon, p => Distance(p, other, x0, x1, y0, y1) - Distance(p, target, x0, x1, y0, y1));
        }
        return polygon;
    }

    private static float Distance(Vector2 p, string side, float x0, float x1, float y0, float y1)
    {
        if (side == "left") return p.x - x0;
        if (side == "right") return x1 - p.x;
        if (side == "bottom") return p.y - y0;
        return y1 - p.y;
    }

    private static List<Vector2> Clip(List<Vector2> input, System.Func<Vector2,float> signed)
    {
        var output = new List<Vector2>();
        if (input.Count == 0) return output;
        Vector2 a = input[input.Count - 1]; float fa = signed(a);
        foreach (Vector2 b in input)
        {
            float fb = signed(b); bool insideA = fa >= -1e-5f, insideB = fb >= -1e-5f;
            if (insideA != insideB)
            {
                float t = fa / (fa - fb);
                output.Add(Vector2.Lerp(a, b, t));
            }
            if (insideB) output.Add(b);
            a = b; fa = fb;
        }
        return output;
    }

    private void DrawBoundary(string side, Color color)
    {
        float x0 = Mathf.Min(slab.x0, slab.x1), x1 = Mathf.Max(slab.x0, slab.x1);
        float y0 = Mathf.Min(slab.y0, slab.y1), y1 = Mathf.Max(slab.y0, slab.y1);
        Vector3 a, b;
        if (side == "bottom") { a = new Vector3(x0, slab.z + .095f, y0); b = new Vector3(x1, slab.z + .095f, y0); }
        else if (side == "top") { a = new Vector3(x0, slab.z + .095f, y1); b = new Vector3(x1, slab.z + .095f, y1); }
        else if (side == "left") { a = new Vector3(x0, slab.z + .095f, y0); b = new Vector3(x0, slab.z + .095f, y1); }
        else { a = new Vector3(x1, slab.z + .095f, y0); b = new Vector3(x1, slab.z + .095f, y1); }
        var lineObject = new GameObject("Receptor " + side); lineObject.transform.SetParent(tributaryRoot.transform, false);
        var line = lineObject.AddComponent<LineRenderer>(); line.useWorldSpace = true; line.positionCount = 2;
        line.SetPosition(0, a); line.SetPosition(1, b); line.startWidth = line.endWidth = .10f;
        line.material = TransparentMaterial(new Color(color.r, color.g, color.b, 1f));
    }

    private static Material TransparentMaterial(Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        var material = new Material(shader); material.color = color; material.renderQueue = 4100;
        return material;
    }
}
