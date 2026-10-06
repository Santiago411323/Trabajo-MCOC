using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

public partial class StructuralXRayController : MonoBehaviour
{
    private static StructuralXRayController instance;
    public static bool Rendering=>instance!=null&&instance.overlay!=null;
    public bool IsOpen=>open;
    public float ContentHeight=>!open?0:result==null?400:presentation?900:1100;
    private StructureViewer viewer;
    private IncrementalResponseNetwork graph;
    private IncrementalResponseNetwork.Frame frame;
    private IncrementalResponseNetwork.Result result;
    private MobileLoadController mobile;
    private ElementPicker picker;
    private OrbitCamera orbit;
    private StructuralXRayOverlay overlay;
    private string expectedHash,status="Selecciona una losa y aplica la persona para trazar su respuesta incremental.";
    private bool open,wantTrace,live=true,presentation=true,inside=true,showConstraints,cinematic,whyWalking;
    private bool savedFollow,followSaved,suppressSaved,savedSuppression;
    private int metric=3,threshold,filter,stage=5,lastCameraStage=-1,selectedId;
    private float movieClock,nextUpdate;
    private MobileLoadController.Response pendingResponse;
    private readonly Dictionary<Behaviour,bool> suspended=new Dictionary<Behaviour,bool>();
    private readonly Dictionary<int,ElementSelectable> selectableById=new Dictionary<int,ElementSelectable>();
    private Bounds modelBounds;
    private string savedDiagram;
    private Vector3 savedCameraTarget;private float savedCameraDistance,savedYaw,savedPitch;
    private bool cameraSaved;
    private GUIStyle wrap,heading,row,small;
    private Texture2D hudTexture;
    private Rect hudRect;
    private bool wasWalking;
    private bool workerRefreshRequested;
    public IncrementalResponseNetwork.Result Current=>result;

