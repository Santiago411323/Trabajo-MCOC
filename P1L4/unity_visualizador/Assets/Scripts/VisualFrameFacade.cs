using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Decorative infill inside exterior frames. No structural IDs or colliders.</summary>
public sealed class VisualFrameFacade : MonoBehaviour
{
    public readonly List<GameObject> Panels = new List<GameObject>();
    private Material material, frameMaterial, rearCladding;
    private readonly Dictionary<GameObject,Rect> cafeDoorCuts=new Dictionary<GameObject,Rect>();
    public int CafeDoorCount { get; private set; }
    public int AccessDoorCount { get; private set; }
    private static bool Between(FramePost a,FramePost b,string first,string second)
        => (a.Tag==first && b.Tag==second) || (a.Tag==second && b.Tag==first);
    private static bool CafeCantileverFrame(FramePost a,FramePost b)
        => Between(a,b,"E1_241","E1_254") || Between(a,b,"E1_254","E1_255") || Between(a,b,"E1_241","E1_243") ||
           Between(a,b,"E1_230","E1_243") || Between(a,b,"E1_255","E1_257") ||
           Between(a,b,"E1_259","E1_310") || Between(a,b,"E1_271","E1_311") || Between(a,b,"E1_311","E1_310");
    private sealed class FramePost
    {
        public Vector3 Point;
        public float Bottom, Top, Inset;
        public string Tag, Floor;
    }

