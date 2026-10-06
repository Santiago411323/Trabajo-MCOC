using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class DesignComparisonLoads
{
    public string Label, Combo;
    public float G, Q, EX, EY;
    public float[] Factors => new[] {G,Q,EX,EY};
}

// Consumes immutable exported states. No access to active UnityData forces/mobile increments.
public sealed class DesignComparisonSnapshot
{
    public readonly StructureData Data;
    public readonly string Source, Fingerprint, CapturedUtc;
    public readonly Dictionary<int,NodeData> Nodes=new Dictionary<int,NodeData>();
    public readonly Dictionary<string,ElementData> Members=new Dictionary<string,ElementData>();
    public readonly Dictionary<int,WallData> Walls=new Dictionary<int,WallData>();
    private readonly Dictionary<string,Dictionary<int,float[]>> forces=new Dictionary<string,Dictionary<int,float[]>>();
    private readonly Dictionary<string,Dictionary<int,DisplacementRecord>> displacements=new Dictionary<string,Dictionary<int,DisplacementRecord>>();
    private readonly Dictionary<string,PMCurveData> curves=new Dictionary<string,PMCurveData>();
    private readonly Dictionary<string,SectionMaterialData> materials=new Dictionary<string,SectionMaterialData>();
    public static string Key(ElementData e)=>!string.IsNullOrEmpty(e.elementTag)?e.elementTag:e.id.ToString();

    public DesignComparisonSnapshot(StructureData frozenData,string source,string fingerprint)
    {
        Data=frozenData;Source=source;Fingerprint=fingerprint;CapturedUtc=DateTime.UtcNow.ToString("o");
        foreach(var n in Data.nodes??Array.Empty<NodeData>())Nodes.Add(n.id,n);
        foreach(var e in Data.elements??Array.Empty<ElementData>())Members.Add(Key(e),e);
        foreach(var w in Data.walls??Array.Empty<WallData>())Walls.Add(w.id,w);
        foreach(var row in Data.p1l4?.elementForces??Array.Empty<ElementForceRecord>())
        {
            if(!forces.TryGetValue(row.combo,out var byId))forces[row.combo]=byId=new Dictionary<int,float[]>();
            byId.Add(row.id,row.f);
        }
        foreach(var row in Data.p1l4?.displacements??Array.Empty<DisplacementRecord>())
        {
            if(!displacements.TryGetValue(row.combo,out var byId))displacements[row.combo]=byId=new Dictionary<int,DisplacementRecord>();
            byId.Add(row.node,row);
        }
        foreach(var c in Data.p1l4?.pmCurves??Array.Empty<PMCurveData>())curves[c.sectionId]=c;
        foreach(var m in Data.p1l4?.sectionMaterials??Array.Empty<SectionMaterialData>())materials[m.sectionId]=m;
    }

    public string Validate()
    {
        if(Nodes.Count==0||Members.Count==0)return "Modelo sin geometría calculable.";
        foreach(string name in new[]{"G","Q","EX","EY"})
        {
            if(!forces.TryGetValue(name,out var fs)||fs.Count==0||!displacements.TryGetValue(name,out var ds)||ds.Count==0)
                return "Falta respuesta exportada de "+name+".";
            foreach(var f in fs.Values)if(!FrameForces.IsValid(f))return "Acciones no válidas en "+name+".";
            foreach(var d in ds.Values)if(!DiaphragmKinematics.Finite(d))return "Desplazamiento no válido en "+name+".";
        }
        return null;
    }

