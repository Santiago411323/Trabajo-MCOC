using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Decorative cafeteria. Dimensions are bounded by E1_24 and the nearest lift wall.
public sealed class VisualCafe : MonoBehaviour
{
    public sealed class Layout
    {
        public float Left,Right,Front,Back;
        public float Floor=-3.97f;
        public float PatioFront=>Front-7.6f;
        public float Rear,RearLeft,RearRight;
        public float PatioRear=>Rear+9f;
    }
    private readonly List<Material> materials=new List<Material>();
    private readonly List<Mesh> meshes=new List<Mesh>();
    private Material floor,wood,metal,cream,green,orange,fabric;
    public static bool TryLayout(StructureData data,out Layout layout)
    {
        layout=null;
        var beam=data.elements.FirstOrDefault(e=>e.elementTag=="E1_24");if(beam==null)return false;
        var nodes=data.nodes.ToDictionary(n=>n.id,n=>new Vector3(n.x,n.z,n.y));
        var a=nodes[beam.nodeI];var b=nodes[beam.nodeJ];var center=(a+b)/2;
        var walls=(data.walls ?? new WallData[0]).Where(w=>nodes.ContainsKey(w.nodeI)&&nodes.ContainsKey(w.nodeJ))
            .SelectMany(w=>new[]{nodes[w.nodeI],nodes[w.nodeJ]})
            .Where(p=>p.z>center.z+.5f && p.x<center.x).OrderBy(p=>(new Vector2(p.x-center.x,p.z-center.z)).sqrMagnitude).ToList();
        if(walls.Count==0)return false;
        var boundary=walls[0];
        var main=data.elements.Where(e=>(e.elementTag ?? "").StartsWith("E1_") && e.type=="columna")
            .SelectMany(e=>new[]{nodes[e.nodeI],nodes[e.nodeJ]}).ToList();
        layout=new Layout{Left=Mathf.Min(a.x,b.x,boundary.x)+.18f,Right=Mathf.Max(a.x,b.x)-.18f,
            Front=center.z+.12f,Back=boundary.z-.2f,
            Rear=main.Count>0?main.Max(p=>p.z):8.9f,RearLeft=main.Count>0?main.Min(p=>p.x): -10,
            RearRight=main.Count>0?main.Where(p=>p.x<=Mathf.Max(a.x,b.x)+13).Select(p=>p.x).DefaultIfEmpty(20).Max():20};return true;
    }
    public static float CutHeight(Layout l,float x,float z,float height,float stairStartX)
    {
        if(l==null)return height;
        bool cafe=x>=l.Left-.4f && x<=l.Right+1.3f && z>=l.PatioFront-.3f && z<=l.Back+.3f;
        bool stair=x>=l.Right && x<=stairStartX+1 && Mathf.Abs(z-(l.Front-3.3f))<3f;
        bool rear=x>=l.RearLeft-.4f && x<=l.RearRight+.4f && z>=l.Back && z<=l.PatioRear+2;
        return cafe || stair || rear ? Mathf.Min(height,l.Floor-.22f) : height;
    }
    private Material Mat(string name,Color color)
    {var m=new Material(Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit")){name=name,color=color};materials.Add(m);return m;}
    private GameObject Piece(string name,Vector3 position,Vector3 size,Material material,PrimitiveType type=PrimitiveType.Cube)
    {
        var p=GameObject.CreatePrimitive(type);p.name=name;p.layer=2;p.transform.SetParent(transform,false);
        p.transform.localPosition=position;p.transform.localScale=size;p.GetComponent<Renderer>().sharedMaterial=material;
        var c=p.GetComponent<Collider>();c.enabled=false;if(Application.isPlaying)Destroy(c);else DestroyImmediate(c);return p;
    }
    public void Build(StructureData data,float terraceX,System.Action<GameObject,string> registerStair)
    {
        if(!TryLayout(data,out var l))return;
        floor=Mat("Cafe_pavimento",new Color(.60f,.57f,.50f));wood=Mat("Cafe_madera",new Color(.45f,.25f,.13f));
        metal=Mat("Cafe_acero",new Color(.17f,.20f,.22f));cream=Mat("Cafe_quitasoles",new Color(.92f,.85f,.67f));green=Mat("Cafe_plantas",new Color(.20f,.35f,.18f));
        orange=Mat("Fascia_naranja_cafeteria",new Color(.78f,.24f,.09f));fabric=Mat("Tela_quitasoles_gris",new Color(.42f,.46f,.49f));
        float y=l.Floor,w=l.Right-l.Left,depth=l.Back-l.Front;
        Piece("Piso_cafeteria_nivel_menos4",new Vector3((l.Left+l.Right)/2,y-.1f,(l.Front+l.Back)/2),new Vector3(w,.2f,depth),floor);
        Piece("Terraza_cafeteria_nivel_menos4",new Vector3((l.Left+l.Right+1)/2,y-.12f,(l.PatioFront+l.Front)/2),new Vector3(w+1,.24f,l.Front-l.PatioFront),floor);
        Piece("Borde_terraza",new Vector3((l.Left+l.Right+1)/2,y-.3f,l.PatioFront),new Vector3(w+1,.55f,.12f),orange);
        // Ceiling stays under the floor above; furniture remains below the real beam.
        Piece("Cielo_cafeteria",new Vector3((l.Left+l.Right)/2,-.58f,(l.Front+l.Back)/2),new Vector3(w,.10f,depth),cream);
        Piece("Muro_fondo_cafe",new Vector3(l.Left+(w-2.2f)/2,y+1.55f,l.Back),new Vector3(w-2.2f,3.1f,.12f),cream);
        Piece("Dintel_paso_cafe",new Vector3(l.Right-1.1f,y+2.85f,l.Back),new Vector3(2.2f,.5f,.12f),cream);
        float barX=l.Left+1.5f,barZ=l.Back-1.1f;
        Piece("Barra_servicio",new Vector3(barX,y+.53f,barZ),new Vector3(2.6f,1.06f,.8f),wood);
        Piece("Meson",new Vector3(barX,y+1.1f,barZ),new Vector3(2.8f,.12f,.9f),metal);
        Piece("Maquina_espresso",new Vector3(barX-.45f,y+1.4f,barZ),new Vector3(.9f,.5f,.55f),metal);
        foreach(float dx in new[]{-.7f,-.35f})
        {
            Piece("Grupo_cafe",new Vector3(barX+dx,y+1.24f,barZ-.34f),new Vector3(.15f,.18f,.12f),cream);
            Piece("Taza",new Vector3(barX+dx,y+1.18f,barZ-.42f),new Vector3(.09f,.04f,.09f),cream,PrimitiveType.Cylinder);
        }
        Piece("Molinillo",new Vector3(barX+.6f,y+1.45f,barZ),new Vector3(.28f,.55f,.3f),metal);
        Piece("Tolva_molinillo",new Vector3(barX+.6f,y+1.79f,barZ),new Vector3(.23f,.14f,.23f),cream,PrimitiveType.Cylinder);
        Piece("Vitrina_pasteleria",new Vector3(barX+1,y+1.33f,barZ-.10f),new Vector3(.60f,.34f,.48f),cream);
        Piece("Refrigerador",new Vector3(l.Right-.6f,y+1,l.Back-.45f),new Vector3(.75f,2,.65f),metal);
        RearTerrace(l);
        for(int r=0;r<2;r++)for(int c=0;c<3;c++)Table(new Vector3(l.Left+1.25f+c*2.75f,y,l.Front+1.4f+r*1.8f),false);
        for(int r=0;r<2;r++)for(int c=0;c<3;c++)Table(new Vector3(l.Left+1.4f+c*3.1f,y,l.Front-2.1f-r*3.2f),true);
        foreach(float x in new[]{l.Left+.35f,l.Right-.35f})
        {
            Piece("Macetero",new Vector3(x,y+.3f,l.Front-.7f),new Vector3(.55f,.6f,.55f),wood);
            Piece("Planta",new Vector3(x,y+.85f,l.Front-.7f),new Vector3(.65f,.75f,.65f),green,PrimitiveType.Sphere);
        }
        var sign=new GameObject("Rotulo_cafeteria");sign.layer=2;sign.transform.SetParent(transform,false);
        sign.transform.localPosition=new Vector3((l.Left+l.Right)/2,y+2.65f,l.Back-.08f);
        var text=sign.AddComponent<TextMesh>();text.text="CAFETERÍA";text.anchor=TextAnchor.MiddleCenter;text.fontSize=48;text.characterSize=.075f;text.color=new Color(.22f,.18f,.13f);
        // Start at the existing E1_220 -> terrace Y4 landing. Extend its flat approach outside the facade.
        var nodes=data.nodes.ToDictionary(n=>n.id,n=>new Vector3(n.x,n.z,n.y));
        var exit=data.elements.FirstOrDefault(e=>e.elementTag=="E1_220");if(exit==null)return;
        float z=((nodes[exit.nodeI]+nodes[exit.nodeJ])/2).z;
        float startX=terraceX+.4f,routeZ=l.Front-3.3f,width=5f;
        StairPiece("Plano_conexion_escalera_existente",new Vector3(startX,4.03f-.1f,(z+routeZ)/2),new Vector3(width,.2f,Mathf.Abs(z-routeZ)+width),registerStair);
        float endX=l.Right+.5f,run=startX-endX,drop=4.03f-y;if(run<1 || drop<=0)return;
        int count=Mathf.CeilToInt(drop/.18f);float tread=run/count;
        for(int i=0;i<count;i++)
        {
            float top=4.03f-drop*(i+1)/count;
            StairPiece("Bajada_ancha_cafeteria_peldaño_"+(i+1),new Vector3(startX-(i+.5f)*tread,(top+y-.22f)/2,routeZ),
                new Vector3(tread+.01f,top-y+.22f,width),registerStair);
        }
        StairPiece("Descanso_terraza_cafeteria",new Vector3(endX,y-.1f,routeZ),new Vector3(1.4f,.2f,width),registerStair);
        foreach(float side in new[]{-width/2,width/2})
        {
            var fascia=MeshPiece("Fascia_diagonal_bajada",new[]{new Vector3(startX,4.73f,routeZ+side),new Vector3(endX,y+.7f,routeZ+side),
                new Vector3(startX,3.63f,routeZ+side),new Vector3(endX,y-.4f,routeZ+side)},new[]{0,2,1,1,2,3,0,1,2,1,3,2},orange);
            registerStair(fascia,"FOUNDATION");
            Vector3 a=new Vector3(startX,5.03f,routeZ+side),b=new Vector3(endX,y+1,routeZ+side);
            var rail=StairPiece("Pasamanos_bajada",(a+b)/2,new Vector3(.07f,(b-a).magnitude,.07f),registerStair);
            rail.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);
            for(int k=0;k<=6;k++)
            {var p=Vector3.Lerp(a,b,k/6f);StairPiece("Poste_baranda",p-Vector3.up*.5f,new Vector3(.06f,1,.06f),registerStair);}
        }
    }
    private GameObject StairPiece(string name,Vector3 p,Vector3 size,System.Action<GameObject,string> register)
    {var piece=Piece(name,p,size,floor);register(piece,"FOUNDATION");return piece;}
    private void Table(Vector3 p,bool umbrella)
    {
        Piece("Mesa",p+Vector3.up*.76f,new Vector3(1.15f,.045f,1.15f),wood,PrimitiveType.Cylinder);
        Piece("Pie_mesa",p+Vector3.up*.36f,new Vector3(.13f,.36f,.13f),metal,PrimitiveType.Cylinder);
        foreach(Vector3 offset in new[]{new Vector3(.85f,0,0),new Vector3(-.85f,0,0),new Vector3(0,0,.85f),new Vector3(0,0,-.85f)})
        {
            Piece("Silla_asiento",p+offset+Vector3.up*.44f,new Vector3(.42f,.08f,.42f),wood);
            foreach(float dx in new[]{-.15f,.15f})foreach(float dz in new[]{-.15f,.15f})
                Piece("Pata_silla",p+offset+new Vector3(dx,.2f,dz),new Vector3(.04f,.4f,.04f),metal);
            var back=p+offset*1.2f+Vector3.up*.68f;
            Piece("Silla_respaldo",back,Mathf.Abs(offset.x)>.1f?new Vector3(.08f,.5f,.42f):new Vector3(.42f,.5f,.08f),wood);
        }
        if(!umbrella)return;
        Piece("Poste_quitasol",p+Vector3.up*1.35f,new Vector3(.06f,1.35f,.06f),metal,PrimitiveType.Cylinder);
        MeshPiece("Quitasol_cuadrado",new[]{p+new Vector3(-1.3f,2.5f,-1.3f),p+new Vector3(1.3f,2.5f,-1.3f),
            p+new Vector3(1.3f,2.5f,1.3f),p+new Vector3(-1.3f,2.5f,1.3f),p+Vector3.up*2.85f},
            new[]{0,4,1,1,4,2,2,4,3,3,4,0,0,1,4,1,2,4,2,3,4,3,0,4},fabric);
    }
    private GameObject MeshPiece(string name,Vector3[] vertices,int[] triangles,Material mat)
    {
        var mesh=new Mesh{name=name,vertices=vertices,triangles=triangles};mesh.RecalculateNormals();meshes.Add(mesh);
        var p=new GameObject(name);p.layer=2;p.transform.SetParent(transform,false);p.AddComponent<MeshFilter>().sharedMesh=mesh;p.AddComponent<MeshRenderer>().sharedMaterial=mat;return p;
    }
    private void RearTerrace(Layout l)
    {
        float y=l.Floor,width=l.RearRight-l.RearLeft;
        // A circulation/gallery strip joins both cafe outlets on the same confirmed level.
        Piece("Galeria_cafeteria_hacia_fachada_opuesta",new Vector3((l.Left+l.Right)/2,y-.10f,(l.Back+l.Rear)/2),
            new Vector3(l.Right-l.Left,.2f,l.Rear-l.Back),floor);
        Piece("Cielo_galeria_cafe",new Vector3((l.Left+l.Right)/2,-.58f,(l.Back+l.Rear)/2),new Vector3(l.Right-l.Left,.1f,l.Rear-l.Back),cream);
        Piece("Terraza_cafeteria_fachada_cancha",new Vector3((l.RearLeft+l.RearRight)/2,y-.12f,(l.Rear+l.PatioRear)/2),new Vector3(width,.24f,9),floor);
        Piece("Fascia_naranja_terraza_posterior",new Vector3((l.RearLeft+l.RearRight)/2,y-.36f,l.PatioRear),new Vector3(width,.7f,.14f),orange);
        // Paving joints, metal guardrail and planted edge follow the terrace outline.
        for(float x=l.RearLeft+.6f;x<l.RearRight;x+=1.2f)
            Piece("Junta_pavimento",new Vector3(x,y+.012f,(l.Rear+l.PatioRear)/2),new Vector3(.012f,.015f,9),metal);
        for(float z=l.Rear+.6f;z<l.PatioRear;z+=1.2f)
            Piece("Junta_transversal",new Vector3((l.RearLeft+l.RearRight)/2,y+.012f,z),new Vector3(width,.015f,.012f),metal);
        Piece("Pasamanos_terraza_posterior",new Vector3((l.RearLeft+l.RearRight)/2,y+1.05f,l.PatioRear-.15f),new Vector3(width,.065f,.065f),metal);
        for(float x=l.RearLeft;x<=l.RearRight;x+=1.5f)
        {
            Piece("Poste_terraza",new Vector3(x,y+.51f,l.PatioRear-.15f),new Vector3(.055f,1.02f,.055f),metal);
            Piece("Barra_guardacuerpo",new Vector3(x,y+.5f,l.PatioRear-.15f),new Vector3(.025f,.95f,.025f),metal);
        }
        int columns=Mathf.Max(1,Mathf.FloorToInt((width-3)/3.5f));
        for(int r=0;r<2;r++)for(int c=0;c<columns;c++)Table(new Vector3(l.RearLeft+1.8f+c*3.5f,y,l.Rear+2+r*3.5f),true);
        for(int r=0;r<3;r++)Table(new Vector3(l.Right-1.8f,y,l.Back+2+r*3.4f),false);
        foreach(float x in new[]{l.RearLeft+.5f,l.RearRight-.5f})
        {
            Piece("Jardinera_posterior",new Vector3(x,y+.35f,l.PatioRear-.9f),new Vector3(.7f,.7f,1.3f),cream);
            Piece("Vegetacion_jardinera",new Vector3(x,y+.85f,l.PatioRear-.9f),new Vector3(.8f,.65f,1.3f),green,PrimitiveType.Sphere);
        }
        // Exterior access at the end of the veranda: five broad shallow steps.
        for(int step=0;step<5;step++)Piece("Acceso_terraza_posterior_"+step,
            new Vector3(l.RearLeft-.3f-step*.3f,y-.08f*(step+1)-.1f,l.Rear+2),new Vector3(.32f,.2f,2.2f),floor);
    }
    private void OnDestroy(){foreach(Object item in materials.Cast<Object>().Concat(meshes))if(item!=null){if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);}}
}
