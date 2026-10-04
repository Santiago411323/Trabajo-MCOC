using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Decorative infill inside exterior frames. No structural IDs or colliders.</summary>
public sealed class VisualFrameFacade : MonoBehaviour
{
    public readonly List<GameObject> Panels = new List<GameObject>();
    private Material material;
    private sealed class FramePost
    {
        public Vector3 Point;
        public float Bottom, Top, Inset;
        public string Tag, Floor;
    }

    public void Build(IEnumerable<ElementSelectable> members, StructureData data, System.Action<GameObject,string> register)
    {
        var columns = members.Where(m => m.data != null && m.data.type == "columna").ToList();
        var beams = members.Where(m => m.data != null && m.data.type == "viga").ToList();
        var nodes = data.nodes.ToDictionary(n => n.id, n => new Vector3(n.x,n.z,n.y));
        var basePoints = data.supports.Where(s => nodes.ContainsKey(s.node)).Select(s => nodes[s.node]).ToList();
        if (basePoints.Count == 0) return;
        float minZ = basePoints.Min(p => p.z), maxZ = basePoints.Max(p => p.z);
        // Foundation perimeter defines the main facade, so columns standing on
        // projected balconies in Z do not close those cantilevers with walls.
        columns = columns.Where(c => c.startPoint.z >= minZ - .01f && c.startPoint.z <= maxZ + .01f).ToList();
        var posts = columns.Select(c => new FramePost {
            Point=c.startPoint, Bottom=Mathf.Min(c.startPoint.y,c.endPoint.y), Top=Mathf.Max(c.startPoint.y,c.endPoint.y),
            Inset=Mathf.Max(c.data.width_m,c.data.height_m)*.5f+.035f, Tag=c.data.elementTag, Floor=c.visualFloor
        }).ToList();
        float west = basePoints.Min(p => p.x);
        foreach (var wall in members.Where(m => m.isWall))
        {
            Vector3 a=wall.startPoint, b=wall.endPoint;
            bool exterior = (Mathf.Abs(a.x-west)<.02f && Mathf.Abs(b.x-west)<.02f) ||
                (Mathf.Abs(a.z-minZ)<.02f && Mathf.Abs(b.z-minZ)<.02f) ||
                (Mathf.Abs(a.z-maxZ)<.02f && Mathf.Abs(b.z-maxZ)<.02f);
            if (!exterior) continue;
            float bottom=a.y, top=b.y;
            string floor=data.slabs.FirstOrDefault(s => Mathf.Abs(s.z-top)<.02f)?.nivel ?? wall.wallTop;
            foreach(Vector3 endpoint in new[]{a,b})
            {
                Vector3 p=new Vector3(endpoint.x,bottom,Mathf.Clamp(endpoint.z,minZ,maxZ));
                if(posts.Any(post => (post.Point-p).sqrMagnitude<.0001f && Mathf.Abs(post.Top-top)<.02f)) continue;
                posts.Add(new FramePost {Point=p,Bottom=bottom,Top=top,Inset=.015f,Tag="Muro_"+wall.wallId,Floor=floor});
            }
        }
        Shader shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
        material = new Material(shader) { name = "Salmon_fachada_visual", color = new Color(1f, .49f, .38f) };
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .12f);
        for (int a = 0; a < posts.Count; a++) for (int b = a + 1; b < posts.Count; b++)
        {
            FramePost first = posts[a], second = posts[b];
            float bottom = first.Bottom, top = first.Top;
            if (Mathf.Abs(bottom-second.Bottom)>.01f || Mathf.Abs(top-second.Top)>.01f) continue;
            Vector3 p = first.Point, q = second.Point;
            bool alongX = Mathf.Abs(p.z-q.z) < .01f && Mathf.Abs(p.x-q.x) > .1f;
            bool alongZ = Mathf.Abs(p.x-q.x) < .01f && Mathf.Abs(p.z-q.z) > .1f;
            if (!alongX && !alongZ) continue;
            float left = alongX ? Mathf.Min(p.x,q.x) : Mathf.Min(p.z,q.z);
            float right = alongX ? Mathf.Max(p.x,q.x) : Mathf.Max(p.z,q.z);
            float fixedAxis = alongX ? p.z : p.x;
            var story = posts.Where(c => Mathf.Abs(c.Bottom-bottom)<.01f && Mathf.Abs(c.Top-top)<.01f).ToList();
            if (story.Any(c => {
                Vector3 v = c.Point; float span = alongX ? v.x : v.z, cross = alongX ? v.z : v.x;
                return Mathf.Abs(cross-fixedAxis)<.01f && span>left+.01f && span<right-.01f;
            })) continue;
            bool lowerSide = false, upperSide = false;
            foreach (var c in story)
            {
                Vector3 v = c.Point; float span = alongX ? v.x : v.z, cross = alongX ? v.z : v.x;
                if (span < left-.01f || span > right+.01f) continue;
                lowerSide |= cross < fixedAxis-.01f; upperSide |= cross > fixedAxis+.01f;
            }
            if (lowerSide && upperSide) continue; // Interior frame.
            bool withinStructuralWall = members.Where(m => m.isWall).Any(w => {
                Vector3 i=w.startPoint,j=w.endPoint;
                float from=alongX?Mathf.Min(i.x,j.x):Mathf.Min(i.z,j.z);
                float to=alongX?Mathf.Max(i.x,j.x):Mathf.Max(i.z,j.z);
                return Mathf.Abs(i.y-bottom)<.02f && Mathf.Abs(j.y-top)<.02f &&
                    Mathf.Abs((alongX?i.z:i.x)-fixedAxis)<.02f && Mathf.Abs((alongX?j.z:j.x)-fixedAxis)<.02f &&
                    from<=left+.02f && to>=right-.02f;
            });
            if(withinStructuralWall) continue;
            var intervals = new List<Vector2>();
            float topDepth = .0f, bottomDepth = .0f;
            foreach (var beam in beams)
            {
                Vector3 i = beam.startPoint, j = beam.endPoint;
                if (Mathf.Abs((alongX ? i.z : i.x)-fixedAxis)>.01f || Mathf.Abs((alongX ? j.z : j.x)-fixedAxis)>.01f) continue;
                float from = alongX ? Mathf.Min(i.x,j.x) : Mathf.Min(i.z,j.z);
                float to = alongX ? Mathf.Max(i.x,j.x) : Mathf.Max(i.z,j.z);
                if (to <= left+.01f || from >= right-.01f) continue;
                if (Mathf.Abs(i.y-top)<.01f && Mathf.Abs(j.y-top)<.01f)
                { intervals.Add(new Vector2(from,to)); topDepth=Mathf.Max(topDepth,beam.data.height_m*.5f); }
                if (Mathf.Abs(i.y-bottom)<.01f && Mathf.Abs(j.y-bottom)<.01f)
                    bottomDepth=Mathf.Max(bottomDepth,beam.data.height_m*.5f);
            }
            float covered = left;
            foreach (var segment in intervals.OrderBy(s => s.x))
            { if (segment.x > covered+.02f) break; covered=Mathf.Max(covered,segment.y); }
            if (covered < right-.02f) continue; // No complete top beam: not a frame.
            float insetA = first.Inset, insetB = second.Inset;
            float width = right-left-insetA-insetB;
            float low = bottom+bottomDepth+.04f, high=top-topDepth-.04f;
            if (width < .1f || high-low < .1f) continue;
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Cerramiento_visual_" + first.Tag + "_" + second.Tag;
            panel.layer=2; panel.transform.SetParent(transform,false);
            float leftInset = (alongX ? p.x : p.z) < (alongX ? q.x : q.z) ? insetA : insetB;
            float rightInset = leftInset == insetA ? insetB : insetA;
            float center=(left+leftInset+right-rightInset)*.5f;
            panel.transform.localPosition = alongX ? new Vector3(center,(low+high)*.5f,fixedAxis) : new Vector3(fixedAxis,(low+high)*.5f,center);
            panel.transform.localScale = alongX ? new Vector3(width,high-low,.08f) : new Vector3(.08f,high-low,width);
            panel.GetComponent<Renderer>().sharedMaterial=material;
            var collider=panel.GetComponent<Collider>(); collider.enabled=false;
            if(Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            Panels.Add(panel); register(panel,first.Floor);
        }
    }
    private void OnDestroy()
    {
        if (material == null) return;
        if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
    }
}