    public void Initialize(StructureViewer owner,StructureData data)
    {
        Shutdown();viewer=owner;instance=this;selectableById.Clear();
        try
        {
            graph=new IncrementalResponseNetwork(data);
            using(var sha=SHA256.Create())expectedHash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(DesktopModelFile.ActivePath))).Replace("-","").ToLowerInvariant();
            bool first=true;foreach(var e in graph.Members.Values)
                foreach(int node in new[]{e.I,e.J})
                {var p=Position(graph.Nodes[node]);if(first){modelBounds=new Bounds(p,Vector3.zero);first=false;}else modelBounds.Encapsulate(p);}
            foreach(var target in owner.GetComponentsInChildren<ElementSelectable>(true))
            {
                if(target.data!=null)selectableById[target.data.id]=target;
                else if(target.isWall)
                {
                    var wall=(data.walls??Array.Empty<WallData>()).FirstOrDefault(w=>w.id==target.wallId);
                    foreach(int id in wall?.analysisElements??Array.Empty<int>())selectableById[id]=target;
                }
            }
            Bind();status=$"Grafo real: {graph.Members.Count} barras/vínculos, {graph.Constraints.Count} MPC y {graph.Supports.Count} apoyos verticales. Aplica la persona y pulsa TRAZAR.";
        }
        catch(Exception e){graph=null;status="X-Ray no disponible: "+e.Message;Debug.LogWarning("[StructuralXRay] "+e.Message);}
    }
    private static Vector3 Position(NodeData n)=>new Vector3(n.x,n.z,n.y);
    private void Bind()
    {
        var next=GetComponent<MobileLoadController>();
        if(next!=mobile){if(mobile!=null)mobile.ResponseApplied-=OnResponse;mobile=next;if(mobile!=null)mobile.ResponseApplied+=OnResponse;}
        if(picker==null)picker=FindFirstObjectByType<ElementPicker>();
        if(orbit==null&&Camera.main!=null)orbit=Camera.main.GetComponent<OrbitCamera>();
    }
    public void Toggle()
    {
        if(open){Shutdown();return;}
        open=true;workerRefreshRequested=false;
        Trace();
    }
    private void OnResponse(MobileLoadController.Response response){if(wantTrace&&live&&!cinematic)pendingResponse=response;}
    private IncrementalResponseNetwork.Frame Capture(MobileLoadController.Response response)
    {
        return new IncrementalResponseNetwork.Frame {
            Sequence=response.seq,Slab=response.slab,X=response.x,Y=response.y,P=response.p,SourceHash=response.sourceHash,
            BaseLabel=UnityData.GetActiveLoadLabel(),TransferError=response.error,Transferred=response.transferred,
            LoadedNodes=(response.nodes??Array.Empty<MobileLoadController.Nodal>()).Where(n=>n.p>1e-9f).Select(n=>n.node).ToArray(),
            Forces=(response.forces??Array.Empty<ElementForceRecord>()).Select(f=>new ElementForceRecord{id=f.id,f=f.f!=null?(float[])f.f.Clone():null}).ToArray(),
            Displacements=(response.displacements??Array.Empty<DisplacementRecord>()).Select(d=>new DisplacementRecord{node=d.node,ux=d.ux,uy=d.uy,uz=d.uz}).ToArray(),
            Receivers=(response.receivers??Array.Empty<MobileLoadController.Receiver>()).Select(r=>new IncrementalResponseNetwork.Receiver{Beam=r.beam,Side=r.side,Load=r.load,Area=r.area,Position=r.t}).ToArray(),
            ReactionsAvailable=response.reactionsAvailable,
            Reactions=(response.reactions??Array.Empty<SupportReactionRecord>()).Select(r=>new SupportReactionRecord{node=r.node,fx=r.fx,fy=r.fy,fz=r.fz,mx=r.mx,my=r.my,mz=r.mz,declared=r.declared}).ToArray()
        };
    }
    private bool Accept(MobileLoadController.Response response)
    {
        if(graph==null||response==null||!response.ok)return false;
        if((response.forces?.Length??0)==0||(response.displacements?.Length??0)==0)
        {status="No hay respuesta incremental exportada; no se dibuja un camino ficticio.";return false;}
        if(string.IsNullOrEmpty(response.sourceHash)||response.sourceHash!=expectedHash)
        {
            EndRendering();frame=null;result=null;
            if(!workerRefreshRequested&&mobile!=null&&mobile.RestartIncrementalAnalysis())
            {
                workerRefreshRequested=true;
                status="Actualizando OpenSees para este modelo. La persona conserva su posición; X-Ray aparecerá al terminar el cálculo.";
            }
            else status="No coincide la huella del modelo: no se dibujan resultados ajenos. "+(mobile?.AnalysisStatus??"");
            return false;
        }
        if(!graph.Slabs.ContainsKey(response.slab)||response.error>Mathf.Max(1e-6f,response.p*1e-6f)||Math.Abs(response.transferred-response.p)>Mathf.Max(1e-6f,response.p*1e-6f))
        {status="Datos de losa/transferencia inconsistentes; no se dibuja una red supuesta.";return false;}
        frame=Capture(response);Rebuild();return true;
    }
    private void Rebuild()
    {
        if(frame==null||graph==null)return;
        result=graph.Evaluate(frame,(IncrementalResponseNetwork.Metric)metric,threshold);
        if(overlay==null)
        {
            ElementResultsPanel.SuspendForXRay();GetComponent<StructuralDemandRadar>()?.SuspendForComparison();
            savedDiagram=GetComponent<DiagramController>()?.CurrentResultName();GetComponent<DiagramController>()?.SetResultMode("None");
            viewer.SetXRayView(true);
            SuppressMobileUI();
            if(mobile!=null&&!followSaved){savedFollow=mobile.followPerson;followSaved=true;mobile.followPerson=false;}
            overlay=new StructuralXRayOverlay(transform);
        }
        overlay.Bind(graph,result,showConstraints);
        status=$"Respuesta seq {frame.Sequence} · losa {frame.Slab} · P={frame.P:0.###} kN · {result.Visible.Count} aristas visibles (incluye conectores).";
    }
    public void Trace()
    {
        if(StructuralModelEditor.ResultsStale||SeismicPlaybackController.IsActive||DesignComparisonSession.Rendering||(UnityData.ActiveCombo??"").StartsWith("LRFD_"))
        {status="Usa resultados estáticos vigentes; cierra sismo o comparación de diseños para trazar.";return;}
        wantTrace=true;stage=5;Bind();
        if(mobile!=null&&mobile.CurrentResponse!=null){if(Accept(mobile.CurrentResponse))SuppressMobileUI();}
        else if(mobile!=null&&mobile.LoadActive&&mobile.CurrentSlab!=null)
            status="Esperando respuesta OpenSees. X-Ray aparecerá automáticamente al terminar el cálculo.";
        else ConfigurePerson();
    }
    private void SuppressMobileUI()
    {
        if(mobile!=null)
        {
            if(!suppressSaved){savedSuppression=mobile.PresentationSuppressed;suppressSaved=true;}
            mobile.SetPresentationSuppressed(true);
        }
        foreach(var behaviour in GetComponents<Behaviour>())
            if(behaviour is MobileLoadLivePanel || behaviour is MobileLoadResultsDashboard)
            {if(!suspended.ContainsKey(behaviour))suspended[behaviour]=behaviour.enabled;behaviour.enabled=false;}
    }
    private void ConfigurePerson()
    {
        StopMovie(false);
        if(mobile==null)return;mobile.SetPresentationSuppressed(false);mobile.SetPanelVisible(true);
        var slabTarget=viewer.GetComponentInChildren<SlabSelectable>(true);
        if(slabTarget!=null)viewer.RevealSearchTarget(null,slabTarget);
        status=overlay==null?"Selecciona una losa y aplica la persona. X-Ray se dibujará automáticamente cuando OpenSees termine.":"Configura carga y posición. Pulsa TRAZAR para volver a la presentación X-Ray.";
    }
    private void Update()
    {
        if(!Application.isPlaying)return;Bind();
        bool walking=DesktopWalkthrough.IsActive;
        if(walking&&!wasWalking){StopMovie(false);status="Primera persona: respuesta congelada; click con la mira inspecciona una barra de la red.";pendingResponse=null;}
        wasWalking=walking;
        if(overlay!=null&&(StructuralModelEditor.ResultsStale||SeismicPlaybackController.IsActive||DesignComparisonSession.Rendering||(UnityData.ActiveCombo??"").StartsWith("LRFD_")))
        {EndRendering();status="X-Ray pausado para no mezclar estados/visualizaciones.";return;}
        if(!walking&&wantTrace&&mobile!=null&&!mobile.LoadActive)
        {
            bool hadFrame=frame!=null;
            EndRendering();frame=null;result=null;pendingResponse=null;
            if(hadFrame)status="Persona retirada. Selecciona una losa para volver a trazar.";
        }
        if(wantTrace&&result==null&&mobile!=null&&mobile.LoadActive&&mobile.CurrentResponse==null)
            status="X-Ray en espera · "+mobile.AnalysisStatus;
        if(!walking&&wantTrace&&live&&!cinematic&&mobile?.CurrentResponse!=null&&mobile.CurrentResponse.seq!=(frame?.Sequence??-1))pendingResponse=mobile.CurrentResponse;
        if(pendingResponse!=null&&Time.unscaledTime>=nextUpdate&&!walking)
        {var pending=pendingResponse;pendingResponse=null;nextUpdate=Time.unscaledTime+.35f;Accept(pending);}
        if(cinematic)
        {
            if(Input.GetMouseButtonDown(1)||Input.GetMouseButtonDown(2)||walking){StopMovie(false);}
            else
            {
                movieClock+=Time.unscaledDeltaTime;stage=Math.Min(5,(int)(movieClock/3f));
                if(stage!=lastCameraStage){lastCameraStage=stage;FocusStage(stage);}
                if(movieClock>=18){cinematic=false;live=false;stage=5;status="Recorrido completo · foto congelada. Inspecciona el resumen; activa LIVE para retomar las actualizaciones.";}
            }
        }
        if(walking&&overlay!=null&&Input.GetMouseButtonDown(0)&&GetComponent<DesktopWalkthrough>()?.Paused==false)SelectUnderCrosshair();
        if(overlay!=null)
        {
            if(!walking&&picker?.Selected!=null)
            {
                if(picker.Selected.data!=null)selectedId=picker.Selected.data.id;
                else if(picker.Selected.isWall)
                {
                    if(!selectableById.TryGetValue(selectedId,out var mapped)||mapped!=picker.Selected)
                    {var wall=UnityData.Structure.walls.FirstOrDefault(w=>w.id==picker.Selected.wallId);selectedId=wall?.analysisElements?.FirstOrDefault()??0;}
                }
            }
            overlay.Animate(Time.unscaledTime,stage,selectedId,inside);
        }
    }
    private bool savedLive;
    private void StartMovie()
    {
        if(result==null||overlay==null||orbit==null||DesktopWalkthrough.IsActive){status="Calcula y traza una respuesta antes del recorrido; la cámara automática no se usa en primera persona.";return;}
        if(!cameraSaved){savedCameraTarget=orbit.target!=null?orbit.target.position:modelBounds.center;savedCameraDistance=orbit.distance;savedYaw=orbit.TraceYaw;savedPitch=orbit.TracePitch;cameraSaved=true;}
        savedLive=live;live=false;cinematic=true;movieClock=0;lastCameraStage=-1;stage=0;
        status="Recorrido de 18 s sobre una respuesta congelada. Los pulsos explican conectividad; no son propagación temporal del esfuerzo.";
    }
    private void FocusStage(int phase)
    {
        Vector3 point=modelBounds.center;float distance=Mathf.Max(30,modelBounds.size.magnitude*.78f);float yaw=45,pitch=28;
        var slab=graph.Slabs[frame.Slab];var origin=new Vector3(frame.X,slab.z,frame.Y);
        if(phase==0){point=modelBounds.center;distance=Mathf.Max(30,modelBounds.size.magnitude*.78f);}
        if(phase==1){point=new Vector3((slab.x0+slab.x1)*.5f,slab.z,(slab.y0+slab.y1)*.5f);distance=Mathf.Max(16,Mathf.Max(Math.Abs(slab.x1-slab.x0),Math.Abs(slab.y1-slab.y0))*2);pitch=48;}
        int id=phase==2?result.PrimaryBeam:phase==3?result.Path?.Edges.FirstOrDefault(e=>e.Type=="columna"||e.Type=="muro_eq")?.Id??0:0;
        if(id>0&&graph.Members.TryGetValue(id,out var edge))
        {var a=Position(graph.Nodes[edge.I]);var b=Position(graph.Nodes[edge.J]);point=(a+b)*.5f;distance=Mathf.Max(17,(b-a).magnitude*2.2f);yaw=CameraYaw(point,distance);pitch=25;}
        if(phase==4&&result.Path!=null){point=Position(graph.Nodes[result.Path.Support]);distance=19;pitch=22;yaw=CameraYaw(point,distance);}
        orbit.BeginTraceFocus(point,Mathf.Clamp(distance,16,190),yaw,pitch);
    }
    private float CameraYaw(Vector3 point,float distance)
    {
        float best=45;int minimum=int.MaxValue;
        foreach(float yaw in new[]{45f,135f,225f,315f})
        {
            var position=point+Quaternion.Euler(25,yaw,0)*new Vector3(0,0,-distance);
            int hits=0;foreach(var hit in Physics.RaycastAll(position,(point-position).normalized,distance,~0,QueryTriggerInteraction.Ignore))
                if(hit.collider.GetComponent<ElementSelectable>()!=null||hit.collider.GetComponent<SlabSelectable>()!=null)hits++;
            if(hits<minimum){minimum=hits;best=yaw;}
        }
        return best;
    }
    private void StopMovie(bool restoreCamera)
    {
        if(cinematic)live=savedLive;cinematic=false;orbit?.CancelRadarFocus();stage=5;
        if(restoreCamera&&cameraSaved&&orbit!=null&&!DesktopWalkthrough.IsActive)orbit.BeginTraceFocus(savedCameraTarget,savedCameraDistance,savedYaw,savedPitch);
        if(restoreCamera)cameraSaved=false;
    }
    private void Select(int id)
    {
        StopMovie(false);selectedId=id;
        if(!selectableById.TryGetValue(id,out var e)){status=graph.Explain(result,id);return;}
        viewer.RevealSearchTarget(e,null);picker?.SelectElement(e,false);ElementResultsPanel.OpenXRayExplanation(e);
        if(orbit!=null&&!DesktopWalkthrough.IsActive&&graph.Members.TryGetValue(id,out var edge))
        {
            var a=Position(graph.Nodes[edge.I]);var b=Position(graph.Nodes[edge.J]);orbit.BeginTraceFocus((a+b)*.5f,Mathf.Max(17,(b-a).magnitude*2),45,25);
        }
    }
    private void SelectUnderCrosshair()
    {
        if(Camera.main==null||result==null)return;
        Ray ray=Camera.main.ViewportPointToRay(new Vector3(.5f,.5f));int best=0;float distance=float.MaxValue;
        foreach(int id in result.Visible)if(graph.Members.TryGetValue(id,out var edge)&&edge.Physical)
        {
            var a=Position(graph.Nodes[edge.I]);var b=Position(graph.Nodes[edge.J]);
            for(int i=0;i<=12;i++)
            {
                var p=Vector3.Lerp(a,b,i/12f);float along=Vector3.Dot(p-ray.origin,ray.direction);
                if(along<=0||along>35)continue;float off=(ray.origin+ray.direction*along-p).magnitude;
                if(off<.65f&&along<distance){best=id;distance=along;}
            }
        }
        if(best!=0){selectedId=best;whyWalking=true;GetComponent<DesktopWalkthrough>()?.SetPaused(true);}
    }
    public string ExplainSelected(ElementSelectable selected)
    {
        if(result==null||graph==null)return "Aplica la persona y activa STRUCTURAL X-RAY en Capas para consultar un incremento calculado.";
        int id=selected?.data!=null?selected.data.id:selectedId;
        if(selected?.isWall==true)
        {
            if(selectableById.TryGetValue(selectedId,out var mapped)&&mapped==selected)id=selectedId;
            else{var wall=UnityData.Structure.walls.FirstOrDefault(w=>w.id==selected.wallId);id=wall?.analysisElements?.FirstOrDefault()??0;}
        }
        string details=graph.Explain(result,id);
        if(result.Path!=null)
        {
            var reaction=frame.Reactions?.FirstOrDefault(r=>r.declared&&r.node==result.Path.Support);
            if(reaction!=null)details+=$"\nAPOYO DE LA RUTA N{reaction.node} · reacción incremental global calculada:\nFx={reaction.fx:0.######}, Fy={reaction.fy:0.######}, Fz={reaction.fz:0.######} kN\nMx={reaction.mx:0.######}, My={reaction.my:0.######}, Mz={reaction.mz:0.######} kN·m";
        }
        return details+"\nHuella del modelo: "+frame.SourceHash+"\nFuente: mobile_slab_worker.py · localForce y desplazamientos incrementales; aproximación nodal.";
    }
    private void EndRendering()
    {
        if(overlay==null&&suspended.Count==0&&!followSaved&&!suppressSaved&&!cinematic&&!cameraSaved)return;
        StopMovie(true);overlay?.Dispose();overlay=null;
        viewer?.SetXRayView(false);
        foreach(var item in suspended)if(item.Key!=null)
        {
            var walking=GetComponent<DesktopWalkthrough>();
            if(walking!=null&&walking.Active)walking.SetRestoredBehaviourState(item.Key,item.Value);else item.Key.enabled=item.Value;
        }
        suspended.Clear();
        if(mobile!=null)
        {
            if(suppressSaved)mobile.SetPresentationSuppressed(savedSuppression);
            if(followSaved)mobile.followPerson=savedFollow;
        }
        suppressSaved=followSaved=false;
        if(!DesktopWalkthrough.IsActive&&!string.IsNullOrEmpty(savedDiagram))GetComponent<DiagramController>()?.SetResultMode(savedDiagram.StartsWith("Deformada real")?"Deformada real":savedDiagram);
        savedDiagram=null;whyWalking=false;
    }
    public void RefreshContextForWalk()
    {
        if(overlay==null||result==null)return;
        StopMovie(false);overlay.Dispose();overlay=new StructuralXRayOverlay(transform);overlay.Bind(graph,result,showConstraints);
    }
    public void Shutdown()
    {
        wantTrace=open=false;EndRendering();frame=null;result=null;pendingResponse=null;workerRefreshRequested=false;
    }
    private void OnDisable(){Shutdown();if(mobile!=null)mobile.ResponseApplied-=OnResponse;mobile=null;if(instance==this)instance=null;}
    private void OnEnable(){instance=this;}
    private void OnDestroy(){if(hudTexture!=null)Destroy(hudTexture);}
}
