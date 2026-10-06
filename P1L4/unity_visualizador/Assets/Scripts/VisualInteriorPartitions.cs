using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Architectural partitions on Unity Y4 only. No analytical elements, IDs or loads.
public sealed class VisualInteriorPartitions : MonoBehaviour
{
    public readonly List<GameObject> Pieces=new List<GameObject>();
    public readonly List<Vector2> CoreOpenings=new List<Vector2>();
    public readonly List<Vector2> CentralCoreOpenings=new List<Vector2>();
    public readonly List<Vector3> DoorCenters=new List<Vector3>();
    public readonly List<Transform> Tables=new List<Transform>();
    public readonly List<GameObject> Furniture=new List<GameObject>();
    private readonly List<Material> materials=new List<Material>();
    private readonly List<Vector3> doors=new List<Vector3>(); // X, inward Z direction, corridor Z.
    private Material cement,wood,metal,fabric;
    private const float Low=4.44f,High=7.56f,Thickness=.12f;

    public void Build(StructureData data,Transform existingRoom)
    {
        cement=new Material(Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit"))
            {name="Tabiques_visuales_cemento_Y4",color=new Color(.57f,.58f,.56f)};
        if(cement.HasProperty("_Glossiness"))cement.SetFloat("_Glossiness",.06f);
        materials.Add(cement);
        wood=Material("Mobiliario_habitaciones_madera",new Color(.64f,.46f,.28f));
        metal=Material("Mobiliario_habitaciones_metal",new Color(.2f,.22f,.24f));
        fabric=Material("Sillas_habitaciones",new Color(.28f,.37f,.42f));
        var nodes=data.nodes.ToDictionary(n=>n.id,n=>new Vector3(n.x,n.z,n.y));
        doors.AddRange(new[]{new Vector3(7.5f,1,2.8f),new Vector3(20,1,2.8f),new Vector3(30,1,2.8f),
            new Vector3(14,-1,0),new Vector3(25,-1,0)});
        var first=data.elements.Single(e=>e.elementTag=="E1_281");var second=data.elements.Single(e=>e.elementTag=="E1_285");
        var a=nodes[first.nodeI];var b=nodes[second.nodeI];
        WallZ("Tabique_visual_E1_281_E1_285",a.x,a.z+first.width_m/2,b.z-second.width_m/2);

        var join=data.walls.Single(w=>w.id==53);var corner=nodes[join.nodeI];
        var facadeColumn=data.elements.Single(e=>e.elementTag=="E1_297");
        float start=corner.x+join.grosor/2,end=nodes[facadeColumn.nodeI].x,z=Mathf.Min(nodes[join.nodeI].z,nodes[join.nodeJ].z);
        var spans=new List<Vector2>{new Vector2(start,end)};
        foreach(int id in new[]{28,23,18})
        {
            var core=data.walls.Single(w=>w.id==id);var p=nodes[core.nodeI];var q=nodes[core.nodeJ];
            float left=Mathf.Min(p.x,q.x)-core.grosor/2-.45f,right=Mathf.Max(p.x,q.x)+core.grosor/2+.45f;
            CoreOpenings.Add(new Vector2(left,right));Subtract(spans,left,right);
        }
        int index=0;foreach(var span in spans)WallX("Tabique_visual_unido_muro53_"+index++,span.x,span.y,z);

        foreach(int id in new[]{8,3,13})
        {
            var core=data.walls.Single(w=>w.id==id);var p=nodes[core.nodeI];var q=nodes[core.nodeJ];
            CentralCoreOpenings.Add(new Vector2(Mathf.Min(p.x,q.x)-core.grosor/2-.45f,Mathf.Max(p.x,q.x)+core.grosor/2+.45f));
        }
        foreach(string tag in new[]{"E1_41","E1_33","E1_31","E1_28"})
        {
            var beam=data.elements.Single(e=>e.elementTag==tag);var p=nodes[beam.nodeI];var q=nodes[beam.nodeJ];
            bool front=Mathf.Min(p.z,q.z)<0;
            WallZ("Tabique_visual_sobre_"+tag,p.x,front?Mathf.Min(p.z,q.z):z-Thickness/2,front?Thickness/2:Mathf.Max(p.z,q.z));
        }

        var posts=data.elements.Where(e=>e.type=="columna" && (e.elementTag ?? "").StartsWith("E1_") &&
            Mathf.Abs(nodes[e.nodeI].y-4)<.01f && Mathf.Abs(nodes[e.nodeI].z)<.01f).OrderBy(e=>nodes[e.nodeI].x).ToArray();
        // Include the room's lintel in the exclusion, preserving its door opening.
        var roomWalls=existingRoom.GetComponentsInChildren<Renderer>().Where(r=>
            (r.name.StartsWith("Muro_visual_") || r.name.StartsWith("Dintel_")) &&
            Mathf.Abs(r.bounds.center.z)<.08f && r.bounds.size.x>r.bounds.size.z).Select(r=>r.bounds).ToArray();
        for(int i=0;i+1<posts.Length;i++)
        {
            var left=posts[i];var right=posts[i+1];
            if(left.elementTag=="E1_285" && right.elementTag=="E1_297")continue;
            var pieces=new List<Vector2>{new Vector2(nodes[left.nodeI].x+left.width_m/2,nodes[right.nodeI].x-right.width_m/2)};
            foreach(var room in roomWalls)Subtract(pieces,room.min.x-.01f,room.max.x+.01f);
            foreach(var opening in CentralCoreOpenings)Subtract(pieces,opening.x,opening.y);
            foreach(var piece in pieces)
                WallX("Tabique_visual_central_"+left.elementTag+"_"+right.elementTag,piece.x,piece.y,0);
        }
        foreach(var door in doors)Door(door);
        var rooms=new[]{Rect.MinMaxRect(-10,z,0,8.9f),Rect.MinMaxRect(0,z,15,8.9f),Rect.MinMaxRect(15,z,25,8.9f),
            Rect.MinMaxRect(25,z,35,8.9f),Rect.MinMaxRect(7.51f,-7.25f,20,0),Rect.MinMaxRect(20,-7.25f,30,0)};
        int roomIndex=0;
        foreach(var room in rooms)
        {
            roomIndex++;
            if(roomIndex==1)continue; // Keep the access sector of structural walls18/23/28 empty.
            foreach(float fraction in new[]{.28f,.72f})
                Table("Mesa_habitacion_"+roomIndex+"_"+fraction,new Vector3(Mathf.Lerp(room.xMin,room.xMax,fraction),4.54f,Mathf.Lerp(room.yMin,room.yMax,room.yMin>=0?.62f:.4f)));
        }
        // Keep the existing laptop tables and add seats without changing their positions.
        foreach(var table in existingRoom.GetComponent<VisualStudyRoom>().Tables)
            foreach(float side in new[]{-1f,1f})Chair(table.position+new Vector3(0,0,side*.95f),side<0?0:180);
    }
    private static void Subtract(List<Vector2> spans,float left,float right)
    {
        var previous=spans.ToArray();spans.Clear();
        foreach(var span in previous)
        {
            if(right<=span.x || left>=span.y){spans.Add(span);continue;}
            if(left>span.x)spans.Add(new Vector2(span.x,Mathf.Min(left,span.y)));
            if(right<span.y)spans.Add(new Vector2(Mathf.Max(right,span.x),span.y));
        }
    }
    private void WallX(string name,float left,float right,float z)
    {
        var spans=new List<Vector2>{new Vector2(left,right)};
        foreach(var door in doors.Where(d=>Mathf.Abs(d.z-z)<.001f && d.x-.65f>left && d.x+.65f<right))
        {
            Subtract(spans,door.x-.65f,door.x+.65f);
            Box(name+"_dintel_"+door.x,new Vector3(door.x,(6.74f+High)/2,z),new Vector3(1.3f,High-6.74f,Thickness));
        }
        foreach(var span in spans)if(span.y-span.x>.03f)Box(name,new Vector3((span.x+span.y)/2,(Low+High)/2,z),new Vector3(span.y-span.x,High-Low,Thickness));
    }
    private void WallZ(string name,float x,float front,float back)
    {if(back-front>.03f)Box(name,new Vector3(x,(Low+High)/2,(front+back)/2),new Vector3(Thickness,High-Low,back-front));}
    private void Box(string name,Vector3 center,Vector3 size)
    {
        var piece=GameObject.CreatePrimitive(PrimitiveType.Cube);piece.name=name;piece.layer=2;piece.transform.SetParent(transform,false);
        piece.transform.localPosition=center;piece.transform.localScale=size;piece.GetComponent<Renderer>().sharedMaterial=cement;
        var collider=piece.GetComponent<Collider>();collider.enabled=false;
        if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);Pieces.Add(piece);
    }
    private Material Material(string name,Color color)
    {var m=new Material(cement.shader){name=name,color=color};materials.Add(m);return m;}
    private GameObject Furnish(string name,Vector3 center,Vector3 size,Material material,Transform parent=null)
    {
        var piece=GameObject.CreatePrimitive(PrimitiveType.Cube);piece.name=name;piece.layer=2;piece.transform.SetParent(parent ?? transform,false);
        piece.transform.localPosition=center;piece.transform.localScale=size;piece.GetComponent<Renderer>().sharedMaterial=material;
        var collider=piece.GetComponent<Collider>();collider.enabled=false;if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);
        Furniture.Add(piece);return piece;
    }
    private void Door(Vector3 door)
    {
        var center=new Vector3(door.x,5.64f,door.z);DoorCenters.Add(center);
        foreach(float side in new[]{-1f,1f})Furnish("Marco_puerta_habitacion",center+Vector3.right*side*.69f,new Vector3(.08f,2.2f,.16f),wood);
        Furnish("Marco_superior_puerta_habitacion",center+Vector3.up*1.14f,new Vector3(1.46f,.08f,.16f),wood);
        Furnish("Puerta_habitacion_abierta",center+new Vector3(-.69f,0,door.y*.66f),new Vector3(.055f,2.12f,1.24f),wood);
        Furnish("Manilla_puerta_habitacion",center+new Vector3(-.63f,-.05f,door.y*1.12f),new Vector3(.12f,.04f,.08f),metal);
    }
    private void Table(string name,Vector3 position)
    {
        var root=new GameObject(name);root.layer=2;root.transform.SetParent(transform,false);root.transform.localPosition=position;Tables.Add(root.transform);
        Furnish("Tablero_mesa_habitacion",new Vector3(0,.76f,0),new Vector3(1.35f,.06f,.9f),wood,root.transform);
        foreach(float x in new[]{-.55f,.55f})foreach(float z in new[]{-.34f,.34f})
            Furnish("Pata_mesa_habitacion",new Vector3(x,.37f,z),new Vector3(.045f,.74f,.045f),metal,root.transform);
        foreach(float side in new[]{-1f,1f})Chair(position+new Vector3(0,0,side*.95f),side<0?0:180);
    }
    private void Chair(Vector3 position,float angle)
    {
        var root=new GameObject("Silla_habitacion");root.layer=2;root.transform.SetParent(transform,false);root.transform.localPosition=position;root.transform.localRotation=Quaternion.Euler(0,angle,0);
        Furnish("Asiento_silla_habitacion",new Vector3(0,.46f,0),new Vector3(.48f,.07f,.48f),fabric,root.transform);
        Furnish("Respaldo_silla_habitacion",new Vector3(0,.76f,-.22f),new Vector3(.48f,.58f,.055f),fabric,root.transform);
        foreach(float x in new[]{-.19f,.19f})foreach(float z in new[]{-.19f,.19f})
            Furnish("Pata_silla_habitacion",new Vector3(x,.22f,z),new Vector3(.035f,.44f,.035f),metal,root.transform);
    }
    private void OnDestroy(){foreach(var material in materials)if(material!=null){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}}
}
