using System.Linq;
using UnityEngine;

public sealed partial class StructuralARController
{
    private readonly MobileStructuralTools engineering=new MobileStructuralTools();
    private int engineeringPage;
    private bool calculationExpanded;
    private void DrawEngineering()
    {
        if(MobileSeismicPlayback.IsActive){GUILayout.Label("Herramientas estáticas pausadas durante el sismo.",WrapStyle());return;}
        engineeringPage=GUILayout.Toolbar(engineeringPage,new[]{"Armadura","Radar","LRFD","Diafragmas"},GUILayout.Height(32));
        if(engineeringPage!=0)engineering.Preview?.Hide();
        if(engineeringPage==3){GUILayout.Label(MobileDiaphragmInfo.Describe(selectedElement,activeCombo),WrapStyle());return;}
        if(engineeringPage==0)
        {
            GUILayout.Label(engineering.Inspect(selectedElement,activeCombo),WrapStyle());
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(engineering.Rotate?"Mz":"My"))engineering.Rotate=!engineering.Rotate;
            if(GUILayout.Button(engineering.Reverse?"Cara −":"Cara +"))engineering.Reverse=!engineering.Reverse;
            if(GUILayout.Button(engineering.UseAxial?"P activa":"P=0"))engineering.UseAxial=!engineering.UseAxial;
            GUILayout.EndHorizontal();
            GUILayout.Label("Corte desde I: "+(engineering.Position*100).ToString("0")+"%");
            engineering.Position=GUILayout.HorizontalSlider(engineering.Position,0,1);
            if(Event.current.type==EventType.Repaint)engineering.UpdatePreview(selectedElement,activeCombo,Time.unscaledDeltaTime);
            if(engineering.Preview?.Texture!=null)GUILayout.Label(engineering.Preview.Texture,GUILayout.Height(165),GUILayout.Width(panelRect.width-45));
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(engineering.Playing?"Pausa":"Play"))engineering.Playing=!engineering.Playing;
            if(GUILayout.Button("Inicio")){engineering.Progress=0;engineering.Playing=false;}
            if(GUILayout.Button(engineering.StaticPreview?"Deformada":"Curva sección")){engineering.StaticPreview=!engineering.StaticPreview;engineering.Progress=0;}
            GUILayout.EndHorizontal();
            engineering.Progress=GUILayout.HorizontalSlider(engineering.Progress,0,1);
            GUILayout.Label("Giro / zoom");engineering.Yaw=GUILayout.HorizontalSlider(engineering.Yaw,-180,180);
            engineering.Zoom=GUILayout.HorizontalSlider(engineering.Zoom,.65f,1.6f);
            engineering.Cracks=GUILayout.Toggle(engineering.Cracks,"Fisuras didácticas");
            GUILayout.Label(engineering.UpdatePreview(selectedElement,activeCombo,0),WrapStyle());
            if(GUILayout.Button(new[]{"M–Φ → P–My nominal","Nominal → P–My diseño","Diseño → M–Φ"}[engineering.PlotMode]))engineering.PlotMode=(engineering.PlotMode+1)%3;
            if(engineering.Plot!=null)GUILayout.Label(engineering.Plot,GUILayout.Height(150),GUILayout.Width(panelRect.width-45));
            GUILayout.Label(engineering.PlotMode==0?"X: Φ [1/m] · Y: M [kN·m] · punto del ensayo P≈0":engineering.PlotMode==1?"X: |My| [kN·m] · Y: P [kN] · punto demanda. Curva nominal exportada; no espejo de ramas asimétricas.":"X: My [kN·m] · Y: P [kN] · φ variable, dos ramas independientes. Diseño P–My parcial.",WrapStyle());
            if(GUILayout.Button("Cálculo paso a paso"))calculationExpanded=!calculationExpanded;
            if(calculationExpanded)GUILayout.Label(engineering.Calculation(selectedElement,activeCombo),WrapStyle());
        }
        else if(engineeringPage==1)
        {
            if(activeCombo.StartsWith("LRFD_")){GUILayout.Label("Radar nominal pausado en LRFD. Elige C1/C2/C3.",WrapStyle());return;}
            engineering.Metric=GUILayout.Toolbar(engineering.Metric,new[]{"D/C","M","V","N−","N+"});
            GUILayout.BeginHorizontal();foreach(string c in new[]{"C1","C2","C3","ENV"})if(GUILayout.Button(c))engineering.RadarCase=c;GUILayout.EndHorizontal();
            GUILayout.Label("Top 5 del sector colocado · "+engineering.RadarCase+"\nD/C nominal P–My parcial; M/V/N no indican seguridad.",WrapStyle());
            foreach(var row in engineering.Rank(placements.Select(p=>p.Element)).Take(5))
                if(GUILayout.Button(row.Element.elementTag+" · ID "+row.Element.id+" · "+row.Reading.Value.ToString("0.##")+"\n"+row.Combo+" · "+(row.Reading.Position*100).ToString("0")+"% I"))
                {SelectPlacement(placements.First(p=>p.Element.id==row.Element.id));UnityData.UseBaseCaseFactors=false;SetCombination(row.Combo);
                    SetActiveResult(engineering.Metric==1?(Mathf.Abs(row.Reading.My)>=Mathf.Abs(row.Reading.Mz)?"My":"Mz"):
                        engineering.Metric==2?(Mathf.Abs(row.Reading.Vy)>=Mathf.Abs(row.Reading.Vz)?"Vy":"Vz"):"N");}
        }
        else
        {
            DrawMobileLrfdInput(engineering.Lrfd);
            GUILayout.Label(engineering.Lrfd.Status,WrapStyle());
            if(!engineering.Lrfd.Current){if(activeCombo.StartsWith("LRFD_")){UnityData.UseBaseCaseFactors=false;SetCombination("C1");}GUILayout.Label("Evalúa el escenario antes de aplicar una combinación.",WrapStyle());return;}
            var rows=engineering.Lrfd.Rows(selectedElement);
            for(int i=0;i<rows.Length;i++)
            {
                var row=rows[i];if(row==null){GUILayout.Label("U"+(i+1)+": sin respuesta");continue;}
                if(GUILayout.Button("U"+(i+1)+" · "+(row.HasCapacity?"D/C diseño "+row.Ratio.ToString("0.##"):"My "+row.Forces.My.ToString("0.##"))+"\nP "+(-row.Forces.N).ToString("0.##")+" kN · My "+row.Forces.My.ToString("0.##")+" kN·m · "+(row.Position*100).ToString("0")+"% I"))
                {UnityData.UseBaseCaseFactors=false;currentPlacement.Compare=false;SetCombination(row.Variant.name);SetActiveResult("My");}
            }
            GUILayout.Label("φ variable · P–My uniaxial de diseño. Sin corte, torsión, esbeltez ni biaxialidad. No certifica colapso.",WrapStyle());
        }
    }
    private void DrawMobileLrfdInput(MobileLrfd lab)
    {
        var s=lab.Input;
        GUILayout.Label("Intensidades físicas · escenario LRFD independiente",WrapStyle());
        MobileSlider("D factor",ref s.d,2);MobileSlider("L factor",ref s.l,2);MobileSlider("Lr kN/m²",ref s.roof,2);
        MobileSlider("Nieve cm",ref s.snowDepth,.6f,100);MobileSlider("Densidad kg/m³",ref s.snowDensity,600);
        MobileSlider("Agua mm",ref s.waterDepth,.2f,1000);MobileSlider("Viento m/s",ref s.windSpeed,60);
        MobileSlider("Cp",ref s.windCoefficient,2);MobileSlider("Dirección grados",ref s.windAngle,360);MobileSlider("E factor ±EX/±EY",ref s.e,2);
        if(GUILayout.Button("Evaluar U1–U7 precalculadas")){lab.Evaluate();if(activeCombo.StartsWith("LRFD_")){UnityData.UseBaseCaseFactors=false;SetCombination("C1");}}
        GUILayout.Label("Lr/S/R son alternativas. El teléfono superpone unidades OpenSees; no ejecuta un reanálisis.",WrapStyle());
    }
    private static void MobileSlider(string name,ref float value,float max,float display=1)
    {GUILayout.Label(name+": "+(value*display).ToString("0.##"));value=GUILayout.HorizontalSlider(value,0,max);}
    private void OnDisable(){engineering.Dispose();}
    internal void RestoreEngineeringCases()
    {if(engineering.Lrfd.Current)foreach(var variant in engineering.Lrfd.Dataset.variants)UnityData.RegisterLrfdCase(variant);}
}