    public string CompatibleWith(DesignComparisonSnapshot other)
    {
        if(other==null)return "Falta el estado DESPUÉS.";
        if(Nodes.Count!=other.Nodes.Count||Members.Count!=other.Members.Count||Walls.Count!=other.Walls.Count)
            return "Cambió la topología del modelo; este duelo requiere los mismos elementos/nodos.";
        if(Math.Abs(Data.Q_kN_m2-other.Data.Q_kN_m2)>1e-6 || Math.Abs(Data.seismic_coefficient-other.Data.seismic_coefficient)>1e-6)
            return "Cambió la sobrecarga o el coeficiente sísmico de base; no se mezclan escenarios.";
        if(Data.units!=other.Data.units||Math.Abs(Data.q_G-other.Data.q_G)>1e-6)return "Cambiaron unidades o carga permanente superficial.";
        foreach(var pair in Nodes)
        {
            if(!other.Nodes.TryGetValue(pair.Key,out var n))return "Nodo distinto entre estados.";
            var a=pair.Value;if(Math.Abs(a.x-n.x)+Math.Abs(a.y-n.y)+Math.Abs(a.z-n.z)>.0001)
                return "Cambió la geometría nodal; este duelo compara cambios de sección/armadura.";
        }
        foreach(var pair in Members)
        {
            var a=pair.Value;
            if(!other.Members.TryGetValue(pair.Key,out var b)||a.nodeI!=b.nodeI||a.nodeJ!=b.nodeJ||a.type!=b.type||a.id!=b.id)
                return "Identidad o conectividad distinta: "+pair.Key;
        }
        foreach(var pair in Walls)
            if(!other.Walls.TryGetValue(pair.Key,out var w)||pair.Value.nodeI!=w.nodeI||pair.Value.nodeJ!=w.nodeJ||pair.Value.sourceId!=w.sourceId)
                return "Cambió la identidad/conectividad de un muro.";
        var supports=new Dictionary<int,string>();
        foreach(var s in Data.supports??Array.Empty<SupportData>())supports[s.node]=$"{s.ux}{s.uy}{s.uz}{s.rx}{s.ry}{s.rz}";
        var others=other.Data.supports??Array.Empty<SupportData>();if(supports.Count!=others.Length)return "Cambiaron los apoyos.";
        foreach(var s in others)if(!supports.TryGetValue(s.node,out var v)||v!=$"{s.ux}{s.uy}{s.uz}{s.rx}{s.ry}{s.rz}")return "Cambiaron restricciones de apoyo.";
        var groups=Data.p1l4?.analysisModel?.diafragmas??Array.Empty<RigidDiaphragmRecord>();
        var otherGroups=other.Data.p1l4?.analysisModel?.diafragmas??Array.Empty<RigidDiaphragmRecord>();
        if(groups.Length!=otherGroups.Length)return "Cambiaron diafragmas.";
        for(int i=0;i<groups.Length;i++)
        {
            var a=groups[i];var b=otherGroups[i];
            if(a.master!=b.master||a.normalAxis!=b.normalAxis||a.slaves!=b.slaves)return "Cambió la restricción de diafragma.";
            if(Math.Abs(a.x-b.x)+Math.Abs(a.y-b.y)+Math.Abs(a.z-b.z)>.0001)return "Cambió el maestro de un diafragma.";
            if(!Same(a.constrainedDofs,b.constrainedDofs)||!Same(a.masterFixity,b.masterFixity))return "Cambiaron los GDL del diafragma.";
            var aa=a.slaveTags??Array.Empty<int>();var bb=b.slaveTags??Array.Empty<int>();
            if(aa.Length!=bb.Length)return "Cambió la membresía de diafragma.";
            for(int j=0;j<aa.Length;j++)if(aa[j]!=bb[j])return "Cambió la membresía de diafragma.";
        }
        return null;
    }
    private static bool Same(int[] a,int[] b)
    {
        a=a??Array.Empty<int>();b=b??Array.Empty<int>();if(a.Length!=b.Length)return false;
        for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;
    }

    public bool TryFrame(string key,out ElementData member,out FrameGeometry frame)
    {
        frame=null;if(!Members.TryGetValue(key,out member))return false;
        return Nodes.TryGetValue(member.nodeI,out var i)&&Nodes.TryGetValue(member.nodeJ,out var j)&&FrameGeometry.TryCreate(i,j,out frame);
    }
    public float[] Forces(string key,DesignComparisonLoads loads)
    {
        if(!TryFrame(key,out var e,out var frame))return null;
        var total=new float[12];string[] names={"G","Q","EX","EY"};var factors=loads.Factors;
        bool any=false;
        for(int i=0;i<4;i++)if(factors[i]!=0)
        {
            if(!forces.TryGetValue(names[i],out var rows)||!rows.TryGetValue(e.id,out var f)||!FrameForces.IsValid(f))return null;
            if(Data.p1l4.elementForceCoordinates!="local")f=frame.ToLocal(f);
            for(int j=0;j<12;j++)total[j]+=f[j]*factors[i];any=true;
        }
        if(!any && (!forces.TryGetValue("G",out var g)||!g.ContainsKey(e.id)))return null;
        return total;
    }
    public bool TryDisplacement(int node,DesignComparisonLoads loads,out DisplacementRecord result)
    {
        result=new DisplacementRecord {node=node};string[] names={"G","Q","EX","EY"};var factors=loads.Factors;bool any=false;
        for(int i=0;i<4;i++)if(factors[i]!=0)
        {
            if(!displacements.TryGetValue(names[i],out var rows)||!rows.TryGetValue(node,out var d))return false;
            float f=factors[i];result.ux+=f*d.ux;result.uy+=f*d.uy;result.uz+=f*d.uz;
            result.rx+=f*d.rx;result.ry+=f*d.ry;result.rz+=f*d.rz;any=true;
        }
        return any || displacements.TryGetValue("G",out var g)&&g.ContainsKey(node);
    }
    public PMCurveData Curve(ElementData e)
    {
        string id=!string.IsNullOrEmpty(e.sectionId)?e.sectionId:e.seccion;
        if(curves.TryGetValue(id,out var curve))return curve;
        if(e.type=="columna" && curves.TryGetValue(id+"_FIBER",out curve))return curve;
        return null;
    }
    public PMCurveData WallCurve(WallData w)
    {
        foreach(var r in Data.p1l4?.wallRegistry??Array.Empty<WallRegistryEntry>())
            if(r.nodeI==w.nodeI && r.nodeJ==w.nodeJ && curves.TryGetValue(r.pmSectionId,out var c))return c;
        return null;
    }
    public SectionMaterialData Material(ElementData e)
    {
        string id=!string.IsNullOrEmpty(e.sectionId)?e.sectionId:e.seccion;
        if(materials.TryGetValue(id,out var m) && m.steelBars>0)return m;
        return materials.TryGetValue(id+"_FIBER",out m)?m:null;
    }
    public Vector3 Position(NodeData n)=>new Vector3(n.x,n.z,n.y);
    public Vector3 MemberDisplacement(string key,DesignComparisonLoads loads,float t)
    {
        if(!TryFrame(key,out var e,out var frame) || !TryDisplacement(e.nodeI,loads,out var di)||!TryDisplacement(e.nodeJ,loads,out var dj))return Vector3.zero;
        Vector3 a=Position(Nodes[e.nodeI]),b=Position(Nodes[e.nodeJ]);
        return MemberPreviewKinematics.Interpolate(new Vector3(di.ux,di.uz,di.uy),new Vector3(dj.ux,dj.uz,dj.uy),
            new Vector3(di.rx,di.ry,di.rz),new Vector3(dj.rx,dj.ry,dj.rz),b-a,(float)frame.Length,t,true);
    }
    public Vector3 MemberPoint(string key,DesignComparisonLoads loads,float t,float scale)
    {
        if(!TryFrame(key,out var e,out var frame))return Vector3.zero;
        Vector3 a=Position(Nodes[e.nodeI]),b=Position(Nodes[e.nodeJ]);
        var u=MemberDisplacement(key,loads,t);
        return Vector3.Lerp(a,b,t)+u*scale;
    }
}

