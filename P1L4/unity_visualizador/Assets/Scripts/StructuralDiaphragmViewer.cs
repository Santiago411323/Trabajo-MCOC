using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Independent postprocessor. Never adds constraints, changes loads or moves the structural model.
public class StructuralDiaphragmViewer : MonoBehaviour
{
    private sealed class Group
    {
        public RigidDiaphragmRecord data;
        public string issue, levels, buildings;
        public GameObject root;
        public Mesh points, links, original;
        public Vector3[] pointVertices, linkVertices, ghostVertices;
        public DiaphragmSelectable master;
        public Transform[] targets;
        public DiaphragmKinematics.Motion motion;
        public Color color;
    }
    private readonly List<Group> groups=new List<Group>();
    private readonly Dictionary<int,NodeData> nodes=new Dictionary<int,NodeData>();
    private readonly Dictionary<int,HashSet<string>> nodeBuildings=new Dictionary<int,HashSet<string>>();
    private StructureData model;
    private StructureViewer owner;
    private DiagramController diagrams;
    private Material material;
    private GameObject visualRoot;
    private bool open, all=true, links=true, original=true;
    private int focused;
    private float nextRefresh;
    private GUIStyle wrap;
    public float ContentHeight => open ? 216f + Mathf.Ceil(groups.Count/3f)*28f : 36f;

    public void Initialize(StructureData data, StructureViewer source)
    {
        Clear(); owner=source; model=data; diagrams=source.GetComponent<DiagramController>();
        foreach(var n in data.nodes??Array.Empty<NodeData>())nodes[n.id]=n;
        foreach(var e in data.elements??Array.Empty<ElementData>())
            foreach(int id in new[]{e.nodeI,e.nodeJ})
            {
                if(!nodeBuildings.TryGetValue(id,out var set))nodeBuildings[id]=set=new HashSet<string>();
                if(!string.IsNullOrEmpty(e.sourceBuilding))set.Add(e.sourceBuilding);
            }
        visualRoot=new GameObject("Diafragmas_OpenSees_Restricciones"); visualRoot.transform.SetParent(source.transform,false);
        var shader=Resources.Load<Shader>("DiaphragmOverlay");
        material=new Material(shader!=null?shader:Shader.Find("Sprites/Default"));
        material.name="Diafragmas_colores_por_cota";
        var records=data.p1l4?.analysisModel?.diafragmas;
        if(records!=null)for(int i=0;i<records.Length;i++)Build(records[i],i);
        focused=Mathf.Clamp(focused,0,Mathf.Max(0,groups.Count-1));
        RefreshMotion(); UpdateVisibility();
    }

    private void Build(RigidDiaphragmRecord data,int index)
    {
        var group=new Group {data=data,color=Color.HSVToRGB((.49f+index*.137f)%1f,.66f,1f),
            issue=DiaphragmKinematics.Validate(data,nodes)};
        groups.Add(group);
        var levels=new HashSet<string>();var buildings=new HashSet<string>();
        foreach(var slab in model.slabs??Array.Empty<SlabData>())
            if(Math.Abs(slab.z-data.z)<.001 && !string.IsNullOrEmpty(slab.nivel))levels.Add(slab.nivel);
        foreach(int tag in data.slaveTags??Array.Empty<int>())
            if(nodeBuildings.TryGetValue(tag,out var set))buildings.UnionWith(set);
        group.levels=string.Join(" / ",levels);group.buildings=string.Join(" + ",buildings);
        if(group.issue!=null)return;
        group.root=new GameObject($"Diafragma_Z{data.z}_Maestro_{data.master}");group.root.transform.SetParent(visualRoot.transform,false);
        int count=data.slaveTags.Length;
        group.pointVertices=new Vector3[(count+1)*6];group.ghostVertices=new Vector3[(count+1)*6];
        group.linkVertices=new Vector3[count*2];group.targets=new Transform[count+1];
        group.points=PointMesh(group,"Nodos_vinculados_y_maestro",false);
        group.original=PointMesh(group,"Referencia_original",true);
        group.links=new Mesh {name="Vínculos_MPC_no_son_barras"};
        var indices=new int[count*2];var colors=new Color[count*2];
        for(int j=0;j<indices.Length;j++){indices[j]=j;colors[j]=new Color(group.color.r,group.color.g,group.color.b,.42f);}
        group.links.vertices=group.linkVertices;group.links.colors=colors;group.links.SetIndices(indices,MeshTopology.Lines,0);
        MeshObject(group.root,"Vínculos",group.links);
        for(int j=0;j<=count;j++)
        {
            int tag=j==0?data.master:data.slaveTags[j-1];
            var target=new GameObject(j==0?$"Maestro {tag}":$"Nodo vinculado {tag}"); target.transform.SetParent(group.root.transform,false);
            target.AddComponent<SphereCollider>().radius=j==0?.55f:.22f;
            var info=target.AddComponent<DiaphragmSelectable>();info.viewer=this;info.groupIndex=index;info.nodeTag=tag;
            group.targets[j]=target.transform;if(j==0)group.master=info;
        }
    }

