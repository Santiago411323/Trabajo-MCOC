using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Grey visual caps on exposed roof slabs; no loads or collision geometry.</summary>
public sealed class VisualFlatRoof : MonoBehaviour
{
    public readonly List<GameObject> Pieces = new List<GameObject>();
    private Material material;
    public void Build(StructureData data, System.Action<GameObject,string> register)
    {
        if(data.slabs==null) return;
        Shader shader=Shader.Find("Standard")??Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Sprites/Default");
        material=new Material(shader){name="Cubierta_plana_gris",color=new Color(.49f,.51f,.53f)};
        if(material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness",.06f);
        foreach(var slab in data.slabs)
        {
            var exposed=new List<Rect>{Rect.MinMaxRect(Mathf.Min(slab.x0,slab.x1),Mathf.Min(slab.y0,slab.y1),Mathf.Max(slab.x0,slab.x1),Mathf.Max(slab.y0,slab.y1))};
            // Subtract higher slabs so intermediate exposed roof areas are
            // covered too, without drawing grey caps on interior floors.
            // L96 is an explicitly requested complete cover, including the portion below L99.
            foreach(var higher in data.slabs.Where(s=>slab.id!="L96" && s.z>slab.z+.01f))
            {
                Rect cover=Rect.MinMaxRect(Mathf.Min(higher.x0,higher.x1),Mathf.Min(higher.y0,higher.y1),Mathf.Max(higher.x0,higher.x1),Mathf.Max(higher.y0,higher.y1));
                var remaining=new List<Rect>();
                foreach(Rect part in exposed) Subtract(part,cover,remaining);
                exposed=remaining;
                if(exposed.Count==0) break;
            }
            float depth=data.elements.Where(e=>e.type=="viga").Select(e=>e.height_m*.5f).DefaultIfEmpty(.4f).Max();
            foreach(Rect part in exposed)
            {
                if(part.width<.02f || part.height<.02f) continue;
                GameObject roof=GameObject.CreatePrimitive(PrimitiveType.Cube);
                roof.name="Techo_plano_visual_"+slab.id; roof.layer=2; roof.transform.SetParent(transform,false);
                roof.transform.localPosition=new Vector3(part.center.x,slab.z+depth+.08f,part.center.y);
                roof.transform.localScale=new Vector3(part.width,.12f,part.height);
                roof.GetComponent<Renderer>().sharedMaterial=material;
                var collider=roof.GetComponent<Collider>();collider.enabled=false;
                if(Application.isPlaying) Destroy(collider);else DestroyImmediate(collider);
                Pieces.Add(roof);register(roof,slab.nivel);
            }
        }
    }
    private static void Subtract(Rect part,Rect cover,List<Rect> output)
    {
        float left=Mathf.Max(part.xMin,cover.xMin),right=Mathf.Min(part.xMax,cover.xMax);
        float front=Mathf.Max(part.yMin,cover.yMin),back=Mathf.Min(part.yMax,cover.yMax);
        if(right<=left || back<=front){output.Add(part);return;}
        if(left>part.xMin) output.Add(Rect.MinMaxRect(part.xMin,part.yMin,left,part.yMax));
        if(right<part.xMax) output.Add(Rect.MinMaxRect(right,part.yMin,part.xMax,part.yMax));
        if(front>part.yMin) output.Add(Rect.MinMaxRect(left,part.yMin,right,front));
        if(back<part.yMax) output.Add(Rect.MinMaxRect(left,back,right,part.yMax));
    }
    private void OnDestroy(){if(material!=null){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}}
}