public static class DesignComparisonReading
{
    public sealed class Metrics
    {
        public bool Available, CapacityAvailable, OutsideCurve;
        public float Moment, Shear, Axial, DisplacementMm, DCR, CapacityP0, SteelMm2, SelfWeightKN, Area;
        public float Width, Height;public int Bars;public float Diameter;
    }
    public static Metrics Evaluate(DesignComparisonSnapshot snapshot,string key,DesignComparisonLoads loads)
    {
        var r=new Metrics();if(!snapshot.TryFrame(key,out var e,out var frame))return r;
        var forces=snapshot.Forces(key,loads);
        if(!FrameForces.IsValid(forces)||!snapshot.TryDisplacement(e.nodeI,loads,out _)||!snapshot.TryDisplacement(e.nodeJ,loads,out _))return r;
        var curve=snapshot.Curve(e);var mat=snapshot.Material(e);
        r.Available=true;r.Width=e.width_m;r.Height=e.height_m;r.Area=e.width_m*e.height_m;
        r.SelfWeightKN=(float)(25*e.width_m*(e.type=="viga"?Math.Max(0,e.height_m-.15):e.height_m)*frame.Length);
        if(mat!=null){r.SteelMm2=mat.Ast_mm2;r.Bars=mat.steelBars;r.Diameter=mat.barDiameter_mm;}
        var capacity=DemandRadarRanking.Evaluate(forces,frame.Length,curve,DemandRadarRanking.Metric.Capacity);
        r.CapacityAvailable=capacity.Available;r.OutsideCurve=capacity.OutsideCurve;r.DCR=capacity.Value;
        if(capacity.Available)
        {
            float probe=UnityData.CapacityRatio(curve,0,1);
            if(probe>0&&probe<UnityData.OutOfCurveRatio)r.CapacityP0=1/probe;
        }
        for(int i=0;i<=60;i++)
        {
            float t=i/60f;var f=FrameForces.Evaluate(forces,frame.Length,t);
            r.Moment=Math.Max(r.Moment,(float)Math.Sqrt((double)f.My*f.My+(double)f.Mz*f.Mz));
            r.Shear=Math.Max(r.Shear,(float)Math.Sqrt((double)f.Vy*f.Vy+(double)f.Vz*f.Vz));r.Axial=Math.Max(r.Axial,Math.Abs(f.N));
            var u=snapshot.MemberDisplacement(key,loads,t);r.DisplacementMm=Math.Max(r.DisplacementMm,u.magnitude*1000);
        }
        return r;
    }
    public static bool TryPercent(float before,float after,out float percent)
    {
        percent=0;if(Math.Abs(before)<1e-7 || float.IsNaN(before+after)||float.IsInfinity(before+after))return false;
        percent=100*(after-before)/Math.Abs(before);return true;
    }
}
