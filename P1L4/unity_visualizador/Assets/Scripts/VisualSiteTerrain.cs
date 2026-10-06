using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Decorative site only: no colliders, structural IDs, loads or model edits.</summary>
public sealed class VisualSiteTerrain : MonoBehaviour
{
    private readonly List<Object> resources = new List<Object>();
    public int PlatformCount { get; private set; }
    public int SlopeCount { get; private set; }
    public float Clearance { get; private set; }
    public GameObject BaseGround { get; private set; }
    public GameObject UpperTerrace { get; private set; }
    public float TerraceContactX { get; private set; }
    private Transform groupParent;
    private VisualCafe.Layout cafeLayout;
    private float cafeStairStartX;

    public void Build(StructureData data, IEnumerable<Renderer> buildingRenderers = null)
    {
        VisualCafe.TryLayout(data,out cafeLayout);
        cafeStairStartX=data.nodes.Where(n=>Mathf.Abs(n.z-4)<.02f).Select(n=>n.x).DefaultIfEmpty(20).Max()+1;
        BaseGround = new GameObject("Pasto_y_talud_niveles_base");
        BaseGround.transform.SetParent(transform, false); groupParent = BaseGround.transform;
        var nodes = data.nodes.ToDictionary(n => n.id, n => new Vector3(n.x, n.z, n.y));
        var bases = (data.supports ?? new SupportData[0]).Where(s =>
            s.ux == 1 && s.uy == 1 && s.uz == 1 && s.rx == 1 && s.ry == 1 && s.rz == 1 && nodes.ContainsKey(s.node))
            .Select(s => nodes[s.node]).Distinct().ToList();
        if (bases.Count == 0) return;
        var levels = bases.GroupBy(p => Mathf.Round(p.y * 100f) / 100f).OrderBy(g => g.Key).ToList();
        Clearance = .22f;
        // Ground stays below the full depth of any foundation-level beam.
        foreach (ElementData e in data.elements)
            if (e.type == "viga" && nodes.ContainsKey(e.nodeI) && nodes.ContainsKey(e.nodeJ) &&
                levels.Any(g => Mathf.Abs(nodes[e.nodeI].y - g.Key) < .01f && Mathf.Abs(nodes[e.nodeJ].y - g.Key) < .01f))
                Clearance = Mathf.Max(Clearance, e.height_m * .5f + .1f);
        Material grass = SurfaceMaterial(true);
        Material rock = SurfaceMaterial(false);
        float zMin = bases.Min(p => p.z) - 12f, zMax = bases.Max(p => p.z) + 12f;
        float bottom = levels[0].Key - Clearance - .4f;
        var strips = new List<Vector3>(); // x minimum, x maximum, surface elevation
        foreach (var level in levels)
        {
            float left = level.Min(p => p.x) - (level == levels[0] ? 12f : 2f);
            float right = level.Max(p => p.x) + (level == levels[levels.Count - 1] ? 27f : 2f);
            float top = level.Key - Clearance;
            strips.Add(new Vector3(left, right, top));
            Surface("Pasto_nivel_" + level.Key, left, right, zMin, zMax, (x, z) => top, grass, 1, 1);
            Edge("Roca_borde_izquierdo", new Vector3(left, top, zMin), new Vector3(left, top, zMax), bottom, rock);
            Edge("Roca_borde_derecho", new Vector3(right, top, zMax), new Vector3(right, top, zMin), bottom, rock);
            Edge("Roca_borde_frontal", new Vector3(right, top, zMin), new Vector3(left, top, zMin), bottom, rock);
            Edge("Roca_borde_posterior", new Vector3(left, top, zMax), new Vector3(right, top, zMax), bottom, rock);
            PlatformCount++;
            // Small stone plinths fill the visual clearance below the existing orange support plates.
            foreach (Vector3 point in level)
            {
                float height = Clearance - .15f;
                GameObject plinth = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                plinth.name = "Piedra_bajo_apoyo"; plinth.layer = 2;
                plinth.transform.SetParent(groupParent, false);
                plinth.transform.localPosition = new Vector3(point.x, top + height * .5f, point.z);
                plinth.transform.localScale = new Vector3(.72f, height * .5f, .72f);
                plinth.GetComponent<Renderer>().sharedMaterial = rock;
                Collider collider = plinth.GetComponent<Collider>(); collider.enabled = false; Release(collider);
            }
        }
        // Connect disjoint terraces across the open horizontal gap. No slope is
        // generated through overlapping building footprints.
        for (int i = 0; i + 1 < strips.Count; i++)
        {
            Vector3 a = strips[i], b = strips[i + 1];
            if (a.y >= b.x) continue;
            float start = a.y, end = b.x;
            System.Func<float, float, float> height = (x, z) => {
                float t = Mathf.InverseLerp(start, end, x);
                float rough = (Mathf.PerlinNoise(x * 1.3f + 9f, z * 1.3f + 3f) - .5f) * .22f * Mathf.Sin(t * Mathf.PI);
                return Mathf.Lerp(a.z, b.z, t) + rough;
            };
            Surface("Talud_rocoso_entre_niveles", start, end, zMin, zMax, height, rock, 24, 48);
            var random = new System.Random(41 + i);
            for (int stone = 0; stone < 34; stone++)
            {
                float x = Mathf.Lerp(start, end, .12f + (float)random.NextDouble() * .76f);
                float z = Mathf.Lerp(zMin + .5f, zMax - .5f, (float)random.NextDouble());
                float y = height(x, z);
                float radius = .22f + (float)random.NextDouble() * .30f;
                float rise = Mathf.Min(.18f + (float)random.NextDouble() * .28f, b.z - .1f - y);
                if (rise > .05f) Stone(new Vector3(x, y, z), radius, rise, rock);
            }
            Edge("Talud_borde_frontal", new Vector3(end, b.z, zMin), new Vector3(start, a.z, zMin), bottom, rock);
            Edge("Talud_borde_posterior", new Vector3(start, a.z, zMax), new Vector3(end, b.z, zMax), bottom, rock);
            SlopeCount++;
        }
        BuildUpperTerrace(nodes, bases, levels[levels.Count - 1].Key - Clearance, bottom, grass, rock, buildingRenderers);
        var campus=new GameObject("Entorno_campus_referencia");campus.transform.SetParent(BaseGround.transform,false);
        campus.AddComponent<VisualCampusSite>().Build(data,Clearance,UpperTerrace!=null?4f:levels[levels.Count-1].Key-Clearance);
    }

