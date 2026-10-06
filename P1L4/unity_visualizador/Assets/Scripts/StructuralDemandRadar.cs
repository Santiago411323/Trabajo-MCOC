using System.Collections.Generic;
using UnityEngine;

public sealed class StructuralDemandRadar : MonoBehaviour
{
    private sealed class Entry
    {
        public ElementSelectable Element;
        public Renderer Renderer;
        public MaterialPropertyBlock Original;
        public DemandRadarRanking.Reading Reading;
        public string Combo;
        public float[] Adjustment;
    }
    private readonly List<Entry> entries=new List<Entry>(),ranking=new List<Entry>();
    // Native Unity resources must be created on the main thread, not in field initializers.
    private MaterialPropertyBlock block;
    private readonly Dictionary<string,float[]> adjustedCases=new Dictionary<string,float[]>();
    private readonly List<Entry> tourStops=new List<Entry>();
    private bool active,painted,touring;
    private int metric,combination,filter,tourIndex,missing;
    private float nextRefresh,nextVisit;
    private ElementPicker picker;
    private StructureViewer viewer;
    private OrbitCamera orbit;
    private string navigationStatus;
    private GUIStyle heading,small,row;
    public float ContentHeight => active?720:37;
    private bool Paused=>StructuralXRayController.Rendering || DesignComparisonSession.Rendering || SeismicPlaybackController.IsActive || (metric==0 && UnityData.ActiveCombo!=null && UnityData.ActiveCombo.StartsWith("LRFD_"));

