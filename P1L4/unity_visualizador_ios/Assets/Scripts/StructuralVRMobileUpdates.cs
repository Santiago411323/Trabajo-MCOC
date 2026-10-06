using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class StructuralVRController
{
    private sealed class OriginalMember
    {
        public Vector3 Position, Scale, I, J;
        public Quaternion Rotation;
    }
    private readonly Dictionary<ElementSelectable,OriginalMember> originalMembers = new Dictionary<ElementSelectable,OriginalMember>();
    private GameObject seismicControls;
    private bool seismicWasActive, showSteel;
    private readonly SeismicCrackVisual mobileCracks = new SeismicCrackVisual();
    private SeismicDamageHistory mobileHistory;
    private SeismicResponseData historyResponse;
    private int historyId = -1;

    private void InitializeMobileWorld()
    {
        originalMembers.Clear();
        foreach(var e in world.GetComponentsInChildren<ElementSelectable>())
            if(e.data!=null)originalMembers.Add(e,new OriginalMember { Position=e.transform.position, Rotation=e.transform.rotation,
                Scale=e.transform.localScale,I=e.startPoint,J=e.endPoint });
        seismicControls = new GameObject("Controles de sismo precalculado VR");
        seismicControls.transform.SetParent(menu,false);
        string[] labels={"Play/Pausa","X/Y","Intensidad","Inicio","Escala","t + 5 s","Fisuras","Cerrar sismo"};
        Action[] callbacks={()=>MobileSeismicPlayback.Instance.TogglePlay(),()=>MobileSeismicPlayback.Instance.CycleDirection(),
            ()=>MobileSeismicPlayback.Instance.CycleIntensity(),()=>MobileSeismicPlayback.Instance.Seek(0),
            ()=>MobileSeismicPlayback.Instance.CycleScale(),()=>MobileSeismicPlayback.Instance.Seek(MobileSeismicPlayback.Instance.TimeSeconds+5),
            ()=>MobileSeismicPlayback.Instance.ShowCracks=!MobileSeismicPlayback.Instance.ShowCracks,()=>MobileSeismicPlayback.Instance.Close()};
        for(int i=0;i<labels.Length;i++)
        {
            var button=GameObject.CreatePrimitive(PrimitiveType.Cube); button.name=labels[i];button.layer=31;
            button.transform.SetParent(seismicControls.transform,false);
            button.transform.localPosition=new Vector3(1.35f+(i%2-.5f)*.48f,.23f-(i/2)*.23f,0);
            button.transform.localScale=new Vector3(.45f,.18f,.02f);
            button.GetComponent<Renderer>().sharedMaterial=MaterialFor(new Color(.25f,.13f,.06f),true);
            button.AddComponent<StructuralVRButton>().Index=actions.Count;actions.Add(callbacks[i]);
            Text(labels[i],seismicControls.transform,button.transform.localPosition+Vector3.back*.02f,.013f,labels[i]);
        }
        seismicControls.SetActive(false);
    }
    private void ToggleMobileSeismic()
    {
        StopMovement(); MobileSeismicPlayback.Instance?.Toggle();
        if(!MobileSeismicPlayback.IsActive && MobileSeismicPlayback.Instance!=null) info.text=MobileSeismicPlayback.Instance.Status;
    }
    private bool TryVRSection(float t,out FrameSectionForces force) => MobileSeismicPlayback.IsActive
        ? MobileSeismicPlayback.Instance.TrySection(selected.data.id,t,out force)
        : UnityData.TryGetSectionForces(selected.data.id,combo,t,out force);

    private void UpdateMobileWorld()
    {
        bool active=MobileSeismicPlayback.IsActive;
        if(seismicControls!=null) seismicControls.SetActive(active);
        if(active)
        {
            var playback=MobileSeismicPlayback.Instance;
            if(historyResponse!=null && historyResponse!=playback.Response)
            {mobileCracks.Clear();mobileHistory=null;historyResponse=null;historyId=-1;}
            foreach(var pair in originalMembers)
            {
                var e=pair.Key;
                if(e==null || !playback.Response.MemberIndex.ContainsKey(e.data.id)) continue;
                Vector3 i=playback.Position(e.data.nodeI),j=playback.Position(e.data.nodeJ),axis=j-i;
                Vector3 reference=Mathf.Abs(axis.normalized.y)>.9f?Vector3.right:Vector3.up;
                Vector3 side=Vector3.Cross(reference,axis).normalized;
                e.transform.position=(i+j)*.5f;
                e.transform.rotation=Quaternion.LookRotation(Vector3.Cross(axis.normalized,side).normalized,axis.normalized);
                e.transform.localScale=new Vector3(e.data.width_m,axis.magnitude,e.data.height_m);
                e.startPoint=i;e.endPoint=j;
            }
            UpdateTitle(); title.text+="\n"+playback.Summary+" · escala visual "+playback.VisualScale.ToString("0")+"x";
            if(selected!=null)
            {
                RefreshResult();
                if(playback.ShowCracks)
                {
                    if(mobileHistory==null || historyResponse!=playback.Response || historyId!=selected.data.id)
                    {
                        mobileCracks.Clear(); var response=playback.Response;
                        int member=response.MemberIndex[selected.data.id];
                        UnityData.TryGetFrameGeometry(selected.data.id,out var geometry);
                        mobileHistory=SeismicDamageEvaluator.Build(response.Metadata.frameCount,frame=> {
                            var f=new float[12];for(int k=0;k<12;k++)f[k]=response.ReadEndForce(frame,member,k);return f;
                        },geometry.Length,MobileCapacity.Profile(response.Metadata.members[member]));
                        historyResponse=response;historyId=selected.data.id;
                    }
                    mobileCracks.Update(selected.transform,mobileHistory,playback.Frame,selected.data.width_m,selected.data.height_m,true);
                    mobileCracks.ConfigureRendering(30,Resources.Load<Shader>("VRMenuOverlay"));
                }
                else mobileCracks.Clear();
            }
            else info.text=playback.Summary+"\nMira un elemento para esfuerzos y capacidad\nModelo elastico · no predice colapso";
        }
        else if(seismicWasActive)
        {
            foreach(var pair in originalMembers)
            {
                if(pair.Key==null)continue;
                pair.Key.transform.SetPositionAndRotation(pair.Value.Position,pair.Value.Rotation);
                pair.Key.transform.localScale=pair.Value.Scale;pair.Key.startPoint=pair.Value.I;pair.Key.endPoint=pair.Value.J;
            }
            mobileCracks.Clear();mobileHistory=null;historyResponse=null;historyId=-1;
            UpdateTitle(); if(selected!=null)RefreshResult();
        }
        seismicWasActive=active;
    }
    private string CapacitySummary()
    {
        if(!MobileSeismicPlayback.IsActive || selected==null) return "";
        var p=MobileSeismicPlayback.Instance;
        if(!p.Response.MemberIndex.TryGetValue(selected.data.id,out int index))return "Sin respuesta para este ID";
        UnityData.TryGetFrameGeometry(selected.data.id,out var geometry);
        var profile=MobileCapacity.Profile(p.Response.Metadata.members[index]);
        var sample=SeismicDamageEvaluator.Evaluate(p.Forces(selected.data.id),geometry.Length,profile);
        return (profile.HasCapacity ? "DCR nominal="+sample.DCR.ToString("0.00")+" · "+profile.Check : "Capacidad N/D")+
            "\nFisuracion estimada: "+(profile.HasCracking?(sample.CrackRatio>1?"umbral superado":"bajo umbral"):"N/D")+" · sin colapso";
    }
    private static bool OwnedByDecoration(Renderer r) => r.GetComponentInParent<VisualSiteTerrain>()!=null ||
        r.GetComponentInParent<VisualFrameFacade>()!=null || r.GetComponentInParent<VisualFlatRoof>()!=null ||
        r.GetComponentInParent<VisualStairs>()!=null || r.GetComponentInParent<VisualCafe>()!=null || r.GetComponentInParent<VisualCampusSite>()!=null || r.GetComponentInParent<VisualStudyRoom>()!=null;
    private void ResetMobileWorld()
    {
        MobileSeismicPlayback.Instance?.Close();mobileCracks.Clear();mobileHistory=null;historyResponse=null;
        originalMembers.Clear();historyId=-1;seismicWasActive=false;seismicControls=null;
    }
}
