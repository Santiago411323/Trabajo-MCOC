using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public partial class ElementResultsPanel
{
    private float designBlend,designScale=50f,designSlice=.5f,designClock;
    private bool designPlaying,designEffects,designCapacityColor=true;
    private int designForce=3;
    private DesignComparisonOverlay designOverlay;
    private string designExportPath,designExportStatus;
    private readonly Color designBeforeColor=new Color(.15f,.85f,1f),designAfterColor=new Color(1f,.3f,.75f),designBlendColor=new Color(1f,.84f,.2f);

    private void UpdateDesignComparison()
    {
        if(!string.IsNullOrEmpty(DesignComparisonSession.PendingSelection)&&DesignComparisonSession.Ready)
        {
            string key=DesignComparisonSession.PendingSelection;
            foreach(var e in GetComponentsInChildren<ElementSelectable>(true))
            {
                if((e.isWall?"W_"+e.wallId:e.data!=null?DesignComparisonSnapshot.Key(e.data):null)!=key)continue;
                DesignComparisonSession.PendingSelection=null;
                GetComponent<StructureViewer>()?.RevealSearchTarget(e,null);
                picker?.SelectElement(e,true);selected=e;expanded=true;SetView(ResultView.DesignComparison);break;
            }
        }
        bool show=expanded&&view==ResultView.DesignComparison&&selected!=null&&DesignComparisonSession.Ready&&
            !StructuralXRayController.Rendering&&!StructuralModelEditor.ResultsStale&&!SeismicPlaybackController.IsActive;
        if(!show){ClearDesignComparison();return;}
        if(designPlaying)
        {
            designClock+=Time.unscaledDeltaTime;
            designBlend=Mathf.SmoothStep(0,1,Mathf.PingPong(designClock/3f,1));
        }
        if(designOverlay==null)designOverlay=new DesignComparisonOverlay();
        designOverlay.Show(transform,selected,designBlend,designScale,designForce,designEffects,designCapacityColor);
    }
    private void ClearDesignComparison(){designOverlay?.Dispose();designOverlay=null;}

    private void DrawDesignComparison(ElementSelectable element,float x,float y,float width)
    {
        if(StructuralXRayController.Rendering){DrawUnavailable(x,y,width,"ANTES ↔ DESPUÉS","Cierra X-Ray antes de comparar diseños; no se mezclan dos geometrías con una respuesta incremental de otro estado.");return;}
        GUI.Label(new Rect(x,y,width,23),"DUELO DE DISEÑOS · ANTES ↔ DESPUÉS",titleStyle);y+=28;
        bool enabled=GUI.enabled;GUI.enabled=enabled&&!StructuralModelEditor.ResultsStale;
        if(GUI.Button(new Rect(x,y,width*.34f-4,27),DesignComparisonSession.Before==null?"GUARDAR ANTES":"NUEVO ANTES (REEMPLAZAR)"))
            DesignComparisonSession.CaptureBefore(true);
        GUI.enabled=enabled&&!StructuralModelEditor.ResultsStale&&DesignComparisonSession.Before!=null;
        if(GUI.Button(new Rect(x+width*.34f,y,width*.33f-4,27),"DESPUÉS ACTUAL CALCULADO"))
            DesignComparisonSession.AcceptAfter(UnityData.Structure,DesktopModelFile.ActivePath);
        GUI.enabled=enabled;
        if(GUI.Button(new Rect(x+width*.67f,y,width*.33f,27),"EDITAR ESTE ELEMENTO")){SetView(ResultView.ModelEditor);return;}y+=34;
        GUI.Label(new Rect(x,y,width,62),DesignComparisonSession.Status,textStyle);y+=66;
        if(StructuralModelEditor.ResultsStale)
        {
            GUI.Label(new Rect(x,y,width,70),"VISTA PREVIA: todavía no es un resultado nuevo. Ejecuta REANALIZAR OPENSEES; la comparación se abre al terminar.",failureStyle);return;
        }
        if(!DesignComparisonSession.Ready)
        {
            GUI.Label(new Rect(x,y,width,100),"1 · Guarda ANTES con resultados vigentes.\n2 · Cambia sección o armadura en EDITAR MODELO.\n3 · Reanaliza en OpenSees.\n4 · Recorre la barra y descubre qué cambió en el elemento y sus vecinos.\nEl editor guarda ANTES automáticamente antes de la primera vista previa válida.",textStyle);return;
        }
        string[] cases={"C1","C2","C3"};
        for(int i=0;i<3;i++)if(GUI.Button(new Rect(x+i*60,y,55,24),cases[i]))
            DesignComparisonSession.SetLoads(new DesignComparisonLoads{Combo=cases[i],Label=cases[i]+": G+0.5Q"+(i==2?"−0.3EX":"+0.3EX")+(i==1?"−0.2EY":"+0.2EY"),G=1,Q=.5f,EX=i==2?-.3f:.3f,EY=i==1?-.2f:.2f});
        GUI.Label(new Rect(x+185,y,width-185,42),"MISMO ESCENARIO EN AMBOS: "+DesignComparisonSession.Loads.Label,mutedStyle);y+=46;
        GUI.Label(new Rect(x,y,width*.43f,22),"ANTES · calculado",new GUIStyle(valueStyle){normal={textColor=designBeforeColor}});
        GUI.Label(new Rect(x+width*.57f,y,width*.43f,22),"DESPUÉS · calculado",new GUIStyle(valueStyle){alignment=TextAnchor.MiddleRight,normal={textColor=designAfterColor}});y+=25;
        float next=GUI.HorizontalSlider(new Rect(x,y,width-150,23),designBlend,0,1);
        if(Math.Abs(next-designBlend)>.0001){designBlend=next;designPlaying=false;}
        if(GUI.Button(new Rect(x+width-142,y-3,85,25),designPlaying?"PAUSAR":"REPRODUCIR")){designPlaying=!designPlaying;designClock=designBlend*3;}
        if(GUI.Button(new Rect(x+width-50,y-3,50,25),"↺")){designPlaying=false;designBlend=0;designClock=0;}y+=28;
        GUI.Label(new Rect(x,y,width,36),$"Transición visual: {designBlend*100:0}% · sección y dorado interpolados gráficamente, no un tercer análisis. Las cifras siguientes son siempre de los dos estados calculados.",mutedStyle);y+=42;
        GUI.Label(new Rect(x,y,210,23),$"Amplificación de deformada: {designScale:0}×",mutedStyle);
        designScale=GUI.HorizontalSlider(new Rect(x+215,y+4,width-215,20),designScale,1,200);y+=28;
        designEffects=GUI.Toggle(new Rect(x,y,width*.55f,24),designEffects,"MOSTRAR EFECTO EN EL EDIFICIO / VECINOS");
        int color=GUI.SelectionGrid(new Rect(x+width*.57f,y,width*.43f,24),designCapacityColor?0:1,new[]{"D/C P–My","|M|"},2);designCapacityColor=color==0;y+=30;
        GUI.Label(new Rect(x,y,width,34),"Cian: antes · magenta: después · rojo: aumentó la métrica · azul: disminuyó · gris: sin cambio/datos. D/C es nominal y uniaxial; no certifica el diseño completo.",mutedStyle);y+=40;
        if(element.isWall){DrawWallDesignComparison(element,x,y,width);return;}
        if(element.data==null){DrawUnavailable(x,y,width,"COMPARACIÓN","Elemento sin ID estructural.");return;}
        string key=DesignComparisonSnapshot.Key(element.data);
        var before=DesignComparisonSession.Reading(key,false);var after=DesignComparisonSession.Reading(key,true);
        if(!before.Available||!after.Available){DrawUnavailable(x,y,width,"MISMO ELEMENTO EN AMBOS ESTADOS","Faltan acciones o desplazamientos calculados. No se reemplazan por ceros.");return;}
        designForce=GUI.SelectionGrid(new Rect(x,y,width,26),designForce,forceNames,5);y+=31;
        y=DrawDesignPlot(key,x,y,width);
        float cardWidth=(width-12)/3;
        ComparisonCard(new Rect(x,y,cardWidth,80),"DESPLAZAMIENTO MÁX. [mm]",before.DisplacementMm,after.DisplacementMm,true);
        ComparisonCard(new Rect(x+cardWidth+6,y,cardWidth,80),"D/C NOMINAL P–My",before.DCR,after.DCR,before.CapacityAvailable&&after.CapacityAvailable&&!before.OutsideCurve&&!after.OutsideCurve,
            before.OutsideCurve||after.OutsideCurve?"Fuera de envolvente en un estado":null);
        ComparisonCard(new Rect(x+2*(cardWidth+6),y,cardWidth,80),"Mn A P=0 [kN·m]",before.CapacityP0,after.CapacityP0,before.CapacityP0>0&&after.CapacityP0>0);y+=86;
        ComparisonCard(new Rect(x,y,cardWidth,80),"M RESULTANTE MÁX. [kN·m]",before.Moment,after.Moment,true);
        ComparisonCard(new Rect(x+cardWidth+6,y,cardWidth,80),"ACERO LONGITUDINAL [mm²]",before.SteelMm2,after.SteelMm2,before.Bars>0&&after.Bars>0);
        ComparisonCard(new Rect(x+2*(cardWidth+6),y,cardWidth,80),"PESO PROPIO DE LA PIEZA [kN]",before.SelfWeightKN,after.SelfWeightKN,true);y+=88;
        GUI.Label(new Rect(x,y,width,42),$"Sección: {before.Width:0.###}×{before.Height:0.###} → {after.Width:0.###}×{after.Height:0.###} m · acero: {before.Bars} Ø{before.Diameter:0.#} → {after.Bars} Ø{after.Diameter:0.#} mm.\nMáximos muestreados; desplazamiento Hermite desde nodos. PP de viga descuenta 0,15 m de losa.",mutedStyle);y+=47;
        if(designEffects)y=DrawDesignNeighbors(key,x,y,width);
        if(GUI.Button(new Rect(x,y,200,26),"EXPORTAR COMPARACIÓN .CSV"))ExportDesignComparison(key,before,after);
        GUI.enabled=enabled&&!string.IsNullOrEmpty(designExportPath);
        if(GUI.Button(new Rect(x+206,y,130,26),"COPIAR RUTA")){GUIUtility.systemCopyBuffer=designExportPath;designExportStatus="Ruta copiada.";}GUI.enabled=enabled;y+=32;
        GUI.Label(new Rect(x,y,width,45),designExportStatus??"Exportación incluye factores, fuentes, huellas y diferencias reales.",mutedStyle);y+=48;
        DrawDesignProvenance(x,y,width);
    }
    private float DrawDesignPlot(string key,float x,float y,float width)
    {
        var a=DesignComparisonSession.Before;var b=DesignComparisonSession.After;var loads=DesignComparisonSession.Loads;
        a.TryFrame(key,out _,out var frame);var fa=a.Forces(key,loads);var fb=b.Forces(key,loads);
        var aa=new float[61];var bb=new float[61];float max=.0001f;int[] indices={0,1,2,4,5};int component=indices[designForce];
        for(int i=0;i<=60;i++){aa[i]=FrameForces.Evaluate(fa,frame.Length,i/60f).Component(component);bb[i]=FrameForces.Evaluate(fb,frame.Length,i/60f).Component(component);max=Math.Max(max,Math.Max(Math.Abs(aa[i]),Math.Abs(bb[i])));}
        string unit=designForce>=3?"kN·m":"kN";
        GUI.Label(new Rect(x,y,width,20),forceNames[designForce]+" ["+unit+"] · misma escala en ambos diseños",valueStyle);y+=25;
        var plot=new Rect(x,y,width,155);GUI.Box(plot,GUIContent.none,cardStyle);
        float zero=plot.center.y;DrawLine(new Vector2(plot.x,zero),new Vector2(plot.xMax,zero),new Color(.4f,.45f,.5f),1);
        for(int i=1;i<=60;i++)
        {
            float px=plot.x+(i-1)/60f*plot.width,qx=plot.x+i/60f*plot.width;
            DrawLine(new Vector2(px,zero-aa[i-1]/max*65),new Vector2(qx,zero-aa[i]/max*65),designBeforeColor,2);
            DrawLine(new Vector2(px,zero-bb[i-1]/max*65),new Vector2(qx,zero-bb[i]/max*65),designAfterColor,2);
            DrawLine(new Vector2(px,zero-Mathf.Lerp(aa[i-1],bb[i-1],designBlend)/max*65),new Vector2(qx,zero-Mathf.Lerp(aa[i],bb[i],designBlend)/max*65),designBlendColor,2.8f);
        }
        if(plot.Contains(Event.current.mousePosition))designSlice=Mathf.Clamp01((Event.current.mousePosition.x-plot.x)/plot.width);
        int sample=Mathf.RoundToInt(designSlice*60);float cursor=plot.x+designSlice*plot.width;
        DrawLine(new Vector2(cursor,plot.y),new Vector2(cursor,plot.yMax),Color.white,1);
        DrawMarker(new Vector2(cursor,zero-aa[sample]/max*65),designBeforeColor,7);DrawMarker(new Vector2(cursor,zero-bb[sample]/max*65),designAfterColor,7);
        y+=160;GUI.Label(new Rect(x,y,width,24),$"I → J · x={designSlice*frame.Length:0.###} m ({designSlice*100:0.#}%) · ANTES {aa[sample]:0.###} → DESPUÉS {bb[sample]:0.###} {unit} · Δ={bb[sample]-aa[sample]:+0.###;-0.###;0}",valueStyle);
        return y+31;
    }
    private void ComparisonCard(Rect rect,string title,float before,float after,bool available,string issue=null)
    {
        GUI.Box(rect,GUIContent.none,cardStyle);GUI.Label(new Rect(rect.x+8,rect.y+5,rect.width-16,28),title,mutedStyle);
        string values=available?$"{before:0.###} → {after:0.###}":issue??"No disponible en ambos estados";
        GUI.Label(new Rect(rect.x+8,rect.y+31,rect.width-16,21),values,valueStyle);
        string delta="";
        if(available)delta=DesignComparisonReading.TryPercent(before,after,out var percent)?$"Δ {after-before:+0.###;-0.###;0} · {percent:+0.0;-0.0;0}%":$"Δ {after-before:+0.###;-0.###;0} · porcentaje N/D (antes=0)";
        GUI.Label(new Rect(rect.x+8,rect.y+55,rect.width-16,20),delta,mutedStyle);
    }
    private float DrawDesignNeighbors(string selectedKey,float x,float y,float width)
    {
        var entries=new List<KeyValuePair<string,float>>();
        foreach(var pair in DesignComparisonSession.Before.Members)
        {
            if(pair.Key==selectedKey||(pair.Value.type!="viga"&&pair.Value.type!="columna"))continue;
            var a=DesignComparisonSession.Reading(pair.Key,false);var b=DesignComparisonSession.Reading(pair.Key,true);
            if(!a.Available||!b.Available || designCapacityColor&&(!a.CapacityAvailable||!b.CapacityAvailable||a.OutsideCurve||b.OutsideCurve))continue;
            float delta=designCapacityColor?b.DCR-a.DCR:b.Moment-a.Moment;
            if(Math.Abs(delta)>1e-6)entries.Add(new KeyValuePair<string,float>(pair.Key,delta));
        }
        entries.Sort((a,b)=>Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));
        GUI.Label(new Rect(x,y,width,25),"EFECTO EN OTRAS PIEZAS · mayores cambios absolutos "+(designCapacityColor?"de D/C":"de |M|"),titleStyle);y+=29;
        if(entries.Count==0){GUI.Label(new Rect(x,y,width,35),"No se detectan cambios en esta métrica. Cambiar solo acero puede modificar capacidad sin redistribuir esfuerzos elásticos.",mutedStyle);return y+40;}
        for(int i=0;i<Math.Min(5,entries.Count);i++)
        {
            var row=entries[i];string target=row.Key;
            if(GUI.Button(new Rect(x,y,width,25),$"{i+1}. {target} · Δ {row.Value:+0.###;-0.###;0} {(designCapacityColor?"D/C":"kN·m")} · VER ELEMENTO"))
                foreach(var e in GetComponentsInChildren<ElementSelectable>(true))if(e.data!=null&&DesignComparisonSnapshot.Key(e.data)==target)
                {GetComponent<StructureViewer>()?.RevealSearchTarget(e,null);picker.SelectElement(e,true);expanded=true;SetView(ResultView.DesignComparison);break;}
            y+=29;
        }
        GUI.Label(new Rect(x,y,width,28),"Ranking de cambios del edificio, no necesariamente vecinos conectados ni un ranking de seguridad global.",mutedStyle);return y+34;
    }
    private void DrawWallDesignComparison(ElementSelectable element,float x,float y,float width)
    {
        var a=DesignComparisonSession.Before;var b=DesignComparisonSession.After;
        if(!a.Walls.TryGetValue(element.wallId,out var wa)||!b.Walls.TryGetValue(element.wallId,out var wb))
        {DrawUnavailable(x,y,width,"MURO","El ID del muro no coincide en ambos estados.");return;}
        GUI.Label(new Rect(x,y,width,40),$"Muro ID {wa.id} · espesor {wa.grosor:0.###} → {wb.grosor:0.###} m · longitud {wa.longitud:0.###} → {wb.longitud:0.###} m",valueStyle);y+=45;
        string combo=DesignComparisonSession.Loads.Combo;DemandRecord da=null,db=null;
        foreach(var d in wa.demands??Array.Empty<DemandRecord>())if(d.combo==combo)da=d;
        foreach(var d in wb.demands??Array.Empty<DemandRecord>())if(d.combo==combo)db=d;
        if(da==null||db==null){GUI.Label(new Rect(x,y,width,60),"Selecciona C1/C2/C3 para comparar demandas P–M del muro. No se superponen magnitudes de M ni se inventan N/V de una barra.",mutedStyle);return;}
        var ca=a.WallCurve(wa);var cb=b.WallCurve(wb);
        float cell=(width-12)/3;
        ComparisonCard(new Rect(x,y,cell,80),"P [kN]",da.P_kN,db.P_kN,true);
        ComparisonCard(new Rect(x+cell+6,y,cell,80),"M [kN·m]",da.M_kN_m,db.M_kN_m,true);
        float ra=ca!=null?UnityData.CapacityRatio(ca,da.P_kN,da.M_kN_m):0,rb=cb!=null?UnityData.CapacityRatio(cb,db.P_kN,db.M_kN_m):0;
        ComparisonCard(new Rect(x+2*(cell+6),y,cell,80),"D/C NOMINAL P–M",ra,rb,ca!=null&&cb!=null&&ra<UnityData.OutOfCurveRatio&&rb<UnityData.OutOfCurveRatio);y+=90;
        GUI.Label(new Rect(x,y,width,60),"La barra interpola visualmente los marcos analíticos del edificio. Aquí las demandas del muro se comparan por su registro exportado y caso exacto; no se presenta una deformación shell.",mutedStyle);y+=65;
        DrawDesignProvenance(x,y,width);
    }
    private void DrawDesignProvenance(float x,float y,float width)
    {
        var a=DesignComparisonSession.Before;var b=DesignComparisonSession.After;
        GUI.Label(new Rect(x,y,width,100),$"TRAZABILIDAD · huellas de los datos congelados\nANTES {a.Fingerprint.Substring(0,12)} · {a.CapturedUtc}\nDESPUÉS {b.Fingerprint.Substring(0,12)} · {b.CapturedUtc}\n{a.Source}\n{b.Source}\nLa comparación dura esta sesión Play; CSV conserva cifras y procedencia.",mutedStyle);
    }
    private void ExportDesignComparison(string key,DesignComparisonReading.Metrics a,DesignComparisonReading.Metrics b)
    {
        try
        {
            var text=new StringBuilder("metrica;antes;despues;delta;unidad\n");
            Action<string,float,float,string> row=(name,oldValue,newValue,unit)=>text.AppendLine(name+";"+oldValue.ToString("R",CultureInfo.InvariantCulture)+";"+newValue.ToString("R",CultureInfo.InvariantCulture)+";"+(newValue-oldValue).ToString("R",CultureInfo.InvariantCulture)+";"+unit);
            row("desplazamiento_max",a.DisplacementMm,b.DisplacementMm,"mm");row("momento_resultante_max",a.Moment,b.Moment,"kN*m");
            row("acero_longitudinal",a.SteelMm2,b.SteelMm2,"mm2");row("peso_propio_pieza",a.SelfWeightKN,b.SelfWeightKN,"kN");
            if(a.CapacityAvailable&&b.CapacityAvailable&&!a.OutsideCurve&&!b.OutsideCurve)row("DCR_nominal_P_My",a.DCR,b.DCR,"1");
            else text.AppendLine("DCR_nominal_P_My;N/D;N/D;N/D;respuesta_ausente_o_fuera_de_envolvente");
            text.AppendLine("elementTag;"+key);text.AppendLine("factores;"+DesignComparisonSession.Loads.Label);
            text.AppendLine("huella_antes;"+DesignComparisonSession.Before.Fingerprint);text.AppendLine("huella_despues;"+DesignComparisonSession.After.Fingerprint);
            text.AppendLine("fuente_antes;"+DesignComparisonSession.Before.Source);text.AppendLine("fuente_despues;"+DesignComparisonSession.After.Source);
            Directory.CreateDirectory(Application.persistentDataPath);
            designExportPath=Path.Combine(Application.persistentDataPath,"comparacion_diseno_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")+".csv");
            File.WriteAllText(designExportPath,text.ToString(),new UTF8Encoding(true));designExportStatus="Guardado: "+designExportPath;
        }
        catch(Exception e){designExportStatus="No se pudo exportar: "+e.Message;}
    }
}