    public void Build(IEnumerable<ElementSelectable> members, StructureData data, System.Action<GameObject,string> register)
    {
        VisualCafe.TryLayout(data,out var cafe);
        var columns = members.Where(m => m.data != null && m.data.type == "columna").ToList();
        var beams = members.Where(m => m.data != null && m.data.type == "viga").ToList();
        var nodes = data.nodes.ToDictionary(n => n.id, n => new Vector3(n.x,n.z,n.y));
        var basePoints = data.supports.Where(s => nodes.ContainsKey(s.node)).Select(s => nodes[s.node]).ToList();
        if (basePoints.Count == 0) return;
        float minZ = basePoints.Min(p => p.z), maxZ = basePoints.Max(p => p.z);
        // Foundation perimeter defines the main facade, so columns standing on
        // projected balconies in Z do not close those cantilevers with walls.
        columns = columns.Where(c => (c.startPoint.z >= minZ - .01f && c.startPoint.z <= maxZ + .01f) ||
            (VisualCafe.UseDesktopLayout && (c.data.elementTag=="E1_241" || c.data.elementTag=="E1_254" ||
                c.data.elementTag=="E1_310" || c.data.elementTag=="E1_311"))).ToList();
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
        material = new Material(shader) { name = "Vidrio_fachada_visual", color = new Color(.20f,.37f,.42f,.55f) };
        if(material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode",3);material.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);material.SetInt("_ZWrite",0);
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");material.renderQueue=3000;
        }
        if(material.HasProperty("_Surface")){material.SetFloat("_Surface",1);material.renderQueue=3000;}
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .78f);
        frameMaterial=new Material(shader){name="Marcos_ventanas",color=new Color(.11f,.15f,.17f)};
        rearCladding=new Material(shader){name="Revestimiento_naranja_posterior",color=new Color(.79f,.25f,.11f)};
        for (int a = 0; a < posts.Count; a++) for (int b = a + 1; b < posts.Count; b++)
        {
            FramePost first = posts[a], second = posts[b];
            if(VisualCafe.UseDesktopLayout && Between(first,second,"E1_243","E1_255")) continue;
            if(VisualCafe.UseDesktopLayout && Between(first,second,"E1_271","E1_259")) continue;
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
                // Projected upper posts close only their three requested frames;
                // they must not reclassify neighboring foundation-perimeter windows.
                if((c.Tag=="E1_310" || c.Tag=="E1_311") && first.Tag!="E1_310" && first.Tag!="E1_311" && second.Tag!="E1_310" && second.Tag!="E1_311")continue;
                Vector3 v = c.Point; float span = alongX ? v.x : v.z, cross = alongX ? v.z : v.x;
                if (span < left-.01f || span > right+.01f) continue;
                lowerSide |= cross < fixedAxis-.01f; upperSide |= cross > fixedAxis+.01f;
            }
            if (lowerSide && upperSide && !(VisualCafe.UseDesktopLayout && CafeCantileverFrame(first,second))) continue;
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
            float leftInset = (alongX ? p.x : p.z) < (alongX ? q.x : q.z) ? insetA : insetB;
            float rightInset = leftInset == insetA ? insetB : insetA;
            float center=(left+leftInset+right-rightInset)*.5f;
            var cuts=new SortedDictionary<float,float>{{bottom,bottomDepth},{top,topDepth}};
            foreach(var beam in beams)
            {
                Vector3 i=beam.startPoint,j=beam.endPoint;
                if(Mathf.Abs(i.y-j.y)>.01f || i.y<=bottom+.01f || i.y>=top-.01f) continue;
                if(Mathf.Abs((alongX?i.z:i.x)-fixedAxis)>.01f || Mathf.Abs((alongX?j.z:j.x)-fixedAxis)>.01f) continue;
                float from=alongX?Mathf.Min(i.x,j.x):Mathf.Min(i.z,j.z),to=alongX?Mathf.Max(i.x,j.x):Mathf.Max(i.z,j.z);
                if(to<=left+.01f || from>=right-.01f)continue;
                cuts[i.y]=Mathf.Max(cuts.ContainsKey(i.y)?cuts[i.y]:0,beam.data.height_m*.5f);
            }
            var heights=cuts.Keys.ToList();
            for(int level=0;level<heights.Count-1;level++)
            {
                float lower=heights[level]+cuts[heights[level]]+.04f,upper=heights[level+1]-cuts[heights[level+1]]-.04f;
                if(upper-lower<.1f)continue;
                string floor=data.slabs.FirstOrDefault(s=>Mathf.Abs(s.z-heights[level+1])<.02f)?.nivel ?? first.Floor;
                GameObject panel=new GameObject("Ventanas_"+first.Tag+"_"+second.Tag+"_nivel_"+level);panel.layer=2;
                panel.transform.SetParent(transform,false);
                panel.transform.localPosition=alongX?new Vector3(center,(lower+upper)*.5f,fixedAxis):new Vector3(fixedAxis,(lower+upper)*.5f,center);
                if(!alongX)panel.transform.localRotation=Quaternion.Euler(0,90,0);
                if(VisualCafe.UseDesktopLayout && (Between(first,second,"E1_281","E1_293") || Between(first,second,"E1_297","E1_301")))
                {
                    float doorFloor=Mathf.Max(lower,bottom+bottomDepth+.14f);
                    float doorAlong=alongX?center:left+leftInset+.08f+.65f;
                    Vector3 doorBase=alongX?new Vector3(doorAlong,doorFloor,fixedAxis):new Vector3(fixedAxis,doorFloor,doorAlong);
                    Vector3 local=panel.transform.InverseTransformPoint(doorBase);
                    cafeDoorCuts[panel]=new Rect(local.x-.65f,local.y,1.3f,2.2f);
                    var marker=new GameObject("Paso_puerta_acceso_"+(alongX?"E1_281_E1_293":"E1_297_Zpositivo"));
                    marker.layer=2;marker.transform.SetParent(panel.transform,false);
                    marker.transform.localPosition=local+Vector3.up*1.1f;
                    AccessDoorCount++;
                }
                float doorX=cafe!=null?cafe.Left+5f:0f;
                if(VisualCafe.UseDesktopLayout && cafe!=null && alongX &&
                    (Mathf.Abs(fixedAxis-minZ)<.02f || Mathf.Abs(fixedAxis-maxZ)<.02f) &&
                    lower<cafe.Floor+2.22f && upper>cafe.Floor &&
                    doorX-.65f>left+leftInset+.08f && doorX+.65f<right-rightInset-.08f)
                {
                    cafeDoorCuts[panel]=new Rect(doorX-center-.65f,cafe.Floor+.02f-(lower+upper)*.5f,1.3f,2.2f);
                    CafeDoorCount++;
                    var marker=new GameObject("Paso_puerta_cafeteria");marker.layer=2;marker.transform.SetParent(panel.transform,false);
                    marker.transform.localPosition=new Vector3(doorX-center,cafe.Floor+1.12f-(lower+upper)*.5f,0);
                }
                WindowPiece(panel,"Vidrio",Vector3.zero,new Vector3(width,upper-lower,.035f),material);
                float h=upper-lower;
                int panes=Mathf.CeilToInt(width/1.4f);
                for(int m=0;m<=panes;m++)WindowPiece(panel,"Montante",new Vector3(-width/2+width*m/panes,0,0),new Vector3(.045f,h,.07f),frameMaterial);
                if(alongX && Mathf.Abs(fixedAxis-maxZ)<.02f)
                    for(int m=0;m<panes;m++)WindowPiece(panel,"Franja_naranja_fachada_posterior",new Vector3(-width/2+width*(m+.5f)/panes,0,.065f),
                        new Vector3(Mathf.Min(.45f,width/panes*.35f),h,.06f),rearCladding);
                foreach(float y in new[]{-h/2,h/2})WindowPiece(panel,"Marco",new Vector3(0,y,0),new Vector3(width,.055f,.07f),frameMaterial);
                // A horizontal transom keeps long column segments from producing giant panes.
                if(h>2.2f)WindowPiece(panel,"Travesaño",new Vector3(0,h*.22f,0),new Vector3(width,.04f,.07f),frameMaterial);
                Panels.Add(panel);register(panel,floor);
            }
        }
    }
    private void WindowPiece(GameObject parent,string name,Vector3 position,Vector3 size,Material mat)
    {
        if(cafeDoorCuts.TryGetValue(parent,out var door))
        {
            float left=position.x-size.x/2,right=position.x+size.x/2,low=position.y-size.y/2,high=position.y+size.y/2;
            if(left<door.xMax && right>door.xMin && low<door.yMax && high>door.yMin)
            {
                WindowFragment(parent,name,left,Mathf.Min(right,door.xMin),low,high,position.z,size.z,mat);
                WindowFragment(parent,name,Mathf.Max(left,door.xMax),right,low,high,position.z,size.z,mat);
                float a=Mathf.Max(left,door.xMin),b=Mathf.Min(right,door.xMax);
                WindowFragment(parent,name,a,b,low,Mathf.Min(high,door.yMin),position.z,size.z,mat);
                WindowFragment(parent,name,a,b,Mathf.Max(low,door.yMax),high,position.z,size.z,mat);
                return;
            }
        }
        CreateWindowPiece(parent,name,position,size,mat);
    }
    private void WindowFragment(GameObject parent,string name,float left,float right,float low,float high,float z,float depth,Material mat)
    {
        if(right-left>.001f && high-low>.001f)
            CreateWindowPiece(parent,name,new Vector3((left+right)/2,(low+high)/2,z),new Vector3(right-left,high-low,depth),mat);
    }
    private void CreateWindowPiece(GameObject parent,string name,Vector3 position,Vector3 size,Material mat)
    {
        var piece=GameObject.CreatePrimitive(PrimitiveType.Cube);piece.name=name;piece.layer=2;
        piece.transform.SetParent(parent.transform,false);piece.transform.localPosition=position;piece.transform.localScale=size;
        piece.GetComponent<Renderer>().sharedMaterial=mat;
        var collider=piece.GetComponent<Collider>();collider.enabled=false;
        if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);
    }
    private void OnDestroy()
    {
        if (material == null) return;
        if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        if(frameMaterial!=null){if(Application.isPlaying)Destroy(frameMaterial);else DestroyImmediate(frameMaterial);}
        if(rearCladding!=null){if(Application.isPlaying)Destroy(rearCladding);else DestroyImmediate(rearCladding);}
    }
}