    private Mesh PointMesh(Group group,string name,bool ghost)
    {
        int count=group.data.slaveTags.Length+1;var triangles=new int[count*24];var colors=new Color[count*6];
        int[] octa={0,2,4,2,1,4,1,3,4,3,0,4,2,0,5,1,2,5,3,1,5,0,3,5};
        for(int j=0;j<count;j++)
        {
            var color=j==0?new Color(1f,.79f,.12f):group.color;if(ghost)color=new Color(.7f,.8f,.9f,.22f);
            for(int k=0;k<6;k++)colors[j*6+k]=color;
            for(int k=0;k<24;k++)triangles[j*24+k]=j*6+octa[k];
        }
        var mesh=new Mesh {name=name};mesh.vertices=ghost?group.ghostVertices:group.pointVertices;mesh.colors=colors;mesh.triangles=triangles;
        mesh.MarkDynamic();MeshObject(group.root,name,mesh);return mesh;
    }
    private void MeshObject(GameObject parent,string name,Mesh mesh)
    {
        var go=new GameObject(name);go.transform.SetParent(parent.transform,false);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
    }

    private void RefreshMotion()
    {
        var records=new Dictionary<int,DisplacementRecord>();
        var excluded=new HashSet<int>();
        if(UnityData.DisplacementsByCombo==null)return;
        if(UnityData.UseBaseCaseFactors)
        {
            string[] cases={"G","Q","EX","EY"};float[] factors={UnityData.FactorG,UnityData.FactorQ,UnityData.FactorEX,UnityData.FactorEY};
            for(int i=0;i<4;i++)if(factors[i]!=0)
            {
                if(!UnityData.DisplacementsByCombo.TryGetValue(cases[i],out var rows))
                {foreach(var n in nodes.Keys)excluded.Add(n);continue;}
                var present=new HashSet<int>();
                foreach(var d in rows)
                {
                    present.Add(d.node);
                    if(!records.TryGetValue(d.node,out var r))records[d.node]=r=new DisplacementRecord {node=d.node};
                    float f=factors[i];r.ux+=d.ux*f;r.uy+=d.uy*f;r.uz+=d.uz*f;r.rx+=d.rx*f;r.ry+=d.ry*f;r.rz+=d.rz*f;
                }
                foreach(var n in nodes.Keys)if(!present.Contains(n))excluded.Add(n);
            }
        }
        else if(UnityData.ActiveCombo!=null && UnityData.DisplacementsByCombo.TryGetValue(UnityData.ActiveCombo,out var rows))
            foreach(var d in rows)records[d.node]=new DisplacementRecord {node=d.node,ux=d.ux,uy=d.uy,uz=d.uz,rx=d.rx,ry=d.ry,rz=d.rz};
        foreach(int n in excluded)records.Remove(n);
        // Moving-load export contains translations only. Its rotations are not invented here.
        foreach(var g in groups)g.motion=DiaphragmKinematics.Evaluate(g.data,nodes,records);
    }

    private static void Octa(Vector3[] output,int offset,Vector3 center,float radius)
    {
        output[offset]=center+Vector3.right*radius;output[offset+1]=center-Vector3.right*radius;
        output[offset+2]=center+Vector3.up*radius;output[offset+3]=center-Vector3.up*radius;
        output[offset+4]=center+Vector3.forward*radius;output[offset+5]=center-Vector3.forward*radius;
    }

