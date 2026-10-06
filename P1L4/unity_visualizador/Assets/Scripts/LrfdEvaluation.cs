using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using Process=System.Diagnostics.Process;

public sealed partial class LrfdLaboratory
{
    private sealed class LrfdRow
    {
        public LrfdVariant Variant;public float P,My,Mz,Vy,Vz,Ratio,Position,Capacity;
        public bool Available,CapacityAvailable;
    }
    private Process lrfdProcess;
    private readonly ConcurrentQueue<string> lrfdMessages=new ConcurrentQueue<string>();
    private LrfdDataset lrfdDataset;
    private LrfdRow[] lrfdRows;
    private ElementSelectable lrfdElement;
    private LrfdDesignCapacity lrfdCapacity;
    private int lrfdGoverning,lrfdChosen,evaluationTab;
    private bool lrfdPlaying;
    private float lrfdNext;
    private string lrfdStatus;
    private GUIStyle lrfdRowStyle;
    private DateTime lrfdModelTime;
    private string LrfdDirectory=>Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","lrfd_results"));
    private string LrfdOutput=>Path.Combine(LrfdDirectory,"resultados_unity.json");
    private LrfdInput CurrentInput()=>new LrfdInput{d=d,l=l,roof=roof,snowDepth=snowDepth,waterDepth=waterDepth,snowDensity=snowDensity,
        windSpeed=windSpeed,windCoefficient=windCoefficient,windAngle=windAngle,e=e};
    private bool LrfdCurrent=>lrfdDataset!=null && JsonUtility.ToJson(lrfdDataset.scenario)==JsonUtility.ToJson(CurrentInput()) &&
        !StructuralModelEditor.ResultsStale && File.GetLastWriteTimeUtc(DesktopModelFile.ActivePath)==lrfdModelTime;
    private void UpdateLrfdEvaluation()
    {
        while(lrfdMessages.TryDequeue(out var message))lrfdStatus=message;
        if(lrfdProcess!=null && lrfdProcess.HasExited)
        {
            lrfdProcess.WaitForExit();int code=lrfdProcess.ExitCode;lrfdProcess.Dispose();lrfdProcess=null;
            if(code==0)LoadLrfdDataset();else lrfdStatus="Análisis rechazado: "+lrfdStatus;
        }
        if(lrfdPlaying && open && Time.unscaledTime>=lrfdNext)
        {
            if(!LrfdCurrent || lrfdRows==null){lrfdPlaying=false;return;}
            lrfdChosen=(lrfdChosen+1)%7;ApplyLrfdRow(lrfdChosen);lrfdNext=Time.unscaledTime+4;
        }
    }
    private void StartLrfdAnalysis()
    {
        if(lrfdProcess!=null)return;
        if(!Application.isEditor && Application.isMobilePlatform){lrfdStatus="El análisis independiente se ejecuta en Editor/Windows.";return;}
        if(StructuralModelEditor.ResultsStale){lrfdStatus="Actualice primero los cambios pendientes de sección.";return;}
        try
        {
            accumulate=false;lrfdPlaying=false;Directory.CreateDirectory(LrfdDirectory);
            string scenario=Path.Combine(LrfdDirectory,"escenario_unity.json");File.WriteAllText(scenario,JsonUtility.ToJson(CurrentInput()));
            string basePath=Path.GetFullPath(Path.Combine(Application.dataPath,"..",".."));
            string runtime=Path.Combine(basePath,"seismic","runtime.local.json");
            string executable=File.Exists(runtime)?JsonUtility.FromJson<SeismicPythonRuntime>(File.ReadAllText(runtime)).executable:"python";
            var info=new System.Diagnostics.ProcessStartInfo{FileName=executable,UseShellExecute=false,CreateNoWindow=true,
                WorkingDirectory=basePath,RedirectStandardOutput=true,RedirectStandardError=true,
                Arguments="-B \""+Path.Combine(basePath,"lrfd_analysis.py")+"\" --model \""+DesktopModelFile.ActivePath+
                    "\" --scenario \""+scenario+"\" --output \""+LrfdOutput+"\""};
            lrfdProcess=new Process{StartInfo=info};
            lrfdProcess.OutputDataReceived+=(s,args)=>{if(!string.IsNullOrEmpty(args.Data))lrfdMessages.Enqueue(args.Data);};
            lrfdProcess.ErrorDataReceived+=(s,args)=>{if(!string.IsNullOrEmpty(args.Data))lrfdMessages.Enqueue(args.Data);};
            lrfdProcess.Start();lrfdProcess.BeginOutputReadLine();lrfdProcess.BeginErrorReadLine();
            lrfdStatus="OpenSees: preparando casos independientes y U1–U7…";
        }
        catch(Exception ex){lrfdProcess?.Dispose();lrfdProcess=null;lrfdStatus=ex.Message;}
    }
    private void LoadLrfdDataset()
    {
        try
        {
            var data=JsonUtility.FromJson<LrfdDataset>(File.ReadAllText(LrfdOutput));
            using(var sha=SHA256.Create())
            {
                string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(DesktopModelFile.ActivePath))).Replace("-","").ToLowerInvariant();
                if(hash!=data.modelHash)throw new InvalidDataException("El modelo cambió; vuelva a evaluar.");
            }
            if(data.variants==null || data.variants.Length==0)throw new InvalidDataException("No hay variantes calculadas.");
            foreach(var variant in data.variants)UnityData.RegisterLrfdCase(variant);
            lrfdDataset=data;lrfdElement=null;lrfdRows=null;
            lrfdModelTime=File.GetLastWriteTimeUtc(DesktopModelFile.ActivePath);
            lrfdStatus="U1–U7 disponibles · error máximo de equilibrio "+data.equilibriumError.ToString("0.000000")+" kN.";
        }
        catch(Exception ex){lrfdDataset=null;lrfdStatus="No se cargó: "+ex.Message;}
    }
    private void RebuildLrfdRows(ElementSelectable element)
    {
        lrfdElement=element;lrfdRows=new LrfdRow[7];lrfdGoverning=0;
        var material=UnityData.GetMaterial(element.pmSectionId);
        lrfdCapacity=new LrfdDesignCapacity(material,element.data.type=="columna");
        if(!UnityData.TryGetFrameGeometry(element.data.id,out var frame))return;
        float worst=-1;
        for(int u=1;u<=7;u++)
        {
            var row=new LrfdRow();float score=-1;
            foreach(var variant in lrfdDataset.variants)
            {
                if(variant.u!=u)continue;
                foreach(var record in variant.forces)
                {
                    if(record.id!=element.data.id || !FrameForces.IsValid(record.f))continue;
                    for(int i=0;i<=40;i++)
                    {
                        float t=i/40f;var f=FrameForces.Evaluate(record.f,frame.Length,t);
                        float ratio=lrfdCapacity.Available?lrfdCapacity.Ratio(-f.N,f.My):float.NaN;
                        float candidate=lrfdCapacity.Available?ratio:Mathf.Abs(f.My);
                        if(candidate>score)
                        {
                            score=candidate;row.Available=true;row.CapacityAvailable=lrfdCapacity.Available;
                            row.Variant=variant;row.P=-f.N;row.My=f.My;row.Mz=f.Mz;row.Vy=f.Vy;row.Vz=f.Vz;
                            row.Ratio=ratio;row.Position=t;row.Capacity=lrfdCapacity.Available?lrfdCapacity.MomentAvailable(row.P,row.My):0;
                        }
                    }
                    break;
                }
            }
            lrfdRows[u-1]=row;if(score>worst){worst=score;lrfdGoverning=u-1;}
        }
        lrfdChosen=lrfdGoverning;
    }
    private void ApplyLrfdRow(int index)
    {
        if(!LrfdCurrent || lrfdRows==null || !lrfdRows[index].Available)return;
        var row=lrfdRows[index];lrfdChosen=index;
        viewer.ActivateLrfdCase(row.Variant.name);
        ElementResultsPanel.OpenFromRadar(lrfdElement,3,row.Position);
    }
    private float DrawLrfdEvaluation(float x,float y,float width)
    {
        float start=y;
        if(lrfdRowStyle==null)lrfdRowStyle=new GUIStyle(GUI.skin.button){fontSize=11,wordWrap=true};
        Label(ref y,x,width,"U1–U7 · DEMANDA Y CAPACIDAD DE DISEÑO",true);
        bool enabled=GUI.enabled;GUI.enabled=enabled && lrfdProcess==null && !SeismicPlaybackController.IsActive;
        if(GUI.Button(new Rect(x,y,width,28),lrfdProcess!=null?"OPENSEES ANALIZANDO…":"EVALUAR LAS 7 COMBINACIONES"))StartLrfdAnalysis();y+=35;
        if(GUI.Button(new Rect(x,y,width,25),"CARGAR EVALUACIÓN GUARDADA"))LoadLrfdDataset();y+=32;GUI.enabled=enabled;
        if(!string.IsNullOrEmpty(lrfdStatus))Label(ref y,x,width,lrfdStatus);
        if(lrfdDataset==null){Label(ref y,x,width,"Evalúe el escenario. Se generan resultados independientes; los análisis existentes permanecen intactos.");return y-start;}
        if(!LrfdCurrent){Label(ref y,x,width,"ESCENARIO CAMBIADO: vuelva a evaluar antes de aplicar resultados. La acumulación se pausa al evaluar.");return y-start;}
        var picker=FindFirstObjectByType<ElementPicker>();var selected=picker!=null?picker.Selected:null;
        if(selected==null || selected.data==null || selected.isWall)
        {Label(ref y,x,width,"Seleccione una viga o columna del edificio para ver sus siete resultados.");return y-start;}
        if(selected!=lrfdElement || lrfdRows==null)RebuildLrfdRows(selected);
        Label(ref y,x,width,$"{selected.data.elementTag} · ID {selected.data.id}\nP–My uniaxial de referencia · Whitney · estribos · ϕ según εt. No incluye corte, torsión, biaxialidad ni esbeltez.");
        Label(ref y,x,width,(lrfdCapacity.Available?"Mayor D/C: ":"Mayor demanda My (sin capacidad): ")+"U"+(lrfdGoverning+1),true);
        for(int i=0;i<7;i++)
        {
            var row=lrfdRows[i];if(row==null || !row.Available){Label(ref y,x,width,"U"+(i+1)+": pendiente · faltan fuerzas.");continue;}
            bool exceed=row.CapacityAvailable && row.Ratio>=1;
            string ratio=!row.CapacityAvailable?"PENDIENTE":row.Ratio>=UnityData.OutOfCurveRatio?"FUERA ENVOLVENTE":row.Ratio.ToString("0.###");
            string state=!row.CapacityAvailable?"SIN CAPACIDAD":exceed?"EXCEDE P–My":row.Ratio>=.8f?"CERCA CAPACIDAD":"DENTRO P–My";
            Color old=GUI.backgroundColor;GUI.backgroundColor=exceed?new Color(1,.35f,.3f):i==lrfdGoverning?new Color(1,.75f,.2f):new Color(.3f,.8f,.7f);
            string detail=$"U{i+1} · {state} · D/C {ratio}\nP {row.P:0.##} kN · My {row.My:0.##} kN·m\nMz {row.Mz:0.##} kN·m · Vy {row.Vy:0.##} · Vz {row.Vz:0.##} kN\nCrítico en {row.Position*100:0.#}% desde I";
            float rowHeight=Mathf.Max(80,lrfdRowStyle.CalcHeight(new GUIContent(detail),width));
            if(GUI.Button(new Rect(x,y,width,rowHeight),detail,lrfdRowStyle))ApplyLrfdRow(i);
            GUI.backgroundColor=old;y+=rowHeight+6;
        }
        if(GUI.Button(new Rect(x,y,width,27),"MOSTRAR COMBINACIÓN GOBERNANTE"))ApplyLrfdRow(lrfdGoverning);y+=34;
        if(GUI.Button(new Rect(x,y,width,27),lrfdPlaying?"PAUSAR RECORRIDO U1–U7":"RECORRER U1–U7"))
        {lrfdPlaying=!lrfdPlaying;lrfdChosen=0;if(lrfdPlaying){ApplyLrfdRow(0);lrfdNext=Time.unscaledTime+4;}}y+=34;
        var chosen=lrfdRows[lrfdChosen];
        if(chosen!=null && chosen.Available)
        {
            Label(ref y,x,width,chosen.Variant.label);
            Label(ref y,x,width,$"En P={chosen.P:0.##} kN: |My demanda|={Mathf.Abs(chosen.My):0.##}; capacidad de diseño My={chosen.Capacity:0.##} kN·m. Interpolación de la envolvente reducida, no un único ϕ=0,9.");
            if(lrfdCapacity.Available){DrawDesignDiagram(new Rect(x+25,y+20,width-38,155),chosen);y+=195;}
        }
        Label(ref y,x,width,"EXCEDE = criterio P–My superado; no demuestra fractura o colapso. Las otras verificaciones siguen pendientes. El modelo global es lineal.");
        Label(ref y,x,width,lrfdDataset.notes);
        return y-start+12;
    }
    private void DrawDesignDiagram(Rect plot,LrfdRow row)
    {
        float pmin=0,pmax=row.P,mmax=Mathf.Abs(row.My);
        foreach(var curve in new[]{lrfdCapacity.Positive,lrfdCapacity.Negative})foreach(var point in curve.points)
        {pmin=Mathf.Min(pmin,point.P_kN);pmax=Mathf.Max(pmax,point.P_kN);mmax=Mathf.Max(mmax,point.M_kN_m);}
        pmin=Mathf.Min(pmin,row.P);pmax=Mathf.Max(pmax,pmin+1);mmax=Mathf.Max(1,mmax)*1.12f;
        Func<float,float,Vector2> map=(p,m)=>new Vector2(plot.center.x+m/mmax*plot.width*.48f,plot.yMax-(p-pmin)/(pmax-pmin)*plot.height);
        GUI.Box(plot,GUIContent.none);
        PmLine(map(pmin,0),map(pmax,0),new Color(.5f,.6f,.7f));
        for(int side=0;side<2;side++)
        {
            var points=(side==0?lrfdCapacity.Positive:lrfdCapacity.Negative).points;
            for(int i=1;i<points.Length;i++)PmLine(map(points[i-1].P_kN,(side==0?1:-1)*points[i-1].M_kN_m),map(points[i].P_kN,(side==0?1:-1)*points[i].M_kN_m),Color.cyan);
        }
        var dot=map(row.P,row.My);Color old=GUI.color;GUI.color=row.Ratio>=1?Color.red:Color.green;
        GUI.DrawTexture(new Rect(dot.x-5,dot.y-5,10,10),Texture2D.whiteTexture);GUI.color=old;
        GUI.Label(new Rect(plot.x,plot.y-20,plot.width,20),"P–My DE DISEÑO · punto de demanda",text);
        GUI.Label(new Rect(plot.x,plot.yMax+3,plot.width,20),$"My ±{mmax:0.#} kN·m · P [{pmin:0.#}, {pmax:0.#}] kN",text);
    }
    private static void PmLine(Vector2 a,Vector2 b,Color color)
    {
        if(Event.current.type!=EventType.Repaint)return;
        Vector2 delta=b-a;if(delta.sqrMagnitude<.000001f)return;
        Matrix4x4 matrix=GUI.matrix;Color old=GUI.color;GUI.color=color;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,a);
        GUI.DrawTexture(new Rect(a.x,a.y,delta.magnitude,1.7f),Texture2D.whiteTexture);GUI.matrix=matrix;GUI.color=old;
    }
}
