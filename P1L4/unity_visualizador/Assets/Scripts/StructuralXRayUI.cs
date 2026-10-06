using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public partial class StructuralXRayController
{
    private string exportStatus;
    private Vector2 walkingScroll;
    private static readonly string[] stages={"CARGA APLICADA","LOSA / REGIÓN TRIBUTARIA","VIGAS RECEPTORAS","COLUMNAS / MUROS / VÍNCULOS","APOYOS DEL MODELO","RESUMEN DE RESPUESTA"};
    public float Draw(float x,float y,float width)
    {
        if(!open)return 0;EnsureStyles();float top=y;
        GUI.Label(new Rect(x,y,width,24),"STRUCTURAL X-RAY · TRAZA LA RESPUESTA",heading);y+=29;
        GUI.Label(new Rect(x,y,width,66),status,wrap);y+=70;
        float cell=(width-8)/3;
        if(GUI.Button(new Rect(x,y,cell,28),"PERSONA"))ConfigurePerson();
        if(GUI.Button(new Rect(x+cell+4,y,cell,28),"TRAZAR"))Trace();
        if(GUI.Button(new Rect(x+2*(cell+4),y,cell,28),"SALIR")){Shutdown();return 0;}y+=34;
        if(GUI.Button(new Rect(x,y,width,28),cinematic?"DETENER CÁMARA":"SEGUIR LA CARGA · RECORRIDO 18 s")){if(cinematic)StopMovie(false);else StartMovie();}y+=34;
        presentation=GUI.Toggle(new Rect(x,y,width*.53f,24),presentation,"Modo presentación");
        bool wasLive=live;live=GUI.Toggle(new Rect(x+width*.56f,y,width*.44f,24),live,"LIVE X-RAY");y+=28;
        if(wasLive!=live&&live&&!cinematic&&mobile?.CurrentResponse!=null)pendingResponse=mobile.CurrentResponse;
        inside=GUI.Toggle(new Rect(x,y,width,24),inside,"Interior · ocultar copia de acabados");y+=28;
        if(!presentation)
        {
            GUI.Label(new Rect(x,y,width,19),"MÉTRICA INCREMENTAL · una unidad por ranking",small);y+=22;
            int next=GUI.SelectionGrid(new Rect(x,y,width,48),metric,new[]{"ΔN","ΔVy","ΔVz","ΔMy","ΔMz","ΔT","ΔU"},4);y+=54;
            if(next!=metric){metric=next;Rebuild();}
            int nextThreshold=GUI.SelectionGrid(new Rect(x,y,width,48),threshold,new[]{"AUTO","TOP 10%","TOP 20%","I≥0,25","I≥0,50"},3);y+=54;
            if(nextThreshold!=threshold){threshold=nextThreshold;Rebuild();}
            bool constraints=GUI.Toggle(new Rect(x,y,width,24),showConstraints,"MPC como contexto · máximo 12 líneas");y+=28;
            if(constraints!=showConstraints){showConstraints=constraints;Rebuild();}
            GUI.Label(new Rect(x,y,width,42),"AUTO: dominantes + receptores + ruta explicativa. El límite del ranking es 48; conectores necesarios se muestran aparte.",small);y+=46;
        }
        if(result==null)
        {
            GUI.Label(new Rect(x,y,width,95),"1 · PERSONA: aplica carga en una losa.\n2 · TRAZAR: se ilumina la respuesta calculada.\n3 · SEGUIR LA CARGA: recorrido de cámara y pulsos.\n4 · Selecciona una pieza y pregunta POR QUÉ.\nSin respuesta OpenSees no se inventa una red.",wrap);
            return Math.Max(ContentHeight,y-top+100);
        }
        GUI.Label(new Rect(x,y,width,47),$"LOSA {frame.Slab} · P={frame.P:0.###} kN\nΔ{(IncrementalResponseNetwork.Metric)metric} [{IncrementalResponseNetwork.Units((IncrementalResponseNetwork.Metric)metric)}] · seq {frame.Sequence} · {(live&&!cinematic?"LIVE, última respuesta recibida":"RESPUESTA CONGELADA")}",heading);y+=53;
        GUI.Label(new Rect(x,y,width,45),$"Posición CALCULADA: X={frame.X:0.###}, Y={frame.Y:0.###} m. La persona puede estar adelantada mientras OpenSees actualiza.\nIntensidad relativa ≠ daño ni D/C.",small);y+=49;
        filter=GUI.SelectionGrid(new Rect(x,y,width,24),filter,new[]{"TODOS","VIGAS","COLUMNAS","MUROS"},4);y+=29;
        var ranking=result.Ranked.Where(r=>filter==0||filter==1&&r.Edge.Type=="viga"||filter==2&&r.Edge.Type=="columna"||filter==3&&r.Edge.Type=="muro_eq").Take(5).ToList();
        GUI.Label(new Rect(x,y,width,20),"TOP · MISMA MÉTRICA EN TODAS LAS PIEZAS",small);y+=24;
        if(ranking.Count==0){GUI.Label(new Rect(x,y,width,36),"Sin incremento significativo para este tipo/métrica.",wrap);y+=40;}
        foreach(var r in ranking)
        {
            if(GUI.Button(new Rect(x,y,width,38),$"#{r.Rank} {r.Edge.Tag} · {r.Edge.Type}\nΔ={r.Signed:0.######} · I={r.Intensity:0.###} · {r.Category}",row))Select(r.Edge.Id);
            y+=42;
        }
        if(selectedId>0&&GUI.Button(new Rect(x,y,width,26),"¿POR QUÉ SE DESTACA ESTA PIEZA?"))Select(selectedId);y+=31;
        GUI.Label(new Rect(x,y,width,65),BalanceText(),result.BalanceAvailable&&Math.Abs(result.ForceResidual)<=Math.Max(1e-5f,frame.P*1e-5f)?wrap:small);y+=70;
        if(GUI.Button(new Rect(x,y,width,26),"EXPORTAR RED Y TRAZABILIDAD .CSV"))ExportNetwork();y+=31;
        if(!string.IsNullOrEmpty(exportStatus)){GUI.Label(new Rect(x,y,width,50),exportStatus,small);y+=54;}
        GUI.Label(new Rect(x,y,width,42),"Red afectada y conectividad real. Pulsos ilustrativos; no trayectoria exacta ni tiempo de propagación. Primera persona usa una foto congelada.",small);y+=46;
        return Math.Max(ContentHeight,y-top);
    }
    private string BalanceText()
    {
        if(result==null)return "Equilibrio no disponible.";
        string transfer=$"Reparto: ΣPnodal={frame.Transferred:0.######} kN · error={frame.TransferError:0.000000} kN";
        if(!result.BalanceAvailable)return transfer+"\nReacciones: NO EXPORTADAS (no se inventan).";
        bool ok=Math.Abs(result.ForceResidual)<=Math.Max(1e-5f,frame.P*1e-5f);
        var main=frame.Reactions.FirstOrDefault(r=>r.declared&&r.node==result.ReactionSupport);
        return transfer+$"\nΣΔRz={result.ReactionZ:0.######} kN · residuo={result.ForceResidual:0.000000} kN · {(ok?"EQUILIBRIO OK":"REVISAR")}"+
            (main!=null?$"\nMayor |ΔRz|: apoyo N{main.node}, ΔRz={main.fz:0.######} kN":"");
    }
    private void EnsureStyles()
    {
        if(wrap!=null)return;wrap=new GUIStyle(GUI.skin.label){fontSize=11,wordWrap=true};
        heading=new GUIStyle(wrap){fontStyle=FontStyle.Bold};heading.normal.textColor=new Color(.3f,.85f,.95f);
        small=new GUIStyle(wrap){fontSize=10};small.normal.textColor=new Color(.72f,.79f,.86f);
        row=new GUIStyle(GUI.skin.button){fontSize=10,wordWrap=true,alignment=TextAnchor.MiddleLeft};
        hudTexture=new Texture2D(1,1);hudTexture.SetPixel(0,0,new Color(.035f,.055f,.085f,.96f));hudTexture.Apply();
    }
    private void OnGUI()
    {
        if(overlay==null||result==null||!Application.isPlaying)return;EnsureStyles();
        if(!DesktopWalkthrough.IsActive&&mobile!=null&&mobile.IsPanelVisible())return;
        float x=DesktopWalkthrough.IsActive?12:Mathf.Min(360,Screen.width*.27f),y=DesktopWalkthrough.IsActive?82:132;
        float width=Mathf.Min(530,Screen.width-x-14);
        if(width<240)return;
        hudRect=new Rect(x,y,width,108);GUI.DrawTexture(hudRect,hudTexture);
        GUI.Label(new Rect(x+12,y+8,width-24,23),"STRUCTURAL X-RAY · "+stages[stage],heading);
        GUI.Label(new Rect(x+12,y+34,width-24,36),$"Losa {frame.Slab} · P={frame.P:0.###} kN · Δ{result.Metric} · seq {frame.Sequence}\n{(DesktopWalkthrough.IsActive?"FOTO CONGELADA · click con la mira para inspeccionar":cinematic?$"Recorrido {movieClock:0.0}/18 s · tiempo narrativo":live?"LIVE · se muestra la última respuesta calculada":"Respuesta congelada")}",small);
        GUI.Label(new Rect(x+12,y+76,width-110,22),"Pulso explicativo · azul/viga · ámbar/columna · violeta/muro",small);
        if(GUI.Button(new Rect(x+width-88,y+74,76,24),"SALIR")){Shutdown();return;}
        if(DesktopWalkthrough.IsActive)
        {
            var old=GUI.color;GUI.color=new Color(.3f,.9f,1f);GUI.Label(new Rect(Screen.width*.5f-7,Screen.height*.5f-9,20,20),"+");GUI.color=old;
            if(whyWalking)
            {
                var rect=new Rect(Mathf.Max(12,Screen.width-410),Screen.height*.22f,395,Mathf.Min(420,Screen.height*.65f));
                GUI.DrawTexture(rect,hudTexture);GUI.Label(new Rect(rect.x+12,rect.y+8,rect.width-24,22),"¿POR QUÉ ESTA PIEZA?",heading);
                walkingScroll=GUI.BeginScrollView(new Rect(rect.x+8,rect.y+35,rect.width-16,rect.height-78),walkingScroll,new Rect(0,0,rect.width-36,520));
                GUI.Label(new Rect(0,0,rect.width-40,510),graph.Explain(result,selectedId),wrap);GUI.EndScrollView();
                if(GUI.Button(new Rect(rect.x+12,rect.yMax-34,rect.width-24,25),"CERRAR EXPLICACIÓN Y CONTINUAR")){whyWalking=false;GetComponent<DesktopWalkthrough>()?.SetPaused(false);}
            }
        }
    }
    public static bool BlocksPointer(Vector2 point)=>instance!=null&&instance.overlay!=null&&instance.hudRect.Contains(point);
    private void ExportNetwork()
    {
        try
        {
            var text=new StringBuilder("elementTag;id;nodeI;nodeJ;tipo;metrica;unidad;delta_firmado;max_abs;posicion_L;intensidad;ranking;visible;receptor;conector\n");
            string F(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
            foreach(var edge in graph.Members.Values.OrderBy(e=>e.Id))
            {
                result.ById.TryGetValue(edge.Id,out var r);
                text.AppendLine($"{edge.Tag};{edge.Id};{edge.I};{edge.J};{edge.Type};{result.Metric};{IncrementalResponseNetwork.Units(result.Metric)};"+
                    (r?.Available==true?$"{F(r.Signed)};{F(r.Value)};{F(r.Position)};{F(r.Intensity)};{r.Rank}":"N/D;N/D;N/D;N/D;N/D")+
                    $";{result.Visible.Contains(edge.Id)};{result.ReceiverIds.Contains(edge.Id)};{edge.Link}");
            }
            text.AppendLine($"# losa={frame.Slab};posicion={F(frame.X)},{F(frame.Y)};P={F(frame.P)};seq={frame.Sequence}");
            text.AppendLine("# fuente=mobile_slab_worker.py localForce incremental;huella="+frame.SourceHash+";base="+frame.BaseLabel);
            text.AppendLine("# pulsos y ruta son explicativos; ninguna asignacion de porcentajes de fuerza a las aristas.");
            text.AppendLine("# "+BalanceText().Replace('\n',' '));
            text.AppendLine("# reacciones: node;fx;fy;fz [kN];mx;my;mz [kN*m];apoyo_declarado");
            foreach(var r in frame.Reactions??Array.Empty<SupportReactionRecord>())text.AppendLine($"# {r.node};{F(r.fx)};{F(r.fy)};{F(r.fz)};{F(r.mx)};{F(r.my)};{F(r.mz)};{r.declared}");
            Directory.CreateDirectory(Application.persistentDataPath);
            string path=Path.Combine(Application.persistentDataPath,"structural_xray_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")+".csv");
            File.WriteAllText(path,text.ToString(),new UTF8Encoding(true));GUIUtility.systemCopyBuffer=path;exportStatus="CSV guardado y ruta copiada: "+path;
        }
        catch(Exception e){exportStatus="No se pudo exportar: "+e.Message;}
    }
}