    private void LateUpdate()
    {
        if(!open)return;
        if(Time.unscaledTime>=nextRefresh){RefreshMotion();nextRefresh=Time.unscaledTime+.15f;}
        UpdateVisibility();float scale=diagrams!=null?diagrams.DiaphragmDisplayScale:0;
        foreach(var g in groups)
        {
            if(g.root==null || !g.root.activeSelf)continue;
            Vector3 master=new Vector3(g.data.x,g.data.z,g.data.y);
            Vector3 movedMaster=master+(g.motion.available?new Vector3((float)g.motion.ux,0,(float)g.motion.uy)*scale:Vector3.zero);
            for(int j=0;j<g.targets.Length;j++)
            {
                Vector3 start=master,pos=movedMaster;
                if(j>0)
                {
                    int id=g.data.slaveTags[j-1];var n=nodes[id];start=new Vector3(n.x,n.z,n.y);
                    pos=start+UnityData.GetNodeDisplacement(UnityData.ActiveCombo,id)*scale;
                    g.linkVertices[(j-1)*2]=movedMaster;g.linkVertices[(j-1)*2+1]=pos;
                }
                g.targets[j].localPosition=pos;
                Octa(g.pointVertices,j*6,pos,j==0?.48f:.16f);Octa(g.ghostVertices,j*6,start,j==0?.37f:.11f);
            }
            g.points.vertices=g.pointVertices;g.points.RecalculateBounds();g.links.vertices=g.linkVertices;g.links.RecalculateBounds();
            g.original.vertices=g.ghostVertices;g.original.RecalculateBounds();
            g.root.transform.Find("Vínculos").gameObject.SetActive(links);
            g.root.transform.Find("Referencia_original").gameObject.SetActive(original && scale!=0);
        }
    }

    private bool PassesFloor(Group g)
    {
        string floor=owner.DiaphragmFloorFilter;if(floor=="Todos")return true;
        foreach(var slab in model.slabs??Array.Empty<SlabData>())
            if(slab.nivel==floor && Math.Abs(slab.z-g.data.z)<.001)return true;
        foreach(var e in model.elements??Array.Empty<ElementData>())
            if(e.piso==floor && nodes.TryGetValue(e.nodeJ,out var n) && Math.Abs(n.z-g.data.z)<.001)return true;
        return false;
    }
    private void UpdateVisibility()
    {
        for(int i=0;i<groups.Count;i++)if(groups[i].root!=null)
            groups[i].root.SetActive(open && (all || focused==i) && PassesFloor(groups[i]) && !StructuralXRayController.Rendering && !SeismicPlaybackController.IsActive && !DesktopWalkthrough.IsActive);
    }
    public void Focus(int index){focused=Mathf.Clamp(index,0,Mathf.Max(0,groups.Count-1));UpdateVisibility();}
    private void Inspect()
    {
        if(groups.Count==0)return;var g=groups[focused];
        if(g.master!=null)FindObjectOfType<ElementPicker>()?.SelectInfo(g.master);
    }

    public float Draw(float x,float y,float width)
    {
        if(wrap==null)wrap=new GUIStyle(GUI.skin.label){wordWrap=true,fontSize=11};
        Color saved=GUI.backgroundColor;GUI.backgroundColor=open?new Color(.2f,.8f,.95f):saved;
        if(GUI.Button(new Rect(x,y,width,28),open?"OCULTAR DIAFRAGMAS":"DIAFRAGMAS — RESTRICCIONES"))
        {open=!open;if(open)all=true;RefreshMotion();UpdateVisibility();}
        GUI.backgroundColor=saved;if(!open)return 36f;y+=34;
        if(groups.Count==0){GUI.Label(new Rect(x,y,width,130),"Sin grupos reales exportados. Ejecute exportar_diafragmas.py; no se utiliza la lista histórica de losas.",wrap);return ContentHeight;}
        float buttonWidth=(width-8f)/3f;
        for(int i=0;i<groups.Count;i++)
        {
            GUI.backgroundColor=all || focused==i?groups[i].color:saved;
            if(GUI.Button(new Rect(x+(i%3)*(buttonWidth+4f),y+(i/3)*28f,buttonWidth,24f),$"Z = {groups[i].data.z:0.##} m"))
            {all=false;Focus(i);Inspect();}
        }
        GUI.backgroundColor=saved;y+=Mathf.Ceil(groups.Count/3f)*28f;
        all=GUI.Toggle(new Rect(x,y,width*.5f,22),all,"Todos los niveles");links=GUI.Toggle(new Rect(x+width*.5f,y,width*.5f,22),links,"Vínculos MPC");y+=24;
        GUI.Label(new Rect(x,y,width,38),all?$"Vista: TODOS · filtro: {owner.DiaphragmFloorFilter}":$"Vista: Z = {groups[focused].data.z:0.##} m · filtro: {owner.DiaphragmFloorFilter}",wrap);y+=40;
        original=GUI.Toggle(new Rect(x,y,width,22),original,"Referencia original en deformada");y+=25;
        if(GUI.Button(new Rect(x,y,width,25),"INSPECCIONAR DIAFRAGMA"))Inspect();y+=29;
        GUI.Label(new Rect(x,y,width,42),"Dorado: maestro · color: nodos vinculados. Líneas = restricciones, no elementos estructurales. Respeta el filtro por piso.",wrap);
        return ContentHeight;
    }

