using System;
using System.Collections.Generic;
using UnityEngine;

public class StructuralElementSearch : MonoBehaviour
{
    private sealed class Target { public ElementSelectable member; public SlabSelectable slab; }
    private readonly List<Target> targets=new List<Target>();
    private readonly List<StructuralSearchIndex.Item> index=new List<StructuralSearchIndex.Item>();
    private List<StructuralSearchIndex.Item> results=new List<StructuralSearchIndex.Item>();
    private StructureViewer viewer;
    private string query="", status="Escribe un ID o tag: 72, E1_72 o el ID de una losa.";
    private int filter, page;
    private GUIStyle rowStyle, wrap;
    private bool textFocused;
    private static StructuralElementSearch focusedSearch;
    public static bool CapturesKeyboard => focusedSearch!=null && focusedSearch.isActiveAndEnabled && focusedSearch.textFocused;
    private static readonly string[] FilterLabels={"Todos","Vigas","Columnas","Muros","Losas"};
    private static readonly string[] Kinds={null,"viga","columna","muro","losa"};
    private const int PageSize=5;
    private string ControlName=>"StructuralSearch_"+GetEntityId();
    public float ContentHeight=>100f+Math.Min(PageSize,results.Count)*43f+(results.Count>PageSize?29f:0f);

    public void Initialize(StructureViewer source,List<ElementSelectable> members,List<GameObject> slabObjects)
    {
        viewer=source;targets.Clear();index.Clear();
        foreach(var member in members)
        {
            if(member==null || !member.isWall && member.data==null)continue;
            var d=member.data;
            Add(new Target{member=member},new StructuralSearchIndex.Item {
                Kind=member.isWall?"muro":d.type,Id=(member.isWall?member.wallId:d.id).ToString(),
                Tag=member.isWall?member.wallSourceId:d.elementTag,
                SourceId=member.isWall?member.wallSourceId:d.sourceId,
                Building=member.isWall?member.wallSourceBuilding:d.sourceBuilding,
                Level=member.isWall?member.wallBottom:(!string.IsNullOrEmpty(member.visualFloor)?member.visualFloor:d.piso)});
        }
        foreach(var go in slabObjects)
        {
            var slab=go!=null?go.GetComponent<SlabSelectable>():null;if(slab?.slab==null)continue;
            Add(new Target{slab=slab},new StructuralSearchIndex.Item{Kind="losa",Id=slab.slab.id,Tag=slab.slab.id,Level=slab.slab.nivel});
        }
        Search(false);
    }
    private void Add(Target target,StructuralSearchIndex.Item item){item.Target=targets.Count;targets.Add(target);index.Add(item);}
    private void Search(bool selectUnique)
    {
        results=StructuralSearchIndex.Find(index,query,Kinds[filter]);page=0;
        status=string.IsNullOrWhiteSpace(query)?"Escribe un ID o tag. La búsqueda incluye capas y pisos ocultos.":
            results.Count==0?"Sin coincidencias. Revisa el ID/tag y el tipo elegido.":
            results.Count==1?"1 coincidencia. Pulsa el resultado para localizarlo.":$"{results.Count} coincidencias. Elige el elemento y su piso.";
        if(selectUnique && results.Count==1)Select(results[0]);
    }
    public float DrawPinned(float x,float y,float width)
    {
        EnsureStyles();GUI.Label(new Rect(x,y,width,18),"BUSCAR ELEMENTO · ID / TAG",wrap);y+=20;
        var field=new Rect(x,y,Mathf.Max(60,width-100),25);
        if(Event.current.type==EventType.MouseDown && !field.Contains(Event.current.mousePosition))ReleaseFocus();
        GUI.SetNextControlName(ControlName);
        string next=GUI.TextField(field,query,100);
        textFocused=GUI.GetNameOfFocusedControl()==ControlName;
        if(textFocused)focusedSearch=this;else if(focusedSearch==this)focusedSearch=null;
        if(next!=query){query=next;Search(false);}
        bool submit=Event.current.type==EventType.KeyDown && textFocused &&
            (Event.current.keyCode==KeyCode.Return || Event.current.keyCode==KeyCode.KeypadEnter);
        if(submit){Event.current.Use();Search(true);ReleaseFocus();}
        if(GUI.Button(new Rect(x+width-94,y,68,25),"BUSCAR")){Search(true);ReleaseFocus();}
        if(GUI.Button(new Rect(x+width-22,y,22,25),"×")){query="";Search(false);ReleaseFocus();}
        return 54f;
    }
    public float DrawResults(float x,float y,float width)
    {
        EnsureStyles();float start=y;
        int next=GUI.SelectionGrid(new Rect(x,y,width,48),filter,FilterLabels,3);y+=52;
        if(next!=filter){filter=next;Search(false);ReleaseFocus();}
        GUI.Label(new Rect(x,y,width,40),status,wrap);y+=43;
        int first=page*PageSize,last=Math.Min(first+PageSize,results.Count);
        for(int i=first;i<last;i++)
        {
            var item=results[i];string detail=item.Level??"Sin piso";
            if(!string.IsNullOrEmpty(item.Building))detail+=" · "+item.Building;
            if(GUI.Button(new Rect(x,y,width,39),item.Label+"\n"+detail,rowStyle))Select(item);
            y+=43;
        }
        if(results.Count>PageSize)
        {
            bool prior=GUI.enabled;GUI.enabled=prior && page>0;
            if(GUI.Button(new Rect(x,y,35,24),"‹"))page--;GUI.enabled=prior;
            GUI.Label(new Rect(x+40,y,width-80,24),$"Página {page+1}/{Mathf.CeilToInt(results.Count/(float)PageSize)}",wrap);
            GUI.enabled=prior && last<results.Count;
            if(GUI.Button(new Rect(x+width-35,y,35,24),"›"))page++;GUI.enabled=prior;y+=29;
        }
        return Mathf.Max(ContentHeight,y-start+5);
    }
    private void Select(StructuralSearchIndex.Item item)
    {
        if(SeismicPlaybackController.IsActive){status="Cierra la reproducción sísmica para buscar en el modelo estático.";return;}
        var target=targets[item.Target];
        var picker=FindFirstObjectByType<ElementPicker>();if(picker==null){status="No se encontró el inspector del visualizador.";return;}
        GetComponent<StructuralDemandRadar>()?.CancelNavigation();
        viewer.RevealSearchTarget(target.member,target.slab);
        if(target.member!=null)picker.SelectElement(target.member,false);else picker.SelectInfo(target.slab);
        Camera camera=picker.cam!=null?picker.cam:Camera.main;
        var orbit=camera!=null?camera.GetComponent<OrbitCamera>():null;
        Renderer renderer=target.member!=null?target.member.GetComponent<Renderer>():target.slab.GetComponent<Renderer>();
        if(orbit!=null && renderer!=null)
        {
            var bounds=renderer.bounds;float size=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
            orbit.BeginRadarFocus(bounds.center,Mathf.Clamp(size*2.2f,8f,65f));
        }
        status="Seleccionado: "+item.Label+". Filtro de piso: Todos; capa activada.";
        ReleaseFocus();
    }
    private void EnsureStyles()
    {
        if(wrap!=null)return;wrap=new GUIStyle(GUI.skin.label){fontSize=11,wordWrap=true};
        rowStyle=new GUIStyle(GUI.skin.button){fontSize=11,wordWrap=true,alignment=TextAnchor.MiddleLeft};
    }
    private void ReleaseFocus(){textFocused=false;if(focusedSearch==this)focusedSearch=null;GUI.FocusControl(null);}
    private void OnDisable(){textFocused=false;if(focusedSearch==this)focusedSearch=null;}
}
