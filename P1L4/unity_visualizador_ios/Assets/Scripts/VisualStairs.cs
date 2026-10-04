using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Decorative stairs only. No structural elements, loads, selection or colliders.</summary>
public sealed class VisualStairs : MonoBehaviour
{
    public readonly List<GameObject> Pieces = new List<GameObject>();
    public int FlightCount { get; private set; }
    private Material material;

    public void Build(StructureData data, float terraceX, System.Action<GameObject,string> register)
    {
        var nodes = data.nodes.ToDictionary(n => n.id, n => new Vector3(n.x,n.z,n.y));
        ElementData upper = data.elements.FirstOrDefault(e => e.elementTag == "E1_207");
        ElementData lower = data.elements.FirstOrDefault(e => e.elementTag == "E1_218");
        ElementData exit = data.elements.FirstOrDefault(e => e.elementTag == "E1_220");
        Shader shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
        material = new Material(shader) { name = "Escaleras_grises_visuales", color = new Color(.61f,.62f,.63f) };
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness",.05f);
        if (upper != null && lower != null)
        {
            Vector3 a = BeamLanding(upper,nodes), b = BeamLanding(lower,nodes);
            // Shared Z aligns both landings inside the actual beam spans.
            float z = (a.z+b.z)*.5f; a.z=z; b.z=z;
            Flight("Escalera_E1_207_E1_218",a,b,upper.piso,register);
        }
        if (exit != null)
        {
            Vector3 a = BeamLanding(exit,nodes);
            Flight("Escalera_E1_220_Terraza_Y4",a,new Vector3(terraceX+.4f,4.03f,a.z),exit.piso,register);
        }
    }

    private static Vector3 BeamLanding(ElementData beam, Dictionary<int,Vector3> nodes)
    {
        Vector3 point = (nodes[beam.nodeI]+nodes[beam.nodeJ])*.5f;
        point.y += beam.height_m*.5f+.14f; // Matches the walking surface of the grey slab cap.
        return point;
    }

    private void Flight(string name, Vector3 start, Vector3 end, string floor, System.Action<GameObject,string> register)
    {
        Vector3 horizontal = end-start; horizontal.y=0;
        float length = horizontal.magnitude;
        if (length < .8f) return;
        Vector3 direction = horizontal/length;
        Quaternion rotation = Quaternion.LookRotation(direction,Vector3.up);
        const float width = 1.4f, landing = .6f;
        Box(name+"_descanso_inicio",start-Vector3.up*.09f,new Vector3(width,.18f,landing),rotation,floor,register);
        Box(name+"_descanso_final",end-Vector3.up*.09f,new Vector3(width,.18f,landing),rotation,floor,register);
        int steps = Mathf.Max(1,Mathf.CeilToInt(Mathf.Abs(end.y-start.y)/.18f));
        float run = length-landing;
        float tread = run/steps;
        float riser = Mathf.Abs(end.y-start.y)/steps;
        for (int i=0;i<steps;i++)
        {
            float top = Mathf.Lerp(start.y,end.y,(i+.5f)/steps);
            Vector3 center = start+direction*(landing*.5f+(i+.5f)*tread);
            center.y=top-(riser+.18f)*.5f;
            Box(name+"_peldaño_"+(i+1),center,new Vector3(width,riser+.18f,tread+.005f),rotation,floor,register);
        }
        FlightCount++;
    }

    private void Box(string name, Vector3 center, Vector3 size, Quaternion rotation, string floor, System.Action<GameObject,string> register)
    {
        GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        piece.name=name; piece.layer=2; piece.transform.SetParent(transform,false);
        piece.transform.localPosition=center; piece.transform.localRotation=rotation; piece.transform.localScale=size;
        piece.GetComponent<Renderer>().sharedMaterial=material;
        Collider collider=piece.GetComponent<Collider>(); collider.enabled=false;
        if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
        Pieces.Add(piece); register(piece,floor);
    }

    private void OnDestroy()
    {
        if(material==null) return;
        if(Application.isPlaying) Destroy(material); else DestroyImmediate(material);
    }
}