    public string Describe(int index,int tag)
    {
        if(index<0||index>=groups.Count)return "Diafragma ausente.";
        var g=groups[index];var d=g.data;var m=g.motion;var b=new StringBuilder();
        b.AppendLine("--- DIAFRAGMA RÍGIDO · OPENSEES ---");
        b.AppendLine($"Cota Z global: {d.z:0.###} m\nPisos: {g.levels}\nEdificios vinculados: {g.buildings}");
        b.AppendLine($"Nodo seleccionado: {tag} · {(tag==d.master?"MAESTRO AUXILIAR":"VINCULADO")}\nMaestro OpenSees: {d.master}\nNodos vinculados: {d.slaves}");
        b.AppendLine($"Posición maestro global [m]: ({d.x:0.###}, {d.y:0.###}, {d.z:0.###})");
        b.AppendLine("--- RESTRICCIONES Y SUPUESTOS ---");
        b.AppendLine("rigidDiaphragm(3, maestro, nodos…)\nPlano XY estructural · normal Z.\nGDL vinculados: Ux, Uy, Rz (1, 2, 6).\nMaestro: Uz, Rx, Ry restringidos; Ux, Uy, Rz libres.\nLos nodos conservan Uz, Rx y Ry independientes del vínculo.\nHipótesis de pequeñas rotaciones; no es una losa shell.");
        if(g.buildings.Contains(" + "))b.AppendLine("ATENCIÓN: el modelo actual agrupa por cota y vincula ambos edificios. Esta visualización conserva ese supuesto; no demuestra una junta estructural independiente.");
        b.AppendLine("--- MOVIMIENTO REAL · SIN AMPLIFICAR ---");b.AppendLine(UnityData.GetActiveLoadLabel());
        if(g.issue!=null)b.AppendLine("Datos inconsistentes: "+g.issue);
        else if(m==null || !m.available)b.AppendLine($"Respuesta incompleta: {m?.samples??0}/{d.slaves} nodos. No se certifica compatibilidad.");
        else
        {
            b.AppendLine($"Ux maestro: {m.ux*1000:0.####} mm\nUy maestro: {m.uy*1000:0.####} mm\nRz: {m.rz:0.######} rad ({m.rz*180/Math.PI:0.####}°)");
            b.AppendLine("Movimiento del maestro recuperado de Ux, Uy y Rz exportados de sus nodos vinculados; no es una medición adicional.");
            b.AppendLine($"Residuo máximo XY: {m.residual*1000:0.000000} mm\nResiduo Rz: {m.rotationResidual:0.00000000} rad\nCompatibilidad: {(m.residual<.00001 && m.rotationResidual<.000001?"VERIFICADA en datos exportados":"REVISAR resultados y restricciones")}");
            b.AppendLine("ux(n)=ux(M)−Rz·(y(n)−y(M))\nuy(n)=uy(M)+Rz·(x(n)−x(M))\nrz(n)=rz(M)");
        }
        b.AppendLine("La comprobación usa los casos estáticos, sin incrementos de carga móvil (no exportan rotaciones). El dibujo de los nodos sí sigue la deformada activa. Sismo dinámico: este modo se oculta.");
        b.AppendLine($"Escala gráfica actual: {(diagrams!=null?diagrams.DiaphragmDisplayScale:0):0.##}×; no modifica OpenSees.\nOpenSees (X,Y,Z) → Unity (X,Z,Y).\nFuente: p1l4.analysisModel.diafragmas; diaphragmList histórica no utilizada.");
        b.AppendLine("--- NODOS VINCULADOS (TAGS EXACTOS) ---");
        var tags=d.slaveTags??Array.Empty<int>();
        for(int i=0;i<tags.Length;i+=10)b.AppendLine(string.Join(", ",new ArraySegment<int>(tags,i,Math.Min(10,tags.Length-i))));
        return b.ToString();
    }

    private void Clear()
    {
        foreach(var g in groups){Release(g.points);Release(g.links);Release(g.original);}
        groups.Clear();nodes.Clear();nodeBuildings.Clear();Release(visualRoot);Release(material);
    }
    private static void Release(UnityEngine.Object obj){if(obj==null)return;if(Application.isPlaying)Destroy(obj);else DestroyImmediate(obj);}
    private void OnDisable(){if(visualRoot!=null)visualRoot.SetActive(false);}
    private void OnDestroy(){Clear();}
}
