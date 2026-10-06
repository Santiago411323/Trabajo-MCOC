using UnityEngine;

public partial class ElementResultsPanel
{
    private MemberRebarPreview memberPreview;
    private bool memberPlaying=true;
    private bool memberIllustrativeCracks;
    private int memberPlaybackMode;
    private float memberProgress,memberClock,memberSpeed=1,memberSlice=.5f;
    private float memberYaw=25,memberPitch=14,memberZoom=1,memberVisualScale=1;
    private float memberPlaybackHeight;

    private void UpdateMemberPlayback()
    {
        if(!expanded || view!=ResultView.Fibers){memberPreview?.Hide();return;}
        if(memberPlaying)
        {
            memberClock+=Time.unscaledDeltaTime*memberSpeed;
            memberProgress=Mathf.SmoothStep(0,1,Mathf.PingPong(memberClock/12,1));
        }
    }

    private float DrawMemberPlayback(ElementSelectable element,SectionMaterialData material,PMCurveData curve,
                                     float x,float y,float width)
    {
        if(!IsBeam(element) && !IsColumn(element)){memberPlaybackHeight=0;return 0;}
        float start=y;
        GUI.Label(new Rect(x,y,width,23),"LABORATORIO 3D · ELEMENTO Y ARMADURA",titleStyle);y+=27;
        int mode=GUI.Toolbar(new Rect(x,y,width,25),memberPlaybackMode,new[]{"CURVA DE SECCIÓN · DIDÁCTICA","DEFORMADA ESTÁTICA · OPENSEES"});
        if(mode!=memberPlaybackMode){memberPlaybackMode=mode;memberProgress=0;memberClock=0;}
        y+=31;
        if(GUI.Button(new Rect(x,y,95,24),memberPlaying?"PAUSE":"PLAY"))memberPlaying=!memberPlaying;
        if(GUI.Button(new Rect(x+100,y,90,24),"REINICIAR")){memberClock=0;memberProgress=0;}
        memberSpeed=GUI.HorizontalSlider(new Rect(x+203,y+6,100,18),memberSpeed,.25f,2);
        GUI.Label(new Rect(x+310,y,width-310,24),$"Velocidad {memberSpeed:0.00}x",mutedStyle);y+=31;
        GUI.Label(new Rect(x,y,92,22),"Recorrido",mutedStyle);
        float progress=GUI.HorizontalSlider(new Rect(x+95,y+5,width-150,18),memberProgress,0,1);
        if(Mathf.Abs(progress-memberProgress)>.0001f){memberProgress=progress;memberPlaying=false;}
        GUI.Label(new Rect(x+width-48,y,48,22),$"{memberProgress*100:0}%",valueStyle);y+=28;
        var state=MemberMaterialPlayback.Sample(material,curve,memberProgress);
        bool curveMode=memberPlaybackMode==0;
        if(curveMode)
        {
            memberIllustrativeCracks=GUI.Toggle(new Rect(x,y,width,23),memberIllustrativeCracks,"Mostrar grietas didácticas · posición y abertura ilustrativas");
            y+=28;
        }
        bool realAvailable=HasMemberDisplacements(element);
        if(curveMode && !state.Available || !curveMode && !realAvailable)
        {
            GUI.Label(new Rect(x,y,width,55),curveMode?"No hay curva y discretización de fibras suficientes para animar materiales.":
                "No hay desplazamientos nodales exportados suficientes para esta combinación.",mutedStyle);
            memberPreview?.Hide();memberPlaybackHeight=y-start+65;return memberPlaybackHeight;
        }
        if(memberPreview==null)memberPreview=new MemberRebarPreview();
        memberPreview.Bind(element,material);
        Rect image=new Rect(x,y,width,Mathf.Min(340,Mathf.Max(230,width*9/16)));
        if(Event.current.type==EventType.MouseDrag && image.Contains(Event.current.mousePosition))
        {memberYaw+=Event.current.delta.x*.45f;memberPitch=Mathf.Clamp(memberPitch-Event.current.delta.y*.35f,-70,70);Event.current.Use();}
        if(Event.current.type==EventType.Repaint)
        {
            memberPreview.UpdateGeometry(t=>curveMode?Vector3.zero:MemberDisplayDisplacement(element,t)*memberVisualScale*memberProgress,
                curveMode?state.Phi:0,curveMode?memberVisualScale:0,curveMode?state:null,memberSlice,curveMode && memberIllustrativeCracks);
            memberPreview.Show(memberYaw,memberPitch,memberZoom);
        }
        if(memberPreview.Texture!=null)GUI.DrawTexture(image,memberPreview.Texture,ScaleMode.ScaleToFit,false);
        GUI.Label(new Rect(image.x+10,image.y+8,image.width-20,22),
            $"{ElementTag(element)} · ID {element.data.id} · L real = {memberPreview.Length:0.###} m",valueStyle);
        GUI.Label(new Rect(image.x+10,image.yMax-26,image.width-20,22),"Arrastre para girar · Hormigón transparente · Sección móvil en cian",mutedStyle);
        y=image.yMax+8;
        GUI.Label(new Rect(x,y,72,23),"Zoom",mutedStyle);
        memberZoom=GUI.HorizontalSlider(new Rect(x+72,y+6,105,18),memberZoom,.65f,1.6f);
        GUI.Label(new Rect(x+190,y,105,23),"Escala gráfica",mutedStyle);
        memberVisualScale=GUI.HorizontalSlider(new Rect(x+295,y+6,Mathf.Max(80,width-363),18),memberVisualScale,1,curveMode?3:100);
        GUI.Label(new Rect(x+width-62,y,62,23),$"{memberVisualScale:0.0}x",valueStyle);y+=29;
        // Un factor grande válido para desplazamientos no se reutiliza como curvatura al cambiar de modo.
        memberVisualScale=Mathf.Clamp(memberVisualScale,1,curveMode?3:100);
        GUI.Label(new Rect(x,y,100,23),"Sección móvil",mutedStyle);
        memberSlice=GUI.HorizontalSlider(new Rect(x+104,y+5,width-180,18),memberSlice,0,1);
        GUI.Label(new Rect(x+width-70,y,70,23),$"{100*memberSlice:0.#}%",valueStyle);y+=28;
        if(element.data!=null && UnityData.TryGetSectionForces(element.data.id,UnityData.ActiveCombo,memberSlice,out var force))
            GUI.Label(new Rect(x,y,width,40),$"CORTE DEL ELEMENTO · {memberSlice*memberPreview.Length:0.###} m desde I ({100*memberSlice:0.#}%)\n"+
                $"{UnityData.GetActiveLoadLabel()} · N {force.N:0.###} kN · Vy {force.Vy:0.###} · Vz {force.Vz:0.###} kN · My {force.My:0.###} · Mz {force.Mz:0.###} kN·m",textStyle);
        else GUI.Label(new Rect(x,y,width,40),"Sin esfuerzos exportados en esta posición.",mutedStyle);
        y+=46;
        if(curveMode)
        {
            GUI.Label(new Rect(x,y,width,22),state.Stage,titleStyle);y+=26;
            GUI.Label(new Rect(x,y,width,41),$"P del ensayo = {state.Axial:0.###} kN · M = {state.Moment:0.###} kN·m · Φ = {state.Phi:0.000000} 1/m\n"+
                $"εs,máx = {state.MaxSteel:0.00000} · εc,máx = {state.MaxConcrete:0.00000} · Verde: tracción · Naranja: compresión · Rojo: fluencia",textStyle);y+=47;
            Rect plot=new Rect(x+49,y+10,width-60,108);
            DrawMomentCurvatureGrid(plot,Mathf.Max(.000001f,curve.momentCurvature[state.LastIndex].phi_1_m*1.06f),
                Mathf.Max(.01f,MaximumPlaybackMoment(curve,state.LastIndex)*1.1f));
            float phiMax=curve.momentCurvature[state.LastIndex].phi_1_m*1.06f,mMax=MaximumPlaybackMoment(curve,state.LastIndex)*1.1f;
            Vector2 previous=MapMomentCurvature(plot,0,0,phiMax,mMax);
            for(int i=0;i<=state.LastIndex;i++)
            {var p=curve.momentCurvature[i];if(p==null)continue;Vector2 point=MapMomentCurvature(plot,p.phi_1_m,p.M_kN_m,phiMax,mMax);DrawLine(previous,point,p.steel_yielded?Color.red:Color.cyan,2);previous=point;}
            DrawMarker(MapMomentCurvature(plot,state.Phi,state.Moment,phiMax,mMax),Color.yellow,10);
            GUI.Label(new Rect(x,y-8,width,20),"PUNTO SINCRONIZADO · CURVA EXPORTADA · P ≈ 0",mutedStyle);y+=146;
            GUI.Label(new Rect(x,y,width,58),
                "Flexión pura de sección: curvatura constante ilustrativa, no la deformada del edificio. Mcr es un umbral didáctico; esta curva no modela resistencia del hormigón en tracción. El recorrido se detiene en εcu; no representa colapso.",mutedStyle);y+=64;
        }
        else
        {
            Vector3 ui=UnityData.GetNodeDisplacement(UnityData.ActiveCombo,element.data.nodeI),uj=UnityData.GetNodeDisplacement(UnityData.ActiveCombo,element.data.nodeJ);
            GUI.Label(new Rect(x,y,width,43),$"DESPLAZAMIENTOS REALES · I: {ui.magnitude*1000:0.###} mm · J: {uj.magnitude*1000:0.###} mm\n"+
                "Reproducción gráfica de una combinación estática; no es una historia temporal de terremoto.",textStyle);y+=49;
            GUI.Label(new Rect(x,y,width,45),"Barras neutras: no se deduce fluencia del acero desde el análisis elástico del edificio. La deformada interpola traslaciones y, cuando están disponibles, rotaciones nodales.",mutedStyle);y+=51;
        }
        GUI.Label(new Rect(x,y,width,44),material.stirrupCount>0?
            $"{material.stirrupCount} estribos · {(material.stirrupCount-1)*material.stirrupSpacing_mm/1000:0.###} m de tramo. Ubicación centrada ilustrativa: no está exportada. Sin fisuras individuales calculadas.":
            "No se exportó una distribución longitudinal de estribos para esta sección; no se inventa. Sin fisuras individuales calculadas.",mutedStyle);y+=50;
        memberPlaybackHeight=y-start+10;return memberPlaybackHeight;
    }

