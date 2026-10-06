using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Static only for the current Play session: survives SceneManager reload, not an application restart.
public static class DesignComparisonSession
{
    public static DesignComparisonSnapshot Before {get;private set;}
    public static DesignComparisonSnapshot After {get;private set;}
    public static DesignComparisonLoads Loads {get;private set;}
    public static string Status {get;private set;}="Guarda ANTES, edita y reanaliza para comparar dos diseños.";
    public static string PendingSelection;
    public static bool Rendering;
    public static int Revision {get;private set;}
    public static string EditedKey {get;private set;}
    private static readonly System.Collections.Generic.Dictionary<string,DesignComparisonReading.Metrics> beforeReadings=new System.Collections.Generic.Dictionary<string,DesignComparisonReading.Metrics>();
    private static readonly System.Collections.Generic.Dictionary<string,DesignComparisonReading.Metrics> afterReadings=new System.Collections.Generic.Dictionary<string,DesignComparisonReading.Metrics>();
    public static bool Ready=>Before!=null && After!=null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset(){Before=After=null;Loads=null;Rendering=false;PendingSelection=EditedKey=null;Invalidate();Status="Guarda ANTES, edita y reanaliza para comparar dos diseños.";}
    private static void Invalidate(){Revision++;beforeReadings.Clear();afterReadings.Clear();}
    private static DesignComparisonSnapshot Freeze(StructureData data,string source)
    {
        string json=JsonUtility.ToJson(data);
        using(var sha=SHA256.Create())
        {
            string hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-","").ToLowerInvariant();
            return new DesignComparisonSnapshot(JsonUtility.FromJson<StructureData>(json),source,hash);
        }
    }
    private static DesignComparisonLoads CurrentLoads()
    {
        if(UnityData.UseBaseCaseFactors)return new DesignComparisonLoads{G=UnityData.FactorG,Q=UnityData.FactorQ,EX=UnityData.FactorEX,EY=UnityData.FactorEY,Label=UnityData.GetActiveLoadLabel()};
        var c=UnityData.GetComboInfo(UnityData.ActiveCombo);
        if(c!=null)return new DesignComparisonLoads{Combo=c.name,Label=c.label,G=c.G,Q=c.Q,EX=c.EX,EY=c.EY};
        string name=UnityData.ActiveCombo;
        if(name=="G"||name=="Q"||name=="EX"||name=="EY")return new DesignComparisonLoads{Combo=name,Label=name,G=name=="G"?1:0,Q=name=="Q"?1:0,EX=name=="EX"?1:0,EY=name=="EY"?1:0};
        return new DesignComparisonLoads{Combo="C1",Label="C1: G+0.5Q+0.3EX+0.2EY",G=1,Q=.5f,EX=.3f,EY=.2f};
    }
    public static bool CaptureBefore(bool replace)
    {
        if(Before!=null && !replace)return true;
        if(StructuralModelEditor.ResultsStale){Status="No se guarda una vista previa como ANTES. Reanaliza primero.";return false;}
        if(SeismicPlaybackController.IsActive || UnityData.MobileForces.Count>0 || (UnityData.ActiveCombo??"").StartsWith("LRFD_"))
        {Status="Guarda ANTES en un caso estático G/Q/EX/EY o C1/C2/C3, sin incremento móvil.";return false;}
        try
        {
            var snapshot=Freeze(UnityData.Structure,DesktopModelFile.ActivePath);
            string issue=snapshot.Validate();if(issue!=null){Status=issue;return false;}
            Before=snapshot;After=null;Loads=CurrentLoads();PendingSelection=null;Invalidate();
            Status="ANTES congelado. Edita una sección/armadura y ejecuta REANALIZAR OPENSEES.";return true;
        }
        catch(Exception e){Status="No se pudo guardar ANTES: "+e.Message;return false;}
    }
    public static void PreparingEdit(string key)
    {
        if(Before==null)CaptureBefore(false);
        if(Before!=null){After=null;EditedKey=key;PendingSelection=null;Invalidate();Status="ANTES conservado · DESPUÉS pendiente de reanálisis.";}
    }
    public static void AcceptAfter(StructureData computed,string source)
    {
        if(Before==null)return;
        try
        {
            var snapshot=Freeze(computed,source);
            string issue=snapshot.Validate()??Before.CompatibleWith(snapshot);
            if(issue!=null){After=null;Status="No se comparan estados incompatibles: "+issue;Invalidate();return;}
            After=snapshot;PendingSelection=EditedKey;Invalidate();Status="DOS DISEÑOS CALCULADOS · misma geometría, apoyos y factores. G puede cambiar por el peso propio.";
        }
        catch(Exception e){After=null;Status="Comparación pendiente: "+e.Message;Invalidate();}
    }
    public static void Failed(){After=null;Invalidate();Status="El reanálisis falló. ANTES conservado; no se inventa un DESPUÉS.";}
    public static void SetLoads(DesignComparisonLoads loads){Loads=loads;Invalidate();}
    public static DesignComparisonReading.Metrics Reading(string key,bool after)
    {
        var cache=after?afterReadings:beforeReadings;var snapshot=after?After:Before;
        if(snapshot==null||Loads==null)return new DesignComparisonReading.Metrics();
        if(!cache.TryGetValue(key,out var r))cache[key]=r=DesignComparisonReading.Evaluate(snapshot,key,Loads);
        return r;
    }
}