    private void BuildUpperTerrace(Dictionary<int, Vector3> nodes, List<Vector3> bases,
        float baseHeight, float bottom, Material grass, Material rock, IEnumerable<Renderer> renderers)
    {
        float elevation = 4f;
        var edgeNodes = nodes.Values.Where(p => Mathf.Abs(p.y - elevation) < .01f).ToList();
        if (edgeNodes.Count == 0) return;
        float contactX = edgeNodes.Max(p => p.x);
        if (renderers != null)
        {
            var edges = renderers.Where(r => r != null && r.bounds.min.y <= elevation && r.bounds.max.y >= elevation).ToList();
            if (edges.Count > 0) contactX = edges.Max(r => r.bounds.max.x);
        }
        contactX -= .005f; // Small overlap makes contact with the actual beam face, without a visible gap.
        float end = bases.Where(p => Mathf.Abs(p.y) < .01f).Max(p => p.x) + 27f;
        float minZ = bases.Min(p => p.z) - 12f, maxZ = bases.Max(p => p.z) + 12f;
        TerraceContactX = contactX;
        float rampStart = contactX - 9f;
        UpperTerrace = new GameObject("Terraza_pasto_Y4_y_bajada_roca");
        UpperTerrace.transform.SetParent(transform, false); groupParent = UpperTerrace.transform;
        // The requested full-width slope descends toward -X, including the building footprint.
        Surface("Pasto_terraza_Y4", contactX, end, minZ, maxZ, (x,z) => elevation, grass, 1, 1);
        Edge("Terraza_frente", new Vector3(end, elevation, minZ), new Vector3(contactX, elevation, minZ), baseHeight, rock);
        Edge("Terraza_fondo", new Vector3(contactX, elevation, maxZ), new Vector3(end, elevation, maxZ), baseHeight, rock);
        Edge("Terraza_borde_exterior", new Vector3(end, elevation, maxZ), new Vector3(end, elevation, minZ), baseHeight, rock);
        System.Func<float,float,float> height = (x,z) => {
            float t = Mathf.InverseLerp(rampStart, contactX, x);
            return Mathf.Lerp(baseHeight, elevation, t) + (Mathf.PerlinNoise(x,z)-.5f)*.2f*Mathf.Sin(t*Mathf.PI);
        };
        Surface("Talud_Y4_hacia_menos_X", rampStart, contactX, minZ, maxZ, height, rock, 32, 48);
        Edge("Bajada_frente", new Vector3(contactX,elevation,minZ), new Vector3(rampStart,baseHeight,minZ), baseHeight, rock);
        Edge("Bajada_fondo", new Vector3(rampStart,baseHeight,maxZ), new Vector3(contactX,elevation,maxZ), baseHeight, rock);
        var random = new System.Random(94);
        for (int i = 0; i < 32; i++)
        {
            float x = Mathf.Lerp(rampStart,contactX,.08f+(float)random.NextDouble()*.84f);
            float z = Mathf.Lerp(minZ+.5f,maxZ-.5f,(float)random.NextDouble());
            Stone(new Vector3(x,height(x,z),z),.2f+(float)random.NextDouble()*.3f,.15f+(float)random.NextDouble()*.25f,rock);
        }
        PlatformCount++; SlopeCount++;
        groupParent = BaseGround.transform;
    }

    public void SetVisibility(bool ground, bool terrace)
    {
        if (BaseGround != null) BaseGround.SetActive(ground);
        if (UpperTerrace != null) UpperTerrace.SetActive(terrace);
    }

