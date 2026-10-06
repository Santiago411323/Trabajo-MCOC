using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Decorative room only. Navigation makes temporary collision proxies separately.
public sealed class VisualStudyRoom : MonoBehaviour
{
    private readonly List<Material> materials=new List<Material>();
    public readonly List<Transform> Tables=new List<Transform>();
    public Vector3 DoorCenter {get;private set;}
    private Material cement,wood,metal,screen;
    private Material Material(string name,Color color)
    {
        var m=new Material(Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit")){name=name,color=color};
        materials.Add(m);return m;
    }
    private GameObject Piece(string name,Vector3 position,Vector3 size,Material material,Transform parent=null)
    {
        var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.layer=2;
        obj.transform.SetParent(parent ?? transform,false);obj.transform.localPosition=position;obj.transform.localScale=size;
        obj.GetComponent<Renderer>().sharedMaterial=material;
        var collider=obj.GetComponent<Collider>();collider.enabled=false;
        if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);
        return obj;
    }
    public void Build(StructureData data)
    {
        var nodes=data.nodes.ToDictionary(n=>n.id,n=>new Vector3(n.x,n.z,n.y));
        System.Func<string,Vector3> point=tag=>nodes[data.elements.Single(e=>e.elementTag==tag).nodeI];
        Vector3 a=point("E1_243"),b=point("E1_247"),c=point("E1_255");
        var beam=data.elements.Single(e=>e.elementTag=="E1_38");
        Vector3 end=nodes[beam.nodeI].z>nodes[beam.nodeJ].z?nodes[beam.nodeI]:nodes[beam.nodeJ];
        float low=a.y+.44f,high=a.y+3.56f,floor=a.y+.54f;
        cement=Material("Cemento_sala_visual",new Color(.57f,.58f,.56f));
        wood=Material("Mesas_sala_visual",new Color(.64f,.46f,.28f));
        metal=Material("Laptops_sala",new Color(.19f,.21f,.23f));
        screen=Material("Pantallas_laptops",new Color(.16f,.28f,.37f));
        var image=Resources.Load<Texture2D>("StudyRoomLaptopScreen");
        if(image!=null)
        {
            screen.mainTexture=image;screen.color=Color.white;
            if(screen.HasProperty("_EmissionMap"))
            {
                screen.SetTexture("_EmissionMap",image);screen.SetColor("_EmissionColor",Color.white);
                screen.EnableKeyword("_EMISSION");
            }
        }
        Piece("Piso_visual_sala",new Vector3((a.x+c.x)/2,floor-.025f,(a.z+b.z)/2),new Vector3(c.x-a.x-.12f,.05f,b.z-a.z-.12f),cement);
        Piece("Muro_visual_E1_243_E1_247",new Vector3(a.x,(low+high)/2,(a.z+b.z)/2),new Vector3(.12f,high-low,b.z-a.z-.7f),cement);
        // E1_255 needs its column inset; the E1_38/E1_9 corner is a continuous wall joint.
        float sideStart=c.z+.35f;
        Piece("Muro_visual_E1_38_E1_255",new Vector3(c.x,(low+high)/2,(sideStart+end.z)/2),new Vector3(.12f,high-low,end.z-sideStart),cement);
        // Door lies over E1_8, with a 1.30 x 2.20 m clear opening above the walking finish.
        var doorBeam=data.elements.Single(e=>e.elementTag=="E1_8");
        float doorX=(nodes[doorBeam.nodeI].x+nodes[doorBeam.nodeJ].x)/2;
        float left=b.x+.35f,right=end.x;
        DoorCenter=new Vector3(doorX,floor+1.1f,b.z);
        Piece("Muro_visual_E1_8_izquierda",new Vector3((left+doorX-.65f)/2,(low+high)/2,b.z),new Vector3(doorX-.65f-left,high-low,.12f),cement);
        Piece("Muro_visual_E1_8_derecha_hasta_E1_38",new Vector3((doorX+.65f+right)/2,(low+high)/2,b.z),new Vector3(right-doorX-.65f,high-low,.12f),cement);
        Piece("Dintel_puerta_sala_E1_8",new Vector3(doorX,(floor+2.2f+high)/2,b.z),new Vector3(1.3f,high-floor-2.2f,.12f),cement);
        foreach(float side in new[]{-1f,1f})
            Piece("Marco_puerta_sala_computadores",DoorCenter+Vector3.right*side*.69f,new Vector3(.08f,2.2f,.16f),wood);
        Piece("Marco_superior_puerta_sala_computadores",DoorCenter+Vector3.up*1.14f,new Vector3(1.46f,.08f,.16f),wood);
        // Open toward the corridor so the leaf does not collide with the existing tables/chairs.
        Piece("Puerta_sala_computadores_abierta",DoorCenter+new Vector3(-.69f,0,.66f),new Vector3(.055f,2.12f,1.24f),wood);
        Piece("Manilla_puerta_sala_computadores",DoorCenter+new Vector3(-.63f,-.05f,1.12f),new Vector3(.12f,.04f,.08f),metal);
        for(int row=0;row<2;row++)for(int col=0;col<2;col++)
        {
            Vector3 p=new Vector3(Mathf.Lerp(a.x,c.x,.25f+.5f*col),floor,Mathf.Lerp(a.z,b.z,.25f+.5f*row));
            var table=new GameObject("Mesa_sala_"+(row*2+col+1));table.layer=2;table.transform.SetParent(transform,false);table.transform.localPosition=p;
            Tables.Add(table.transform);
            Piece("Tablero_mesa_sala",new Vector3(0,.76f,0),new Vector3(1.35f,.06f,.9f),wood,table.transform);
            foreach(float x in new[]{-.55f,.55f})foreach(float z in new[]{-.34f,.34f})
                Piece("Pata_mesa_sala",new Vector3(x,.37f,z),new Vector3(.045f,.74f,.045f),metal,table.transform);
            var laptop=new GameObject("Laptop_sala");laptop.layer=2;laptop.transform.SetParent(table.transform,false);laptop.transform.localPosition=new Vector3(0,.805f,0);
            Piece("Base_laptop",Vector3.zero,new Vector3(.36f,.025f,.26f),metal,laptop.transform);
            Piece("Teclado_laptop",new Vector3(0,.015f,.015f),new Vector3(.30f,.008f,.15f),cement,laptop.transform);
            Piece("Touchpad_laptop",new Vector3(0,.017f,-.085f),new Vector3(.10f,.007f,.045f),metal,laptop.transform);
            var display=Piece("Pantalla_laptop",new Vector3(0,.125f,.12f),new Vector3(.36f,.23f,.022f),metal,laptop.transform);
            display.transform.localRotation=Quaternion.Euler(12,0,0);
            // Quad avoids mirroring the texture across the rear of a cube.
            var picture=GameObject.CreatePrimitive(PrimitiveType.Quad);picture.name="Imagen_laptop";picture.layer=2;picture.transform.SetParent(laptop.transform,false);
            picture.transform.localPosition=new Vector3(0,.125f,.105f);picture.transform.localRotation=display.transform.localRotation;
            picture.transform.localScale=new Vector3(.325f,.195f,1);picture.GetComponent<Renderer>().sharedMaterial=screen;
            var collider=picture.GetComponent<Collider>();collider.enabled=false;if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);
        }
    }
    private void OnDestroy(){foreach(var m in materials)if(m!=null){if(Application.isPlaying)Destroy(m);else DestroyImmediate(m);}}
}
