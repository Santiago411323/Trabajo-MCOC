using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Inspector móvil separado de la colocación AR y de la geometría/calculadores.
public sealed class MobileStructuralTools : IDisposable
{
    public readonly MobileLrfd Lrfd=new MobileLrfd();
    public bool Reverse,Rotate,UseAxial=true,Playing,StaticPreview,Cracks;
    public float Position=.5f,Progress,Yaw=25,Zoom=1;
    public int Metric=1,PlotMode;
    public string RadarCase="C1";
    public MemberRebarPreview Preview {get;private set;}
    private ElementSelectable proxy;
    private Texture2D plot;
    private float nextPlot;
    private int lastPlotMode=-1;
    private string designKey;
    private LrfdDesignCapacity plotCapacity;
    public Texture Plot=>plot;
    public static string SectionId(ElementData e)
    {
        if(e==null)return null;
        string key=string.IsNullOrEmpty(e.sectionId)?e.seccion:e.sectionId;
        if(UnityData.GetMaterial(key+"_FIBER")!=null && UnityData.GetPMCurve(key+"_FIBER")!=null)return key+"_FIBER";
        if(UnityData.GetMaterial(key)!=null)return key;
        return UnityData.Structure?.p1l4?.sectionMaterials?.FirstOrDefault(m=>m.sectionId==key || m.sectionId=="V_"+key || m.sectionId=="C_"+key)?.sectionId;
    }
    public string Inspect(ElementData e,string combo)
    {
        if(e==null || (e.type!="viga"&&e.type!="columna"))return "Seleccione una viga o columna.";
        var m=UnityData.GetMaterial(SectionId(e));
        if(m==null)return "Armadura de esta sección no disponible.";
        bool have=UnityData.TryGetSectionForces(e.id,combo,Position,out var f);
        double axial=e.type=="columna"&&UseAxial?(have?-f.N:double.NaN):0;
        var r=ReinforcementAssessment.Evaluate(m,e.type=="columna",axial,Reverse,Rotate);
        string text=e.elementTag+" · ID "+e.id+"\n"+combo+" · x="+(Position*100).ToString("0")+"% desde I";
        if(have)text+=$"\nP={-f.N:0.##} kN · My={f.My:0.##} · Mz={f.Mz:0.##} kN·m\nVy={f.Vy:0.##} · Vz={f.Vz:0.##} kN";
        text+="\n"+MobileSeismicPlayback.Reinforcement(e);
        if(!r.Valid)return text+"\nVerificación pendiente: "+r.Reason;
        text+=$"\n{(Rotate?"Mz":"My")} {(Reverse?"−":"+")} · P ref={axial:0.##} kN\nAs={r.As:0.#} mm² · ρ total={r.RhoTotal*100:0.##}%\nMínimo: {(r.MinimumOK?"cumple":"no cumple")} · "+
            (r.IsColumn?"máximo: "+(r.MaximumOK?"cumple":"no cumple"):"máximo: referencia de fila extrema");
        if(r.UltimateAvailable)text+=$"\nMn={r.Mn:0.##} kN·m · φ={r.Phi:0.###}\nc={r.C:0.#} mm · εt={r.EpsT:0.#####}\n{r.Failure}";
        else text+="\nEquilibrio de sección pendiente: "+r.Reason;
        return text+"\nACI 318-08 · sección uniaxial de referencia\nNo verifica corte, torsión, biaxialidad ni colapso.";
    }
    public string Calculation(ElementData e,string combo)
    {
        var m=UnityData.GetMaterial(SectionId(e));if(m==null)return "Sin distribución de acero.";
        bool have=UnityData.TryGetSectionForces(e.id,combo,Position,out var f);
        var r=ReinforcementAssessment.Evaluate(m,e.type=="columna",e.type=="columna"&&UseAxial?(have?-f.N:double.NaN):0,Reverse,Rotate);
        if(!r.Valid)return r.Reason;
        return $"As=n·π·Ø²/4={r.As:0.#} mm²\nAg=b·h={r.Ag:0.#} mm² · d={r.D:0.#} mm\nAs fila={r.AsTensionRow:0.#} · As,min={r.AsMin:0.#} mm²\nAs,max ref={r.AsMax:0.#} mm²\nβ1={r.Beta1:0.###} · εy={r.EpsY:0.#####}\nResidual equilibrio={r.Residual:0.#####} kN\nε(y)=ε0−Φy · compresión positiva.";
    }
    public string UpdatePreview(ElementData e,string combo,float dt)
    {
        var section=UnityData.GetMaterial(SectionId(e));
        if(section==null || e==null || !UnityData.TryGetFrameGeometry(e.id,out var geometry)){Preview?.Hide();return "Vista 3D no disponible.";}
        if(proxy==null){var go=new GameObject("Referencia aislada de inspector móvil"){hideFlags=HideFlags.HideAndDontSave};proxy=go.AddComponent<ElementSelectable>();proxy.enabled=false;}
        proxy.data=e;proxy.startPoint=Vector3.zero;proxy.endPoint=Vector3.right*(float)geometry.Length;
        Preview??=new MemberRebarPreview();Preview.Bind(proxy,section);
        if(Playing){Progress=Mathf.Min(1,Progress+dt*.12f);if(Progress>=1)Playing=false;}
        var state=MemberMaterialPlayback.Sample(section,UnityData.GetPMCurve(SectionId(e)),Progress);
        Func<float,Vector3> displacement=t=>Vector3.zero;
        if(StaticPreview)
        {
            var ui=UnityData.GetNodeDisplacement(combo,e.nodeI);var uj=UnityData.GetNodeDisplacement(combo,e.nodeJ);
            var ri=Rotation(combo,e.nodeI);var rj=Rotation(combo,e.nodeJ);
            Vector3 direction=new Vector3((float)geometry.X[0],(float)geometry.X[2],(float)geometry.X[1]);
            Vector3 up=Vector3.ProjectOnPlane(Vector3.up,direction).normalized;
            if(up.sqrMagnitude<.01f)up=Vector3.forward;
            Vector3 side=Vector3.Cross(direction.normalized,up).normalized;
            displacement=t=>{var u=MemberPreviewKinematics.Interpolate(ui,uj,ri,rj,direction,(float)geometry.Length,t,UnityData.MobileDisplacements.Count==0)*Progress*50;
                return new Vector3(Vector3.Dot(u,direction.normalized),Vector3.Dot(u,up),Vector3.Dot(u,side));};
        }
        Preview.UpdateGeometry(displacement,StaticPreview?0:state.Available?state.Phi:0,StaticPreview?0:10,StaticPreview?null:state,Position,Cracks&&!StaticPreview);
        Preview.Show(Yaw,18,Zoom);
        UpdatePlot(e,combo,state);
        return StaticPreview?"Deformada estática OpenSees · escala visual 50x\nInterpolación gráfica, sin análisis dinámico.":
            state.Available?$"Curva de sección P≈0 · arco ilustrativo 10x\nM={state.Moment:0.##} kN·m · Φ={state.Phi:0.#####} 1/m\n{state.Stage}\nVerde tracción · naranja compresión · rojo fluencia":"Sin curva M–Φ exportada.";
    }
    private void UpdatePlot(ElementData e,string combo,MemberMaterialPlayback.State state)
    {
        if(plot!=null && PlotMode==lastPlotMode && Time.unscaledTime<nextPlot)return;nextPlot=Time.unscaledTime+.2f;lastPlotMode=PlotMode;
        const int w=300,h=170;if(plot==null)plot=new Texture2D(w,h,TextureFormat.RGBA32,false);
        var pixels=Enumerable.Repeat(new Color(.03f,.05f,.08f),w*h).ToArray();
        var curve=UnityData.GetPMCurve(SectionId(e));var points=new List<Vector2>();Vector2 demand=Vector2.zero;
        if(PlotMode==0 && curve?.momentCurvature!=null)
        {foreach(var p in curve.momentCurvature)if(p!=null)points.Add(new Vector2(p.phi_1_m,p.M_kN_m));demand=new Vector2(state.Phi,state.Moment);}
        else if(PlotMode==1 && curve?.points!=null)
        {foreach(var p in curve.points)if(p!=null)points.Add(new Vector2(p.M_kN_m,p.P_kN));if(UnityData.TryGetSectionForces(e.id,combo,Position,out var f))demand=new Vector2(Mathf.Abs(f.My),-f.N);}
        else if(PlotMode==2)
        {
            var material=UnityData.GetMaterial(SectionId(e));string key=JsonUtility.ToJson(material)+e.type;
            if(key!=designKey){plotCapacity=new LrfdDesignCapacity(material,e.type=="columna");designKey=key;}
            if(plotCapacity.Available)
            {
                foreach(var p in plotCapacity.Positive.points)points.Add(new Vector2(p.M_kN_m,p.P_kN));
                // Separar las ramas para evitar una línea ficticia a través del dominio.
                points.Add(new Vector2(float.NaN,float.NaN));
                foreach(var p in plotCapacity.Negative.points)points.Add(new Vector2(-p.M_kN_m,p.P_kN));
                if(UnityData.TryGetSectionForces(e.id,combo,Position,out var f))demand=new Vector2(f.My,-f.N);
            }
        }
        if(points.Count>1)
        {
            var valid=points.Where(p=>!float.IsNaN(p.x)).ToArray();
            float xmin=Mathf.Min(0,valid.Min(p=>p.x)),xmax=Mathf.Max(.001f,valid.Max(p=>p.x));
            float ymin=Mathf.Min(0,valid.Min(p=>p.y)),ymax=Mathf.Max(ymin+.001f,valid.Max(p=>p.y));
            xmin=Mathf.Min(xmin,demand.x);xmax=Mathf.Max(xmax,demand.x);ymin=Mathf.Min(ymin,demand.y);ymax=Mathf.Max(ymax,demand.y);
            Func<Vector2,Vector2Int> map=p=>new Vector2Int(Mathf.RoundToInt(12+(p.x-xmin)/(xmax-xmin)*(w-24)),Mathf.RoundToInt(12+(p.y-ymin)/(ymax-ymin)*(h-24)));
            for(int i=1;i<points.Count;i++)if(!float.IsNaN(points[i-1].x)&&!float.IsNaN(points[i].x))Line(pixels,w,h,map(points[i-1]),map(points[i]),Color.cyan);
            var dot=map(demand);for(int x=-3;x<=3;x++)for(int y=-3;y<=3;y++)if(dot.x+x>=0&&dot.x+x<w&&dot.y+y>=0&&dot.y+y<h)pixels[(dot.y+y)*w+dot.x+x]=Color.yellow;
        }
        plot.SetPixels(pixels);plot.Apply(false);
    }
    private static void Line(Color[] pixels,int w,int h,Vector2Int a,Vector2Int b,Color color)
    {int steps=Mathf.Max(Mathf.Abs(b.x-a.x),Mathf.Abs(b.y-a.y));for(int i=0;i<=steps;i++){var p=Vector2.Lerp(a,b,steps==0?0:i/(float)steps);int x=Mathf.RoundToInt(p.x),y=Mathf.RoundToInt(p.y);if(x>=0&&x<w&&y>=0&&y<h)pixels[y*w+x]=color;}}
    private static Vector3 Rotation(string combo,int node)
    {
        if(UnityData.UseBaseCaseFactors && !combo.StartsWith("LRFD_"))
        {
            string[] cases={"G","Q","EX","EY"};float[] factors={UnityData.FactorG,UnityData.FactorQ,UnityData.FactorEX,UnityData.FactorEY};
            Vector3 total=Vector3.zero;for(int i=0;i<4;i++)total+=BaseRotation(cases[i],node)*factors[i];return total;
        }
        return BaseRotation(combo,node);
    }
    private static Vector3 BaseRotation(string combo,int node)
    {if(!UnityData.DisplacementsByCombo.TryGetValue(combo,out var rows))return Vector3.zero;var r=rows.FirstOrDefault(d=>d.node==node);return r==null?Vector3.zero:new Vector3(r.rx,r.ry,r.rz);}
    public sealed class RadarRow {public ElementData Element;public DemandRadarRanking.Reading Reading;public string Combo;}
    public List<RadarRow> Rank(IEnumerable<ElementData> elements)
    {
        var result=new List<RadarRow>();if(MobileSeismicPlayback.IsActive || RadarCase.StartsWith("LRFD_"))return result;
        foreach(var e in elements.GroupBy(e=>e.id).Select(g=>g.First()))
        {
            if((e.type!="viga"&&e.type!="columna") || !UnityData.TryGetFrameGeometry(e.id,out var frame))continue;
            var section=SectionId(e);var m=UnityData.GetMaterial(section);
            if(Metric==0 && m!=null && m.topBars!=m.bottomBars)continue;
            var curve=UnityData.GetPMCurve(section);RadarRow best=null;bool complete=true;
            foreach(var c in RadarCase=="ENV"?new[]{"C1","C2","C3"}:new[]{RadarCase})
            {
                var reading=DemandRadarRanking.Evaluate(UnityData.GetElementForcesForComparison(c,e.id),frame.Length,curve,(DemandRadarRanking.Metric)Metric);
                if(!reading.Available){complete=false;break;}
                if(best==null || reading.Value>best.Reading.Value)best=new RadarRow{Element=e,Reading=reading,Combo=c};
            }
            if(complete&&best!=null)result.Add(best);
        }
        return result.OrderByDescending(r=>r.Reading.Value).ThenBy(r=>r.Element.id).ToList();
    }
    public void Dispose(){Preview?.Dispose();Preview=null;if(proxy!=null)UnityEngine.Object.Destroy(proxy.gameObject);if(plot!=null)UnityEngine.Object.Destroy(plot);plot=null;proxy=null;}
}