    private static float MaximumPlaybackMoment(PMCurveData curve,int last)
    {float max=0;for(int i=0;i<=last;i++)if(curve.momentCurvature[i]!=null)max=Mathf.Max(max,curve.momentCurvature[i].M_kN_m);return max;}

    private static bool TryMemberRotation(string combo,int node,out Vector3 rotation)
    {
        rotation=Vector3.zero;
        if(UnityData.DisplacementsByCombo==null || !UnityData.DisplacementsByCombo.TryGetValue(combo,out var records))return false;
        foreach(var r in records)if(r.node==node){rotation=new Vector3(r.rx,r.ry,r.rz);return true;}
        return false;
    }
    private static bool MemberRotation(int node,out Vector3 rotation)
    {
        rotation=Vector3.zero;
        if(!UnityData.UseBaseCaseFactors)return TryMemberRotation(UnityData.ActiveCombo,node,out rotation);
        string[] cases={"G","Q","EX","EY"};float[] factors={UnityData.FactorG,UnityData.FactorQ,UnityData.FactorEX,UnityData.FactorEY};
        for(int i=0;i<4;i++){if(Mathf.Abs(factors[i])<1e-8f)continue;if(!TryMemberRotation(cases[i],node,out var r))return false;rotation+=r*factors[i];}
        return true;
    }
    private static bool HasMemberDisplacements(ElementSelectable element)
    {
        return element.data!=null && MemberRotation(element.data.nodeI,out _) && MemberRotation(element.data.nodeJ,out _);
    }
    private static Vector3 MemberDisplayDisplacement(ElementSelectable e,float t)
    {
        var ui=UnityData.GetNodeDisplacement(UnityData.ActiveCombo,e.data.nodeI);
        var uj=UnityData.GetNodeDisplacement(UnityData.ActiveCombo,e.data.nodeJ);
        Vector3 dir=(e.endPoint-e.startPoint).normalized;
        float length=Vector3.Distance(e.startPoint,e.endPoint);
        bool moving=UnityData.MobileDisplacements.ContainsKey(e.data.nodeI) || UnityData.MobileDisplacements.ContainsKey(e.data.nodeJ);
        bool haveI=MemberRotation(e.data.nodeI,out var ri),haveJ=MemberRotation(e.data.nodeJ,out var rj);
        Vector3 u=MemberPreviewKinematics.Interpolate(ui,uj,ri,rj,dir,length,t,!moving && haveI && haveJ);
        return Quaternion.Inverse(Quaternion.FromToRotation(Vector3.right,dir))*u;
    }
}
