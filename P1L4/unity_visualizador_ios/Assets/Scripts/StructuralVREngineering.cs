using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class StructuralVRController
{
    private readonly MobileStructuralTools vrEngineering=new MobileStructuralTools();
    private Transform engineeringRoot;
    private TextMesh engineeringInfo,engineeringFooter;
    private readonly List<TextMesh> engineeringButtons=new List<TextMesh>();
    private Renderer engineeringScreen,engineeringPlot;
    private readonly List<GameObject> normalMenu=new List<GameObject>();
    private bool engineeringOpen,radarMap,radarTour,lrfdTour,showLrfdActions,lrfdInterior;
    private bool engineeringDetails;
    private int vrEngineeringPage,radarIndex,lrfdIndex,lrfdVariable,climate;
    private float nextEngineering,nextTour;
    private List<MobileStructuralTools.RadarRow> mobileRanking=new List<MobileStructuralTools.RadarRow>();
    private readonly Dictionary<Renderer,MaterialPropertyBlock> radarOriginal=new Dictionary<Renderer,MaterialPropertyBlock>();
    private LrfdLoadVisuals mobileLoadVisuals;
    private readonly Dictionary<GameObject,bool> interiorOriginal=new Dictionary<GameObject,bool>();
    private void InitializeEngineeringVR()
    {
        normalMenu.Clear();foreach(Transform child in menu)if(child.name!="Piso actual")normalMenu.Add(child.gameObject);
        EngineeringButton("Estudio / volver",menu,new Vector3(-1.35f,.23f,0),()=>ToggleEngineeringVR());
        EngineeringButton("Detalle",menu,new Vector3(-1.35f,0,0),()=>{engineeringDetails=!engineeringDetails;nextEngineering=0;});
        menu.Find("Detalle").gameObject.SetActive(false);menu.Find("Detalle texto").gameObject.SetActive(false);
        engineeringRoot=new GameObject("Herramientas por mirada").transform;engineeringRoot.SetParent(menu,false);
        engineeringRoot.localPosition=Vector3.up*1.25f;
        string[] pages={"Armadura","Radar","LRFD","Diafragmas"};
        for(int i=0;i<pages.Length;i++){int page=i;EngineeringButton(pages[i],engineeringRoot,new Vector3((i-1.5f)*.5f,.37f,0),()=>{vrEngineeringPage=page;radarTour=lrfdTour=false;nextEngineering=0;});}
        for(int i=0;i<12;i++)
        {
            int index=i;var label=EngineeringButton("Accion "+i,engineeringRoot,new Vector3((i%4-1.5f)*.47f,.12f-(i/4)*.23f,0),()=>EngineeringAction(index));
            engineeringButtons.Add(label);
        }
        engineeringInfo=Text("Inspector móvil VR",engineeringRoot,new Vector3(0,-.51f,-.03f),.0105f,"");engineeringInfo.anchor=TextAnchor.UpperCenter;
        engineeringFooter=Text("Alcance del inspector",engineeringRoot,new Vector3(0,-1.95f,-.03f),.01f,"");
        engineeringScreen=EngineeringImage("Vista 3D de armadura",new Vector3(-.43f,-1.58f,-.02f),new Vector3(.83f,.48f,1));
        engineeringPlot=EngineeringImage("Curva de sección",new Vector3(.47f,-1.58f,-.02f),new Vector3(.75f,.43f,1));
        engineeringRoot.gameObject.SetActive(false);
    }
    private TextMesh EngineeringButton(string name,Transform parent,Vector3 p,Action action)
    {
        var button=GameObject.CreatePrimitive(PrimitiveType.Cube);button.name=name;button.layer=31;button.transform.SetParent(parent,false);
        button.transform.localPosition=p;button.transform.localScale=new Vector3(.44f,.18f,.02f);
        button.GetComponent<Renderer>().sharedMaterial=MaterialFor(new Color(.08f,.24f,.27f),true);
        button.AddComponent<StructuralVRButton>().Index=actions.Count;actions.Add(action);
        return Text(name+" texto",parent,p+Vector3.back*.02f,.012f,name);
    }
    private Renderer EngineeringImage(string name,Vector3 p,Vector3 scale)
    {
        var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.name=name;quad.layer=31;quad.transform.SetParent(engineeringRoot,false);
        quad.transform.localPosition=p;quad.transform.localScale=scale;Destroy(quad.GetComponent<Collider>());
        var renderer=quad.GetComponent<Renderer>();var material=new Material(Resources.Load<Shader>("VRInspectorSurface")){color=Color.white};materials.Add(material);
        renderer.sharedMaterial=material;material.renderQueue=4000;return renderer;
    }
    private void ToggleEngineeringVR()
    {
        StopMovement();engineeringOpen=!engineeringOpen;
        title.transform.localPosition=new Vector3(0,engineeringOpen?1.78f:.43f,0);
        menu.Find("Detalle").gameObject.SetActive(engineeringOpen);menu.Find("Detalle texto").gameObject.SetActive(engineeringOpen);
        foreach(var g in normalMenu)if(g!=null)g.SetActive(!engineeringOpen);
        engineeringRoot.gameObject.SetActive(engineeringOpen);nextEngineering=0;
        if(!engineeringOpen){vrEngineering.Preview?.Hide();radarTour=lrfdTour=false;RestoreRadarMap();ShowMobileLrfd(false);}
    }
    private void EngineeringAction(int index)
    {
        if(MobileSeismicPlayback.IsActive)return;
        if(vrEngineeringPage==3)return;
        nextEngineering=0;
        if(vrEngineeringPage==0)
        {
            switch(index)
            {
                case 0:vrEngineering.Playing=!vrEngineering.Playing;break;
                case 1:vrEngineering.Progress=0;vrEngineering.Playing=false;break;
                case 2:vrEngineering.StaticPreview=!vrEngineering.StaticPreview;vrEngineering.Progress=0;break;
                case 3:vrEngineering.Yaw+=30;break;
                case 4:vrEngineering.Zoom=vrEngineering.Zoom>=1.5f?.7f:vrEngineering.Zoom+.2f;break;
                case 5:vrEngineering.Rotate=!vrEngineering.Rotate;break;
                case 6:vrEngineering.Reverse=!vrEngineering.Reverse;break;
                case 7:vrEngineering.UseAxial=!vrEngineering.UseAxial;break;
                case 8:vrEngineering.Position=Mathf.Max(0,vrEngineering.Position-.1f);break;
                case 9:vrEngineering.Position=Mathf.Min(1,vrEngineering.Position+.1f);break;
                case 10:vrEngineering.PlotMode=(vrEngineering.PlotMode+1)%3;break;
                case 11:vrEngineering.Cracks=!vrEngineering.Cracks;break;
            }
        }
        else if(vrEngineeringPage==1)
        {
            switch(index)
            {
                case 0:vrEngineering.Metric=(vrEngineering.Metric+1)%5;break;
                case 1:var cases=new[]{"C1","C2","C3","ENV"};vrEngineering.RadarCase=cases[(Array.IndexOf(cases,vrEngineering.RadarCase)+1)%4];break;
                case 2:radarIndex=Mathf.Max(0,radarIndex-1);break;
                case 3:radarIndex=Mathf.Min(4,radarIndex+1);break;
                case 4:VisitMobileRadar();break;
                case 5:radarTour=!radarTour;radarIndex=0;VisitMobileRadar();nextTour=Time.unscaledTime+4;break;
                case 6:radarMap=!radarMap;if(!radarMap)RestoreRadarMap();break;
                case 7:radarTour=false;break;
            }
        }
        else
        {
            switch(index)
            {
                case 0:lrfdVariable=(lrfdVariable+1)%10;break;
                case 1:AdjustMobileScenario(-1);break;
                case 2:AdjustMobileScenario(1);break;
                case 3:vrEngineering.Lrfd.Evaluate();if(combo.StartsWith("LRFD_")){UnityData.UseBaseCaseFactors=false;combo="C1";RefreshResult();}break;
                case 4:lrfdIndex=(lrfdIndex+6)%7;ApplyMobileLrfd();break;
                case 5:lrfdIndex=(lrfdIndex+1)%7;ApplyMobileLrfd();break;
                case 6:var rows=vrEngineering.Lrfd.Rows(selected?.data);float worst=-1;for(int i=0;i<rows.Length;i++)if(rows[i]!=null&&rows[i].Ratio>worst){worst=rows[i].Ratio;lrfdIndex=i;}ApplyMobileLrfd();break;
                case 7:PresetMobileScenario();break;
                case 8:climate=(climate+1)%3;showLrfdActions=true;break;
                case 9:showLrfdActions=!showLrfdActions;break;
                case 10:SetMobileInterior(!lrfdInterior);break;
                case 11:lrfdTour=!lrfdTour;lrfdIndex=0;ApplyMobileLrfd();nextTour=Time.unscaledTime+4;break;
            }
        }
    }
    private void AdjustMobileScenario(int sign)
    {
        var s=vrEngineering.Lrfd.Input;
        switch(lrfdVariable)
        {
            case 0:s.d=Mathf.Clamp(s.d+sign*.1f,0,2);break;case 1:s.l=Mathf.Clamp(s.l+sign*.1f,0,2);break;
            case 2:s.roof=Mathf.Clamp(s.roof+sign*.1f,0,2);break;case 3:s.snowDepth=Mathf.Clamp(s.snowDepth+sign*.02f,0,.6f);break;
            case 4:s.snowDensity=Mathf.Clamp(s.snowDensity+sign*50,0,600);break;case 5:s.waterDepth=Mathf.Clamp(s.waterDepth+sign*.01f,0,.2f);break;
            case 6:s.windSpeed=Mathf.Clamp(s.windSpeed+sign*5,0,60);break;case 7:s.windCoefficient=Mathf.Clamp(s.windCoefficient+sign*.1f,0,2);break;
            case 8:s.windAngle=Mathf.Repeat(s.windAngle+sign*45,360);break;case 9:s.e=Mathf.Clamp(s.e+sign*.1f,0,2);break;
        }
        InvalidateMobileLrfd();
    }
    private void InvalidateMobileLrfd()
    {lrfdTour=false;if(combo.StartsWith("LRFD_")){UnityData.UseBaseCaseFactors=false;combo="C1";RefreshResult();}}
    private void PresetMobileScenario()
    {
        climate=(climate+1)%3;
        vrEngineering.Lrfd.Input=new LrfdInput{d=1,l=1,roof=.5f,snowDensity=300,windCoefficient=1,windSpeed=25,windAngle=45,e=1,
            snowDepth=climate==2?.1f:0,waterDepth=climate==1?.03f:0};showLrfdActions=true;InvalidateMobileLrfd();
    }
    private void ApplyMobileLrfd()
    {
        var rows=vrEngineering.Lrfd.Rows(selected?.data);if(rows[lrfdIndex]==null)return;
        UnityData.UseBaseCaseFactors=false;combo=rows[lrfdIndex].Variant.name;result=4;showDiagram=true;RefreshResult();RestoreRadarMap();
    }
    private void VisitMobileRadar()
    {
        if(mobileRanking.Count==0)return;radarIndex=Mathf.Clamp(radarIndex,0,Mathf.Min(4,mobileRanking.Count-1));var row=mobileRanking[radarIndex];
        var target=world.GetComponentsInChildren<ElementSelectable>().FirstOrDefault(e=>e.data?.id==row.Element.id);
        if(target==null)return;
        UnityData.UseBaseCaseFactors=false;combo=row.Combo;result=vrEngineering.Metric==1?(Mathf.Abs(row.Reading.My)>=Mathf.Abs(row.Reading.Mz)?4:5):
            vrEngineering.Metric==2?(Mathf.Abs(row.Reading.Vy)>=Mathf.Abs(row.Reading.Vz)?1:2):0;
        vrEngineering.Position=row.Reading.Position;SelectElement(target);
        // Selection and diagrams stay in place; never teleport the head unexpectedly.
    }
    private void UpdateEngineeringVR()
    {
        if(!engineeringOpen){ShowMobileLrfd(false);return;}
        bool suspended=MobileSeismicPlayback.IsActive;
        if(vrEngineeringPage!=1)RestoreRadarMap();
        if(suspended){engineeringInfo.text="Herramientas estáticas pausadas durante el sismo.";vrEngineering.Preview?.Hide();RestoreRadarMap();ShowMobileLrfd(false);return;}
        if(radarTour&&Time.unscaledTime>=nextTour){radarIndex++;if(radarIndex>=Mathf.Min(5,mobileRanking.Count))radarTour=false;else VisitMobileRadar();nextTour=Time.unscaledTime+4;}
        if(lrfdTour&&Time.unscaledTime>=nextTour){if(!vrEngineering.Lrfd.Current)lrfdTour=false;else{lrfdIndex=(lrfdIndex+1)%7;ApplyMobileLrfd();}nextTour=Time.unscaledTime+4;}
        ShowMobileLrfd(vrEngineeringPage==2&&showLrfdActions);
        engineeringScreen.enabled=engineeringPlot.enabled=vrEngineeringPage==0&&selected!=null;
        if(vrEngineeringPage!=0)vrEngineering.Preview?.Hide();
        if(vrEngineeringPage==0&&selected!=null)
        {
            string sample=vrEngineering.UpdatePreview(selected.data,combo,Time.unscaledDeltaTime);
            engineeringScreen.sharedMaterial.mainTexture=vrEngineering.Preview?.Texture;engineeringPlot.sharedMaterial.mainTexture=vrEngineering.Plot;
            engineeringFooter.text=sample+"\n"+(vrEngineering.PlotMode==0?"X: Φ [1/m] · Y: M [kN·m]":vrEngineering.PlotMode==1?"X: |My| [kN·m] · Y: P [kN] · curva nominal":"X: My [kN·m] · Y: P [kN] · φ variable · diseño parcial");
        }
        if(Time.unscaledTime<nextEngineering)return;nextEngineering=Time.unscaledTime+.6f;
        string[] labels=vrEngineeringPage==0?new[]{"Play/Pausa","Inicio","Modo","Girar","Zoom","My/Mz","Cara ±","P activa/0","Corte −","Corte +","MΦ / P-M","Fisuras"}:
            vrEngineeringPage==1?new[]{"Métrica","Caso","Top −","Top +","Seleccionar","Recorrer","Mapa","Parar","—","—","—","—"}:
            new[]{"Variable","−","+","Evaluar","U −","U +","Gobernante","Escenario","Clima","Acciones","Interior","Recorrer"};
        for(int i=0;i<labels.Length;i++)
        {
            engineeringButtons[i].text=labels[i];
            engineeringButtons[i].gameObject.SetActive(vrEngineeringPage!=3);
            engineeringRoot.Find("Accion "+i).gameObject.SetActive(vrEngineeringPage!=3);
        }
        if(vrEngineeringPage==3)
        {
            engineeringInfo.text=string.Join("\n",MobileDiaphragmInfo.Describe(selected?.data,combo).Split('\n').Take(13));
            engineeringFooter.text="Membresía real rigidDiaphragm(3)\nConsulta estática de C1/C2/C3 y casos LRFD evaluados.";return;
        }
        if(vrEngineeringPage==0)
        {
            string full=engineeringDetails?vrEngineering.Calculation(selected?.data,combo)+"\n"+string.Join("\n",vrEngineering.Inspect(selected?.data,combo).Split('\n').Skip(8)):
                vrEngineering.Inspect(selected?.data,combo);
            engineeringInfo.text=string.Join("\n",full.Split('\n').Take(9));
        }
        else if(vrEngineeringPage==1)
        {
            if(combo.StartsWith("LRFD_")){engineeringInfo.text="Radar nominal pausado en LRFD.\nVuelve a C1/C2/C3.";RestoreRadarMap();return;}
            if(!radarTour)mobileRanking=vrEngineering.Rank(world.GetComponentsInChildren<ElementSelectable>().Where(e=>e.data!=null&&e.gameObject.activeInHierarchy&&
                e.GetComponent<Renderer>()?.enabled==true&&(e.data.type=="columna"?
                    Mathf.Min(e.startPoint.y,e.endPoint.y)<=Floors[FloorIndex]+.6f&&Mathf.Max(e.startPoint.y,e.endPoint.y)>=Floors[FloorIndex]+.6f:
                    Mathf.Abs((e.startPoint.y+e.endPoint.y)*.5f-Floors[FloorIndex])<.1f)).Select(e=>e.data));
            engineeringInfo.text="TOP 5 · "+new[]{"D/C nominal","M kN·m","V kN","N− kN","N+ kN"}[vrEngineering.Metric]+" · "+vrEngineering.RadarCase+"\n"+
                string.Join("\n",mobileRanking.Take(5).Select((r,i)=>(i==radarIndex?"> ":"")+(i+1)+". "+r.Element.elementTag+" ID "+r.Element.id+" · "+r.Reading.Value.ToString("0.##")+" · "+r.Combo+" · "+(r.Reading.Position*100).ToString("0")+"% I"));
            engineeringFooter.text="Ranking del piso actual · selección por mirada\nD/C nominal P–My parcial. M/V/N: colores relativos, no seguridad.";
            PaintMobileRadar();
        }
        else
        {
            var s=vrEngineering.Lrfd.Input;string[] names={"D factor","L factor","Lr kN/m²","Nieve cm","Densidad kg/m³","Agua mm","Viento m/s","Cp","Dirección °","E factor"};
            float[] values={s.d,s.l,s.roof,s.snowDepth*100,s.snowDensity,s.waterDepth*1000,s.windSpeed,s.windCoefficient,s.windAngle,s.e};
            engineeringInfo.text="LRFD · "+names[lrfdVariable]+" = "+values[lrfdVariable].ToString("0.##")+"\n"+vrEngineering.Lrfd.Status+"\n";
            if(!vrEngineering.Lrfd.Current)engineeringInfo.text+="Escenario cambiado: pulsa Evaluar.\nNo se aplican resultados de un escenario anterior.";
            else
            {
                var rows=vrEngineering.Lrfd.Rows(selected?.data);
                for(int i=0;i<rows.Length;i++){var r=rows[i];engineeringInfo.text+="\n"+(i==lrfdIndex?"> ":"")+"U"+(i+1)+": "+(r==null?"selecciona viga/columna":r.HasCapacity?"D/C diseño "+r.Ratio.ToString("0.##"):"sin capacidad, |My| "+r.Ratio.ToString("0.##"));}
                if(rows[lrfdIndex]!=null){var r=rows[lrfdIndex];engineeringInfo.text+=$"\nP={-r.Forces.N:0.##} · My={r.Forces.My:0.##} · Mz={r.Forces.Mz:0.##}\nVy={r.Forces.Vy:0.##} · Vz={r.Forces.Vz:0.##} · x={r.Position*100:0}% I\n"+r.Variant.label;}
            }
            engineeringFooter.text="Unidades OpenSees precalculadas · no reanálisis en teléfono\nφ variable · P–My uniaxial parcial, sin certificación ni colapso.\nClima/acciones: representación visual independiente de los esfuerzos.";
        }
    }
    private void PaintMobileRadar()
    {
        if(!radarMap){RestoreRadarMap();return;}
        RestoreRadarMap();
        var lookup=mobileRanking.ToDictionary(r=>r.Element.id);float max=mobileRanking.Count==0?1:Mathf.Max(.001f,mobileRanking[0].Reading.Value);
        foreach(var e in world.GetComponentsInChildren<ElementSelectable>())
        {
            var renderer=e.GetComponent<Renderer>();if(renderer==null||e.data==null||!lookup.TryGetValue(e.data.id,out var row))continue;
            if(!radarOriginal.ContainsKey(renderer)){var previous=new MaterialPropertyBlock();renderer.GetPropertyBlock(previous);radarOriginal[renderer]=previous;}
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
            float t=vrEngineering.Metric==0?Mathf.Clamp01(row.Reading.Value):row.Reading.Value/max;
            block.SetColor("_Color",e==selected?Color.yellow:Color.Lerp(Color.green,Color.red,t));renderer.SetPropertyBlock(block);
        }
    }
    private void RestoreRadarMap(){foreach(var pair in radarOriginal)if(pair.Key!=null)pair.Key.SetPropertyBlock(pair.Value);radarOriginal.Clear();}
    private void SetMobileInterior(bool value)
    {
        lrfdInterior=value;
        if(value)foreach(var g in world.GetComponentsInChildren<VisualFrameFacade>(true).SelectMany(f=>f.Panels).Concat(world.GetComponentsInChildren<VisualFlatRoof>(true).SelectMany(r=>r.Pieces)))
        {if(!interiorOriginal.ContainsKey(g))interiorOriginal[g]=g.activeSelf;g.SetActive(false);}
        else{foreach(var pair in interiorOriginal)if(pair.Key!=null)pair.Key.SetActive(pair.Value);interiorOriginal.Clear();}
    }
    private void ShowMobileLrfd(bool visible)
    {
        if(!visible){mobileLoadVisuals?.Update(false,0,0,0,0,0,new float[7],0,new[]{true,true,true,true,true,true,true},false);if(lrfdInterior)SetMobileInterior(false);return;}
        if(mobileLoadVisuals==null)
        {
            mobileLoadVisuals=new LrfdLoadVisuals();mobileLoadVisuals.Build(world.GetComponent<StructureViewer>(),data);
            var root=world.transform.Find("Laboratorio_LRFD_solo_visual");
            foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            foreach(var r in root.GetComponentsInChildren<Renderer>(true))foreach(var m in r.sharedMaterials)
                if(m!=null)m.shader=r.GetComponent<TextMesh>()!=null?Resources.Load<Shader>("VRMenuOverlay"):
                    r is ParticleSystemRenderer || r.name=="Nubes_LRFD" || m.color.a<.99f?Resources.Load<Shader>("VRLrfdTransparent"):Resources.Load<Shader>("VRVisualEnvironment");
        }
        var s=vrEngineering.Lrfd.Input;
        mobileLoadVisuals.Update(true,climate,s.snowDepth,s.waterDepth,s.windSpeed,s.windAngle,
            new[]{s.d,s.l,s.roof,LrfdScenario.SnowPressure(s.snowDepth,s.snowDensity),LrfdScenario.WaterPressure(s.waterDepth),LrfdScenario.WindPressure(s.windSpeed,s.windCoefficient),s.e},50,new[]{true,true,true,true,true,true,true},true);
    }
    private void DisposeEngineeringVR()
    {
        vrEngineering.Dispose();RestoreRadarMap();mobileLoadVisuals?.Dispose();mobileLoadVisuals=null;
        if(world!=null)SetMobileInterior(false);engineeringOpen=radarTour=lrfdTour=radarMap=showLrfdActions=false;engineeringButtons.Clear();normalMenu.Clear();engineeringRoot=null;
    }
}