    private Material SurfaceMaterial(bool grass)
    {
        Shader shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
        Material material = new Material(shader) { name = grass ? "Pasto_visual" : "Roca_visual" };
        Texture2D texture = new Texture2D(128, 128, TextureFormat.RGB24, true) {
            name = material.name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4
        };
        Color dark = grass ? new Color(.16f, .28f, .065f) : new Color(.22f, .225f, .20f);
        Color light = grass ? new Color(.40f, .53f, .20f) : new Color(.55f, .52f, .44f);
        Color[] pixels = new Color[128 * 128];
        for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
        {
            float noise = Mathf.PerlinNoise(x / 14f, y / 14f) * .65f + Mathf.PerlinNoise(x / 2f + 41, y / 2f + 23) * .35f;
            pixels[y * 128 + x] = Color.Lerp(dark, light, noise);
        }
        texture.SetPixels(pixels); texture.Apply(true, true);
        material.mainTexture = texture; material.color = Color.white;
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .04f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .04f);
        resources.Add(texture); resources.Add(material);
        return material;
    }

    private void Surface(string name, float minX, float maxX, float minZ, float maxZ,
        System.Func<float, float, float> height, Material material, int columns, int rows)
    {
        if(cafeLayout!=null && minX<cafeStairStartX+1 && maxX>cafeLayout.Left-.4f && minZ<cafeLayout.Back+.3f && maxZ>cafeLayout.PatioFront-.3f)
        {columns=Mathf.Max(columns,Mathf.CeilToInt((maxX-minX)/.7f));rows=Mathf.Max(rows,Mathf.CeilToInt((maxZ-minZ)/.7f));}
        Vector3[] vertices = new Vector3[(columns + 1) * (rows + 1)];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[columns * rows * 6];
        for (int z = 0; z <= rows; z++) for (int x = 0; x <= columns; x++)
        {
            float px = Mathf.Lerp(minX, maxX, x / (float)columns), pz = Mathf.Lerp(minZ, maxZ, z / (float)rows);
            int index = z * (columns + 1) + x;
            vertices[index] = new Vector3(px, VisualCafe.CutHeight(cafeLayout,px,pz,height(px,pz),cafeStairStartX), pz); uv[index] = new Vector2(px, pz) * .65f;
        }
        int k = 0;
        for (int z = 0; z < rows; z++) for (int x = 0; x < columns; x++)
        {
            int a = z * (columns + 1) + x, b = a + columns + 1;
            triangles[k++] = a; triangles[k++] = b; triangles[k++] = a + 1;
            triangles[k++] = a + 1; triangles[k++] = b; triangles[k++] = b + 1;
        }
        DrawMesh(name, vertices, uv, triangles, material);
    }

    private void Stone(Vector3 center, float radius, float height, Material material)
    {
        Vector3[] corners = {
            center + new Vector3(-radius, -.08f, -radius * .55f),
            center + new Vector3(radius * .75f, -.08f, -radius),
            center + new Vector3(radius, -.08f, radius * .6f),
            center + new Vector3(-radius * .6f, -.08f, radius),
            center + new Vector3(-radius * .15f, height, radius * .1f)
        };
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        // Separate triangle vertices give each rock face its own flat normal.
        for (int side = 0; side < 4; side++)
        {
            foreach (int corner in new[] { side, 4, (side + 1) % 4 })
            { triangles.Add(vertices.Count); vertices.Add(corners[corner]); uv.Add(new Vector2(corners[corner].x, corners[corner].z)); }
        }
        DrawMesh("Roca_del_talud", vertices.ToArray(), uv.ToArray(), triangles.ToArray(), material);
    }

    private void Edge(string name, Vector3 a, Vector3 b, float bottom, Material material)
    {
        int segments=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/.7f));
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        for(int s=0;s<segments;s++)
        {
            Vector3 p=Vector3.Lerp(a,b,s/(float)segments),q=Vector3.Lerp(a,b,(s+1f)/segments);
            p.y=VisualCafe.CutHeight(cafeLayout,p.x,p.z,p.y,cafeStairStartX);q.y=VisualCafe.CutHeight(cafeLayout,q.x,q.z,q.y,cafeStairStartX);
            int i=vertices.Count;vertices.AddRange(new[]{p,q,new Vector3(p.x,bottom,p.z),new Vector3(q.x,bottom,q.z)});
            uv.AddRange(new[]{new Vector2(s,p.y),new Vector2(s+1,q.y),new Vector2(s,bottom),new Vector2(s+1,bottom)});
            triangles.AddRange(new[]{i,i+2,i+1,i+1,i+2,i+3});
        }
        DrawMesh(name,vertices.ToArray(),uv.ToArray(),triangles.ToArray(),material);
    }

    private void DrawMesh(string name, Vector3[] vertices, Vector2[] uv, int[] triangles, Material material)
    {
        Mesh mesh = new Mesh { name = name }; mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); resources.Add(mesh);
        GameObject piece = new GameObject(name); piece.layer = 2; piece.transform.SetParent(groupParent, false);
        piece.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = piece.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static void Release(Object item)
    {
        if (item == null) return;
        if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
    }
    private void OnDestroy() { foreach (Object item in resources) Release(item); resources.Clear(); }
}
