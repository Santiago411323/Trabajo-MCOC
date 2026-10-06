using UnityEngine;

public sealed partial class LrfdLaboratory : MonoBehaviour
{
    private StructureViewer viewer;
    private StructureData model;
    private LrfdLoadVisuals visuals;
    private bool open,accumulate,accompanyingWind;
    private bool showZeroSymbols=true,interior;
    private readonly bool[] shown={true,true,true,true,true,true,true};
    private float d=1,l=1,roof,snowDepth,waterDepth,snowDensity=300,windSpeed,windCoefficient=1,windAngle,e;
    private float precipitationRate=50,weatherTimeScale=120;
    private int precipitation,combination,alternative;
    private float contentHeight=1500;
    private GUIStyle heading,text;
    private string status;
    public float ContentHeight=>open?contentHeight:0;
    public bool IsOpen=>open;
    public void Initialize(StructureViewer source,StructureData data)
    {viewer=source;model=data;visuals?.Dispose();visuals=null;}
    public void Toggle(){open=!open;if(open && visuals==null && model!=null){visuals=new LrfdLoadVisuals();visuals.Build(viewer,model);}if(!open){viewer?.SetLaboratoryInterior(false);interior=false;}}
    private void OnDisable(){visuals?.Dispose();visuals=null;open=false;viewer?.SetLaboratoryInterior(false);lrfdPlaying=false;lrfdProcess?.Dispose();lrfdProcess=null;}
    private void Update()
    {
        UpdateLrfdEvaluation();
        bool visible=open && !SeismicPlaybackController.IsActive;
        if(visible && accumulate)
        {
            float depth=LrfdScenario.RetainedDepth(precipitationRate,Time.unscaledDeltaTime,weatherTimeScale);
            if(precipitation==1)waterDepth=Mathf.Min(.2f,waterDepth+depth);
            if(precipitation==2)snowDepth=Mathf.Min(.6f,snowDepth+depth);
        }
        visuals?.Update(visible,precipitation,snowDepth,waterDepth,windSpeed,windAngle,
            new[]{d,l,roof,LrfdScenario.SnowPressure(snowDepth,snowDensity),LrfdScenario.WaterPressure(waterDepth),LrfdScenario.WindPressure(windSpeed,windCoefficient),e},precipitationRate,shown,showZeroSymbols);
    }
    private void Label(ref float y,float x,float width,string message,bool title=false)
    {
        var style=title?heading:text;
        float height=style.CalcHeight(new GUIContent(message),width);
        GUI.Label(new Rect(x,y,width,height),message,style);y+=height+7;
    }
    private void Slider(ref float y,float x,float width,string label,ref float value,float maximum,string unit,float display=1)
    {
        Label(ref y,x,width,$"{label}: {value*display:0.##} {unit}");
        value=GUI.HorizontalSlider(new Rect(x,y,width,18),value,0,maximum);y+=24;
    }
    public float Draw(float x,float y,float width)
    {
        if(!open)return 0;
        if(heading==null)
        {
            text=new GUIStyle(GUI.skin.label){fontSize=11,wordWrap=true};
            heading=new GUIStyle(text){fontSize=12,fontStyle=FontStyle.Bold};heading.normal.textColor=Color.cyan;
        }
        float start=y;
        Label(ref y,x,width,"LABORATORIO LRFD · TODAS LAS CARGAS",true);
        evaluationTab=GUI.Toolbar(new Rect(x,y,width,27),evaluationTab,new[]{"ESCENARIO / CLIMA","RESULTADOS U1–U7"});y+=34;
        if(evaluationTab==1){contentHeight=y-start+DrawLrfdEvaluation(x,y,width)+16;return contentHeight;}
        float half=(width-5)/2;
        if(GUI.Button(new Rect(x,y,half,27),"DEMO LLUVIA")){Demo(1);}
        if(GUI.Button(new Rect(x+half+5,y,half,27),"DEMO NEVADA")){Demo(2);}y+=33;
        if(GUI.Button(new Rect(x,y,half,27),"DEMO VIENTO")){Demo(3);}
        if(GUI.Button(new Rect(x+half+5,y,half,27),"VER LAS 7 CARGAS")){Demo(4);}y+=34;
        Label(ref y,x,width,"Mostrar acciones (solo visibilidad):");
        string[] actionNames={"D · peso","L · personas","Lr · cubierta","S · nieve","R · lluvia/agua","W · viento","E · sismo"};
        for(int i=0;i<7;i++)
        {
            shown[i]=GUI.Toggle(new Rect(x+(i%2)*(half+5),y+(i/2)*25,half,23),shown[i],actionNames[i]);
        }
        y+=103;
        showZeroSymbols=GUI.Toggle(new Rect(x,y,width,24),showZeroSymbols,"Mostrar también símbolos con carga 0");y+=30;
        bool nextInterior=GUI.Toggle(new Rect(x,y,width,24),interior,"Abrir fachada/techo para ver cargas interiores");y+=30;
        if(nextInterior!=interior){interior=nextInterior;viewer.SetLaboratoryInterior(interior);}
        Label(ref y,x,width,"Pesos violetas · personas naranjas · mantenimiento amarillo · nieve blanca · agua azul · viento verde · E rojo. Gris = carga 0. Objetos y flechas son símbolos, no cargas nodales adicionales.");
        Label(ref y,x,width,"Escenarios visuales + cargas físicas. Los factores de combinación se aplican por separado. No son una certificación de diseño.");
        if(SeismicPlaybackController.IsActive)Label(ref y,x,width,"Efectos ambientales suspendidos durante el sismo real.");
        Slider(ref y,x,width,"D · Permanente exportada",ref d,2,"x base G");
        Slider(ref y,x,width,"L · Uso exportado",ref l,2,"x base Q");
        Label(ref y,x,width,$"L aplicada = {l*(model!=null?model.Q_kN_m2:0):0.###} kN/m². Flechas naranjas en pisos.");
        Slider(ref y,x,width,"Lr · Sobrecarga de cubierta",ref roof,3,"kN/m² · evaluar en U1–U7");
        Label(ref y,x,width,"CLIMA · lluvia y nieve son excluyentes",true);
        precipitation=GUI.Toolbar(new Rect(x,y,width,26),precipitation,new[]{"Despejado","Lluvia","Nieve"});y+=33;
        accumulate=GUI.Toggle(new Rect(x,y,width,23),accumulate,"Acumular precipitación");y+=29;
        Slider(ref y,x,width,"Precipitación",ref precipitationRate,100,"mm/h");
        Slider(ref y,x,width,"Tiempo ambiental acelerado",ref weatherTimeScale,600,"x · solo clima");
        Slider(ref y,x,width,"S · Espesor de nieve",ref snowDepth,.6f,"cm",100);
        Slider(ref y,x,width,"Densidad de nieve",ref snowDensity,500,"kg/m³");
        float snow=LrfdScenario.SnowPressure(snowDepth,snowDensity),water=LrfdScenario.WaterPressure(waterDepth);
        Label(ref y,x,width,$"S = h·ρ·g = {snow:0.###} kN/m² · evaluación independiente en U1–U7.");
        Slider(ref y,x,width,"R · Agua retenida",ref waterDepth,.2f,"mm",1000);
        Label(ref y,x,width,$"R = h·ρagua·g = {water:0.###} kN/m². Se supone retención sin drenaje; no se convierte la cantidad de gotas directamente en carga.");
        if(GUI.Button(new Rect(x,y,width,25),"LIMPIAR ACUMULACIÓN")){snowDepth=0;waterDepth=0;accumulate=false;}y+=32;
        Label(ref y,x,width,"Cambiar clima apaga las partículas anteriores y conserva la acumulación. Limpiar elimina nieve/agua y pausa la acumulación.");
        Slider(ref y,x,width,"W · Velocidad de viento",ref windSpeed,50,"m/s");
        Slider(ref y,x,width,"Coeficiente de presión",ref windCoefficient,2,"Cp de referencia");
        Slider(ref y,x,width,"Dirección de viento",ref windAngle,360,"grados");
        float wind=LrfdScenario.WindPressure(windSpeed,windCoefficient);
        Label(ref y,x,width,$"W referencia = 0,613·v²·Cp = {wind:0.###} kN/m². Corrientes verdes y flechas horizontales. Faltan distribución y presiones normativas.");
        Slider(ref y,x,width,"E · Escala de EX/EY exportados",ref e,1.5f,"x · escenario pseudoestático");
        Label(ref y,x,width,"Las flechas rojas indican la acción lateral. No simulan un terremoto. El botón superior SIMULACIÓN SÍSMICA reproduce la respuesta dinámica real.");
        Label(ref y,x,width,"COMBINACIONES DE LOS APUNTES",true);
        combination=GUI.SelectionGrid(new Rect(x,y,width,7*28),combination,LrfdScenario.Names,1);y+=7*28+8;
        Label(ref y,x,width,"A = Lr, S o R: alternativas, no su suma.");
        alternative=GUI.Toolbar(new Rect(x,y,width,25),alternative,new[]{"Lr","S","R"});y+=32;
        accompanyingWind=GUI.Toggle(new Rect(x,y,width,23),accompanyingWind,"Combinación 3: acompañante W");y+=29;
        var factors=LrfdScenario.Factors(combination,alternative,accompanyingWind);
        Label(ref y,x,width,$"Factores: D {factors[0]:0.#} · L {factors[1]:0.#} · Lr {factors[2]:0.#} · S {factors[3]:0.#} · R {factors[4]:0.#} · W {factors[5]:0.#} · E {factors[6]:0.#}");
        Label(ref y,x,width,$"Cubierta ambiental: γLr·Lr={factors[2]*roof:0.###}; γS·S={factors[3]*snow:0.###}; γR·R={factors[4]*water:0.###} kN/m². No se mezclan con la presión lateral W.");
        bool pending=LrfdScenario.HasUnavailableResponse(factors,roof,snow,water,wind,e);
        Label(ref y,x,width,pending?
            "Para incluir cubierta, nieve, agua, viento o EX/EY use RESULTADOS U1–U7 y EVALUAR. Este botón rápido solo admite D/L.":
            "Esta configuración solo tiene términos D/L no nulos: se puede superponer la respuesta lineal exportada de G/Q.");
        bool enabled=GUI.enabled;GUI.enabled=enabled && !pending && !SeismicPlaybackController.IsActive && model?.p1l4!=null;
        if(GUI.Button(new Rect(x,y,width,28),"APLICAR DEMANDA D/L DISPONIBLE"))
        {
            viewer.ApplyLaboratoryLoads(factors[0]*d,factors[1]*l);
            status="Demanda D/L aplicada. Abra los diagramas o el radar. Capacidad de diseño LRFD: pendiente de validación independiente.";
        }
        GUI.enabled=enabled;y+=35;
        if(!string.IsNullOrEmpty(status))Label(ref y,x,width,status);
        Label(ref y,x,width,"ϕ·Rn ≥ Ru · consulte la evaluación P–My de diseño en U1–U7. El resto de verificaciones queda pendiente; no se usa ϕ=0,9 para todos ni se mezcla el radar nominal.");
        Label(ref y,x,width,"C1/C2/C3 permanecen disponibles. Fórmulas de apuntes: falta confirmar edición normativa y definición de cargas antes de certificar diseño.");
        contentHeight=y-start+20;return contentHeight;
    }
    private void Demo(int preset)
    {
        for(int i=0;i<7;i++)shown[i]=true;
        d=l=1;roof=1;windCoefficient=1;windAngle=0;snowDensity=300;precipitationRate=60;showZeroSymbols=true;
        snowDepth=preset==2 || preset==4?.1f:0;waterDepth=preset==1 || preset==4?.015f:0;
        precipitation=preset==1?1:preset==2 || preset==4?2:0;
        windSpeed=preset==3?20:preset==4?15:8;e=preset==4?1:0;
        accumulate=preset==1 || preset==2;weatherTimeScale=120;
        status="Demostración visual activada. Las intensidades se ven en sus controles; no aplica nuevos esfuerzos automáticamente.";
    }
}