    public void Initialize(List<ElementSelectable> elements)
    {
        if(block==null)block=new MaterialPropertyBlock();
        Restore();entries.Clear();ranking.Clear();adjustedCases.Clear();
        foreach(var e in elements)
        {
            if(e==null || e.data==null || (e.data.type!="viga" && e.data.type!="columna"))continue;
            var renderer=e.GetComponent<Renderer>();
            if(renderer==null)continue;
            var original=new MaterialPropertyBlock();renderer.GetPropertyBlock(original);
            entries.Add(new Entry{Element=e,Renderer=renderer,Original=original});
        }
        viewer=GetComponent<StructureViewer>();picker=FindFirstObjectByType<ElementPicker>();
        orbit=picker!=null && picker.cam!=null?picker.cam.GetComponent<OrbitCamera>():FindFirstObjectByType<OrbitCamera>();
        nextRefresh=0;
    }
    private void StopTour(){touring=false;orbit?.CancelRadarFocus();}
    public void CancelNavigation(){StopTour();}
    public void SuspendForComparison(){Restore();StopTour();}
    private void OnDisable(){Restore();StopTour();}
    private void Update()
    {
        if(!active || Paused){if(painted)Restore();StopTour();return;}
        if(Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))StopTour();
        if(!touring && Time.unscaledTime>=nextRefresh){Rebuild();nextRefresh=Time.unscaledTime+.6f;}
        if(touring && Time.unscaledTime>=nextVisit)
        {
            if(tourIndex>=tourStops.Count){touring=false;navigationStatus="Recorrido completo: "+tourStops.Count+" elementos.";}
            else
            {
                var stop=tourStops[tourIndex++];
                if(!Visit(stop)){StopTour();return;}
                navigationStatus=$"Visitando {tourIndex}/{tourStops.Count}: {stop.Element.data.elementTag}";
                nextVisit=Time.unscaledTime+4;
            }
        }
    }
    private void LateUpdate()
    {
        if(!active || Paused)return;
        if(block==null)block=new MaterialPropertyBlock();
        float maximum=ranking.Count>0?ranking[0].Reading.Value:0;
        foreach(var entry in entries)
        {
            if(entry.Renderer==null)continue;
            bool selected=picker!=null && picker.Selected==entry.Element;
            Color color=selected?Color.yellow:entry.Reading==null || !entry.Reading.Available?
                new Color(.28f,.32f,.36f):Heat(entry.Reading,maximum);
            entry.Renderer.GetPropertyBlock(block);block.SetColor("_Color",color);block.SetColor("_BaseColor",color);
            entry.Renderer.SetPropertyBlock(block);
        }
        painted=true;
    }
    private Color Heat(DemandRadarRanking.Reading reading,float maximum)
    {
        float v=metric==0?(reading.OutsideCurve?1.1f:reading.Value):maximum>0?reading.Value/maximum:0;
        if(v<.5f)return Color.Lerp(new Color(.12f,.38f,.3f),new Color(.25f,.95f,.42f),v*2);
        if(v<.8f)return Color.Lerp(new Color(.25f,.95f,.42f),new Color(1f,.88f,.18f),(v-.5f)/.3f);
        if(v<1)return Color.Lerp(new Color(1f,.88f,.18f),new Color(1f,.43f,.12f),(v-.8f)/.2f);
        return new Color(1f,.18f,.13f);
    }
    private void Restore()
    {foreach(var entry in entries)if(entry.Renderer!=null)entry.Renderer.SetPropertyBlock(entry.Original);painted=false;}
    private void Rebuild()
    {
        if(UnityData.UseBaseCaseFactors && !string.IsNullOrEmpty(UnityData.SelectedPresetCombo))
            adjustedCases[UnityData.SelectedPresetCombo]=new[]{UnityData.FactorG,UnityData.FactorQ,UnityData.FactorEX,UnityData.FactorEY};
        else if(!string.IsNullOrEmpty(UnityData.ActiveCombo))adjustedCases.Remove(UnityData.ActiveCombo);
        ranking.Clear();missing=0;
        foreach(var entry in entries)
        {
            entry.Reading=null;
            var e=entry.Element;
            if(e==null || !e.gameObject.activeInHierarchy || entry.Renderer==null || !entry.Renderer.enabled)continue;
            if(filter==1 && e.data.type!="viga" || filter==2 && e.data.type!="columna")continue;
            if(!UnityData.TryGetFrameGeometry(e.data.id,out var frame)){missing++;continue;}
            PMCurveData curve=UnityData.GetPMCurve(e.pmSectionId);
            var material=UnityData.GetMaterial(e.pmSectionId);
            // Coincide con la restricción existente: no reflejar capacidad positiva en vigas asimétricas.
            if(metric==0 && material!=null && material.topBars!=material.bottomBars)
            {missing++;continue;}
            string[] cases=combination==4?new[]{"C1","C2","C3"}:
                new[]{combination==0?UnityData.ActiveCombo:"C"+combination};
            bool complete=true;
            foreach(string combo in cases)
            {
                string named=combo=="SUPER"?UnityData.SelectedPresetCombo:combo;
                float[] adjustment=null;
                if(named!=null)adjustedCases.TryGetValue(named,out adjustment);
                var forces=combination==0?UnityData.GetElementForces(combo,e.data.id):
                    adjustment!=null?AdjustedForces(e.data.id,adjustment):UnityData.GetElementForcesForComparison(combo,e.data.id);
                var reading=DemandRadarRanking.Evaluate(forces,frame.Length,curve,(DemandRadarRanking.Metric)metric);
                if(!reading.Available){complete=false;continue;}
                if(entry.Reading==null || reading.Value>entry.Reading.Value)
                {entry.Reading=reading;entry.Combo=named??combo;entry.Adjustment=adjustment;}
            }
            // Envolvente incompleta no se presenta como un máximo de tres casos.
            if(!complete || entry.Reading==null){entry.Reading=null;missing++;continue;}
            ranking.Add(entry);
        }
        ranking.Sort((a,b)=>{int c=b.Reading.Value.CompareTo(a.Reading.Value);return c!=0?c:a.Element.data.id.CompareTo(b.Element.data.id);});
    }
    private static float[] AdjustedForces(int id,float[] factors)
    {
        var result=new float[12];string[] names={"G","Q","EX","EY"};
        for(int i=0;i<4;i++)
        {
            if(factors[i]==0)continue;
            var source=UnityData.GetElementForcesForCase(names[i],id);
            if(!FrameForces.IsValid(source))return null;
            for(int j=0;j<12;j++)result[j]+=source[j]*factors[i];
        }
        if(UnityData.MobileForces.TryGetValue(id,out var extra))
        {if(!FrameForces.IsValid(extra))return null;for(int j=0;j<12;j++)result[j]+=extra[j];}
        return result;
    }
    private bool BindNavigation()
    {
        var main=Camera.main;
        picker=main!=null?main.GetComponent<ElementPicker>():null;
        if(picker==null)picker=FindFirstObjectByType<ElementPicker>(FindObjectsInactive.Include);
        if(picker!=null && picker.cam==null)picker.cam=main;
        var camera=picker!=null?picker.cam:main;
        orbit=camera!=null?camera.GetComponent<OrbitCamera>():null;
        if(orbit==null && camera!=null)orbit=camera.GetComponentInParent<OrbitCamera>();
        if(picker==null || orbit==null)
        {navigationStatus="No se encontró la cámara del visualizador o el selector. Recorrido detenido.";return false;}
        return true;
    }
    private void StartTour()
    {
        Rebuild();tourStops.Clear();
        for(int i=0;i<Mathf.Min(5,ranking.Count);i++)
        {
            var e=ranking[i];tourStops.Add(new Entry{Element=e.Element,Reading=e.Reading,Combo=e.Combo,Adjustment=e.Adjustment});
        }
        if(tourStops.Count==0){navigationStatus="No hay elementos evaluados para recorrer.";return;}
        touring=true;tourIndex=1;
        if(!Visit(tourStops[0])){StopTour();return;}
        navigationStatus=$"Visitando 1/{tourStops.Count}: {tourStops[0].Element.data.elementTag}";
        nextVisit=Time.unscaledTime+4;
    }
    private bool Visit(Entry entry)
    {
        if(entry.Element==null)return false;
        viewer?.GetComponent<DesktopWalkthrough>()?.Exit();
        if(!BindNavigation())return false;
        if(entry.Combo!="SUPER")viewer?.ActivateRadarCombination(entry.Combo,entry.Adjustment);
        picker.SelectElement(entry.Element,false);
        int component=metric==0?3:metric==1?(Mathf.Abs(entry.Reading.My)>=Mathf.Abs(entry.Reading.Mz)?3:4):
            metric==2?(Mathf.Abs(entry.Reading.Vy)>=Mathf.Abs(entry.Reading.Vz)?1:2):0;
        ElementResultsPanel.OpenFromRadar(entry.Element,component,entry.Reading.Position);
        orbit.enabled=true;
        orbit.BeginRadarFocus((entry.Element.startPoint+entry.Element.endPoint)*.5f,
            Mathf.Clamp(Vector3.Distance(entry.Element.startPoint,entry.Element.endPoint)*3,12,65));
        return true;
    }
    public float Draw(float x,float y,float width)
    {
        if(heading==null)
        {
            heading=new GUIStyle(GUI.skin.label){fontStyle=FontStyle.Bold,fontSize=12,wordWrap=true};heading.normal.textColor=Color.cyan;
            small=new GUIStyle(GUI.skin.label){fontSize=11,wordWrap=true};
            row=new GUIStyle(GUI.skin.button){fontSize=11,wordWrap=true,alignment=TextAnchor.MiddleLeft};
        }
        float start=y;
        if(GUI.Button(new Rect(x,y,width,28),active?"OCULTAR RADAR DE DEMANDA":"ACTIVAR RADAR DE DEMANDA"))
        {active=!active;StopTour();nextRefresh=0;if(!active)Restore();else Rebuild();}
        y+=36;if(!active)return y-start;
        if(Paused)
        {GUI.Label(new Rect(x,y,width,58),SeismicPlaybackController.IsActive?"Radar estático suspendido durante la reproducción sísmica.":
            "Radar nominal pausado en casos LRFD. Consulte D/C de diseño en Resultados U1–U7; no se mezclan ambos criterios.",small);return y-start+64;}
        GUI.Label(new Rect(x,y,width,22),"RADAR · VIGAS Y COLUMNAS",heading);y+=25;
        int nextMetric=GUI.Toolbar(new Rect(x,y,width,25),metric,new[]{"D/C","M","V","N−","N+"});y+=30;
        int nextCase=GUI.Toolbar(new Rect(x,y,width,25),combination,new[]{"Activa","C1","C2","C3","Envolv."});y+=30;
        int nextFilter=GUI.Toolbar(new Rect(x,y,width,25),filter,new[]{"Todos","Vigas","Columnas"});y+=31;
        if(nextMetric!=metric || nextCase!=combination || nextFilter!=filter)
        {metric=nextMetric;combination=nextCase;filter=nextFilter;StopTour();Rebuild();}
        GUI.Label(new Rect(x,y,width,80),metric==0?
            "D/C P–My uniaxial · curva nominal. Verde <0,5 · amarillo 0,8 · rojo ≥1. No es una verificación completa de seguridad.":
            (metric==1?"M = √(My²+Mz²) [kN·m]":metric==2?"V = √(Vy²+Vz²) [kN]":metric==3?"Compresión = máx(0,−N) [kN]":"Tracción = máx(0,N) [kN]")+". Colores relativos al máximo visible; no indican capacidad.",small);y+=85;
        GUI.Label(new Rect(x,y,width,48),$"{ranking.Count} evaluados · {missing} sin evaluar\nVisibles · 41 posiciones · seleccionado amarillo\nAjustes de casos conservados durante esta sesión del radar.",small);y+=54;
        bool enabled=GUI.enabled;GUI.enabled=enabled && ranking.Count>0;
        if(GUI.Button(new Rect(x,y,width,26),touring?"DETENER RECORRIDO":"RECORRER TOP 5"))
        {if(touring)StopTour();else StartTour();}
        GUI.enabled=enabled;y+=33;
        if(!string.IsNullOrEmpty(navigationStatus)){GUI.Label(new Rect(x,y,width,34),navigationStatus,small);y+=39;}
        float maximum=ranking.Count>0?ranking[0].Reading.Value:0;
        for(int i=0;i<Mathf.Min(5,ranking.Count);i++)
        {
            var entry=ranking[i];var reading=entry.Reading;
            Color old=GUI.backgroundColor;GUI.backgroundColor=Heat(reading,maximum);
            string value=reading.OutsideCurve?"FUERA DE CURVA":reading.Value.ToString("0.###")+(metric==0?" D/C":metric==1?" kN·m":" kN");
            if(GUI.Button(new Rect(x,y,width,57),$"{i+1}. {entry.Element.data.elementTag} · ID {entry.Element.data.id}\n{value} · {entry.Combo} · {100*reading.Position:0.#}% desde I",row))
            {StopTour();if(Visit(entry))navigationStatus="Seleccionado: "+entry.Element.data.elementTag;}
            GUI.backgroundColor=old;y+=62;
        }
        if(ranking.Count==0){GUI.Label(new Rect(x,y,width,45),"Sin elementos con datos aplicables para esta lectura.",small);y+=50;}
        return y-start+9;
    }
}
