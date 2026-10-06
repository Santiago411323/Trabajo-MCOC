using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Approximate architectural context based on the supplied aerial references.
// Decorative only: no colliders, FE elements, masses or structural coordinates.
public sealed class VisualCampusSite : MonoBehaviour
{
    private const float PitchLength=105f,PitchWidth=68f;
    private readonly List<Material> materials=new List<Material>();
    private readonly List<Mesh> meshes=new List<Mesh>();
    private Vector2[] gradeLevels;
    private float frontZ;
    private float parkingLift;
    private Material sand,concrete,asphalt,white,grass,bark,leaves,glass;
    private Material Mat(string name,Color color)
    {
        var m=new Material(Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit")){name=name,color=color};
        materials.Add(m);return m;
    }
    private GameObject Piece(string name,Vector3 position,Vector3 size,Material material,PrimitiveType type=PrimitiveType.Cube)
    {
        var p=GameObject.CreatePrimitive(type);p.name=name;p.layer=2;p.transform.SetParent(transform,false);
        p.transform.localPosition=position;p.transform.localScale=size;p.GetComponent<Renderer>().sharedMaterial=material;
        var c=p.GetComponent<Collider>();c.enabled=false;if(Application.isPlaying)Destroy(c);else DestroyImmediate(c);
        return p;
    }
    public void Build(StructureData data,float clearance,float upperTerraceElevation)
    {
        var nodes=data.nodes.ToDictionary(n=>n.id,n=>new Vector3(n.x,n.z,n.y));
        var supports=(data.supports ?? new SupportData[0]).Where(s=>nodes.ContainsKey(s.node)).Select(s=>nodes[s.node]).ToArray();
        if(supports.Length==0)return;
        float xmin=supports.Min(p=>p.x),xmax=supports.Max(p=>p.x),zmin=supports.Min(p=>p.z),zmax=supports.Max(p=>p.z);
        var levels=supports.GroupBy(p=>Mathf.Round(p.y*100)/100).Select(g=>new Vector2(g.Average(p=>p.x),g.Key-clearance)).OrderBy(p=>p.x).ToArray();
        gradeLevels=levels;frontZ=zmin;
        parkingLift=upperTerraceElevation-levels[levels.Length-1].y;
        sand=Mat("Explanada_arena",new Color(.60f,.52f,.37f));concrete=Mat("Senderos_hormigon",new Color(.68f,.67f,.61f));
        asphalt=Mat("Calzada",new Color(.23f,.25f,.25f));white=Mat("Demarcacion",new Color(.88f,.87f,.78f));
        grass=Mat("Cancha_pasto",new Color(.23f,.39f,.17f));bark=Mat("Troncos",new Color(.28f,.23f,.16f));
        leaves=Mat("Arboles",new Color(.24f,.34f,.16f));glass=Mat("Vidrio_autos",new Color(.16f,.24f,.27f));
        float ground=levels.Min(p=>p.y)-.6f;
        VisualCafe.TryLayout(data,out var cafe);
        float fieldY=cafe!=null?cafe.Floor-.3f:ground+.5f;
        float fieldX=cafe!=null?(cafe.RearLeft+cafe.RearRight)/2:(xmin+xmax)/2;
        // The near goal faces the terrace; the pitch extends away along +Z.
        float fieldNear=cafe!=null?cafe.PatioRear+3:zmax+12;
        float fieldZ=fieldNear+PitchLength/2;
        System.Func<float,float,float> landscape=(x,z)=>
        {
            float h=Grade(x,z);
            if(x>=xmin-2 && x<=xmax+2 && z>=zmin-12 && z<=zmax+12)h=ground;
            h=VisualCafe.CutHeight(cafe,x,z,h,xmax+2);
            // Level playing surface, blended back into the common hillside without a wall.
            float edge=Mathf.Max(Mathf.Abs(x-fieldX)-(PitchWidth/2+5),Mathf.Abs(z-fieldZ)-(PitchLength/2+5));
            float blend=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(edge/25));
            h=Mathf.Lerp(h,fieldY-.2f,blend);
            if(x>=fieldX+PitchWidth/2+3 && x<=fieldX+PitchWidth/2+15 && Mathf.Abs(z-fieldZ)<25)
                h=fieldY+.4f*Mathf.Clamp(x-(fieldX+PitchWidth/2+3),0,9)-.12f;
            return h;
        };
        Surface("Ladera_continua_hasta_pies_cerros",xmin-320,xmax+320,zmin-320,zmax+320,landscape,sand,3);
        // Grade each strip to its adjacent foundation level, including the uphill building.
        for(int k=0;k<levels.Length;k++)
        {
            float left=k==0?xmin-24:(levels[k-1].x+levels[k].x)*.5f;
            float right=k==levels.Length-1?xmax+24:(levels[k].x+levels[k+1].x)*.5f;
            Surface("Estacionamiento_en_pendiente",left,right,zmin-65,zmin-15,(x,z)=>Grade(x,z)+.025f,sand,1.5f);
            Surface("Vereda_en_pendiente",left,right,zmin-16.3f,zmin-13.3f,(x,z)=>Grade(x,z)+.06f,concrete,1.5f);
            Surface("Calle_en_pendiente",left,right,zmin-69,zmin-61,(x,z)=>Grade(x,z)+.04f,asphalt,1.5f);
            for(float x=left+2;x<right-2;x+=6)OnGrade("Eje_calzada",x,zmin-65,.065f,new Vector3(3,.025f,.12f),white);
            for(int row=0;row<3;row++)for(float x=left+4;x<right-3;x+=5.6f)
            {
                float z=zmin-24-row*14;
                OnGrade("Linea_estacionamiento",x,z,.055f,new Vector3(.07f,.025f,5.2f),white);
                if(((int)(x-left)+row)%3==0)continue;
                float cx=x+1.4f;
                Material car=Mat("Auto",Color.Lerp(new Color(.82f,.80f,.73f),new Color(.29f,.35f,.42f),Mathf.Repeat(x*.17f+row*.27f,1)));
                OnGrade("Auto_carroceria",cx,z,.55f,new Vector3(1.65f,.8f,3.7f),car);
                OnGrade("Auto_cabina",cx,z,1.05f,new Vector3(1.45f,.6f,1.85f),glass);
            }
            for(float x=left+3;x<right-3;x+=9)Tree(new Vector3(x,Grade(x,zmin-14),zmin-14),3.3f+Mathf.Repeat(x,1.4f));
        }
        // Sports ground to the side, set on its adjacent terrace, away from the FE footprint.
        Piece("Base_cancha",new Vector3(fieldX,(fieldY+ground)/2,fieldZ),new Vector3(PitchWidth+10,fieldY-ground,PitchLength+10),sand);
        Piece("Cancha_105x68",new Vector3(fieldX,fieldY+.025f,fieldZ),new Vector3(PitchWidth,.06f,PitchLength),grass);
        foreach(float z in new[]{fieldNear,fieldNear+PitchLength,fieldZ})Piece("Linea_fondo_y_media",new Vector3(fieldX,fieldY+.07f,z),new Vector3(PitchWidth,.02f,.10f),white);
        foreach(float x in new[]{fieldX-PitchWidth/2,fieldX+PitchWidth/2})Piece("Linea_lateral",new Vector3(x,fieldY+.07f,fieldZ),new Vector3(.10f,.02f,PitchLength),white);
        foreach(float z in new[]{fieldNear,fieldNear+PitchLength})
        {
            float back=z+Mathf.Sign(z-fieldZ)*2.2f;
            foreach(float x in new[]{fieldX-3.66f,fieldX+3.66f})
            {
                Piece("Poste_arco",new Vector3(x,fieldY+1.22f,z),new Vector3(.12f,2.44f,.12f),white);
                Piece("Soporte_red",new Vector3(x,fieldY+.8f,back),new Vector3(.06f,1.6f,.06f),white);
            }
            Piece("Travesaño_arco",new Vector3(fieldX,fieldY+2.44f,z),new Vector3(7.32f,.12f,.12f),white);
        }
        // Centre circle and penalty boxes improve the sports-ground reference.
        for(int i=0;i<48;i++)
        {
            float a=i*Mathf.PI*2/48,b=(i+1)*Mathf.PI*2/48;
            Vector3 p=new Vector3(fieldX+Mathf.Cos(a)*9.15f,fieldY+.08f,fieldZ+Mathf.Sin(a)*9.15f),q=new Vector3(fieldX+Mathf.Cos(b)*9.15f,fieldY+.08f,fieldZ+Mathf.Sin(b)*9.15f);
            var line=Piece("Circulo_central",(p+q)/2,new Vector3(.08f,.02f,(q-p).magnitude),white);line.transform.localRotation=Quaternion.LookRotation(q-p);
        }
        foreach(float side in new[]{-1f,1f})
        {
            float goalZ=fieldZ+side*PitchLength/2;
            foreach(float depth in new[]{16.5f,5.5f})
            {
                float areaWidth=7.32f+2*depth;
                Piece("Frente_area",new Vector3(fieldX,fieldY+.08f,goalZ-side*depth),new Vector3(areaWidth,.02f,.08f),white);
                foreach(float x in new[]{fieldX-areaWidth/2,fieldX+areaWidth/2})Piece("Lado_area",new Vector3(x,fieldY+.08f,goalZ-side*depth/2),new Vector3(.08f,.02f,depth),white);
            }
            Piece("Punto_penal",new Vector3(fieldX,fieldY+.08f,goalZ-side*11),new Vector3(.22f,.01f,.22f),white,PrimitiveType.Cylinder);
        }
        for(int t=0;t<12;t++)Tree(new Vector3(fieldX+PitchWidth/2+23,landscape(fieldX+PitchWidth/2+23,fieldNear+5+t*8),fieldNear+5+t*8),4);
        var match=new GameObject("Partido_y_barra_ambientacion");match.layer=2;match.transform.SetParent(transform,false);
        match.AddComponent<VisualFootballCrowd>().Build(fieldX,fieldY+.06f,fieldZ,PitchWidth,PitchLength);
        Hills(new Vector2((xmin+xmax)/2,(zmin+zmax)/2));
    }
    private float Grade(float x,float z)
    {
        var low=gradeLevels[0];var high=gradeLevels[gradeLevels.Length-1];
        float slope=gradeLevels.Length>1 && Mathf.Abs(high.x-low.x)>.01f ? (high.y-low.y)/(high.x-low.x) : .035f;
        return high.y+parkingLift+slope*(x-high.x)-.018f*(z-(frontZ-15));
    }
    private void OnGrade(string name,float x,float z,float offset,Vector3 size,Material mat)
    {
        Vector3 normal=new Vector3(-(Grade(x+.2f,z)-Grade(x-.2f,z))/.4f,1,-(Grade(x,z+.2f)-Grade(x,z-.2f))/.4f).normalized;
        var obj=Piece(name,new Vector3(x,Grade(x,z),z)+normal*offset,size,mat);
        obj.transform.localRotation=Quaternion.FromToRotation(Vector3.up,normal);
    }
    private void Surface(string name,float xmin,float xmax,float zmin,float zmax,System.Func<float,float,float> height,Material mat,float spacing)
    {
        int nx=Mathf.CeilToInt((xmax-xmin)/spacing),nz=Mathf.CeilToInt((zmax-zmin)/spacing);
        var vertices=new Vector3[(nx+1)*(nz+1)];var uv=new Vector2[vertices.Length];var triangles=new int[nx*nz*6];
        for(int j=0;j<=nz;j++)for(int i=0;i<=nx;i++)
        {float x=Mathf.Lerp(xmin,xmax,i/(float)nx),z=Mathf.Lerp(zmin,zmax,j/(float)nz);int k=j*(nx+1)+i;vertices[k]=new Vector3(x,height(x,z),z);uv[k]=new Vector2(x,z)*.1f;}
        int n=0;for(int j=0;j<nz;j++)for(int i=0;i<nx;i++)
        {int a=j*(nx+1)+i,b=a+nx+1;triangles[n++]=a;triangles[n++]=b;triangles[n++]=a+1;triangles[n++]=a+1;triangles[n++]=b;triangles[n++]=b+1;}
        var mesh=new Mesh{name=name};mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();meshes.Add(mesh);
        var obj=new GameObject(name);obj.layer=2;obj.transform.SetParent(transform,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=mat;
    }
    private void Hills(Vector2 center)
    {
        var mat=Mat("Cerros_distantes",new Color(.27f,.34f,.24f));
        for(int h=0;h<14;h++)
        {
            float angle=h*Mathf.PI*2/14;Vector2 peak=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(240+25*Mathf.Sin(h*2.3f));
            float radius=75+15*Mathf.Sin(h),altitude=45+22*(.5f+.5f*Mathf.Sin(h*1.7f));
            Surface("Cerro_panorama_"+h,peak.x-radius,peak.x+radius,peak.y-radius,peak.y+radius,(x,z)=>
            {
                float r=new Vector2(x-peak.x,z-peak.y).magnitude/radius;
                float profile=Mathf.Max(0,1-r*r);
                return Grade(x,z)-.03f+altitude*profile*profile*(.8f+.2f*Mathf.PerlinNoise(x*.026f,z*.026f));
            },mat,5);
        }
    }
    private void Tree(Vector3 p,float height)
    {
        Piece("Tronco",p+Vector3.up*height*.4f,new Vector3(.32f,height*.4f,.32f),bark,PrimitiveType.Cylinder);
        Piece("Copa",p+Vector3.up*height*.85f,new Vector3(height*.85f,height*.9f,height*.8f),leaves,PrimitiveType.Sphere);
    }
    private void OnDestroy(){foreach(Object item in materials.Cast<Object>().Concat(meshes))if(item!=null){if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);}}
}
