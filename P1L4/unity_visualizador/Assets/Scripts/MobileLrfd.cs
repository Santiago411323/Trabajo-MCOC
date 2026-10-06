using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

// Superposición lineal de nueve casos OpenSees precalculados; nunca ejecuta Python.
public sealed class MobileLrfd
{
    public LrfdInput Input = new LrfdInput { d=1,l=1,roof=.5f,snowDepth=.1f,waterDepth=.03f,snowDensity=300,windSpeed=25,windCoefficient=1,windAngle=45,e=1 };
    private LrfdDataset units;
    public LrfdDataset Dataset {get;private set;}
    public string Status {get;private set;} = "Escenario independiente · respuestas OpenSees precalculadas";
    public bool Current => Dataset!=null && JsonUtility.ToJson(Input)==JsonUtility.ToJson(Dataset.scenario);
    public bool Load()
    {
        try
        {
            var resource=Resources.Load<TextAsset>("MobileLrfdUnits");
            var model=Resources.Load<TextAsset>("estructura_p1l4_unity");
            if(resource==null || model==null)throw new Exception("Faltan unidades LRFD empaquetadas.");
            var data=JsonUtility.FromJson<LrfdDataset>(resource.text);
            using(var sha=SHA256.Create())
                if(BitConverter.ToString(sha.ComputeHash(model.bytes)).Replace("-","").ToLowerInvariant()!=data.modelHash)
                    throw new Exception("Unidades LRFD de otro modelo; vuelva a exportar.");
            string[] required={"G","Q","EX","EY","Roof","WXp","WXm","WYp","WYm"};
            if(data.variants==null || required.Any(n=>data.variants.Count(v=>v.name==n)!=1) ||
                !Finite(data.equilibriumError) || data.equilibriumError>.02f)throw new Exception("Unidades o equilibrio inválidos.");
            foreach(var v in data.variants)
            {
                if(v.forces==null || v.displacements==null || v.forces.Length==0 || v.displacements.Length==0 ||
                   v.forces.Any(f=>!FrameForces.IsValid(f.f)) || v.displacements.Any(d=>!Finite(d.ux)||!Finite(d.uy)||!Finite(d.uz)||!Finite(d.rx)||!Finite(d.ry)||!Finite(d.rz)))
                    throw new Exception("Respuesta unitaria incompleta.");
            }
            units=data;return true;
        }
        catch(Exception e){units=null;Dataset=null;Status=e.Message;return false;}
    }
    public bool Evaluate()
    {
        if(units==null && !Load())return false;
        float[] values={Input.d,Input.l,Input.roof,Input.snowDepth,Input.waterDepth,Input.snowDensity,Input.windSpeed,Input.windCoefficient,Input.windAngle,Input.e};
        if(values.Any(v=>!Finite(v)||v<0)){Dataset=null;Status="Intensidades inválidas.";return false;}
        try
        {
            var rows=new List<LrfdVariant>();
            float[] alternatives={Input.roof,LrfdScenario.SnowPressure(Input.snowDepth,Input.snowDensity),LrfdScenario.WaterPressure(Input.waterDepth)};
            float radians=Input.windAngle*Mathf.Deg2Rad,x=Mathf.Cos(radians),y=-Mathf.Sin(radians);
            float wind=LrfdScenario.WindPressure(Input.windSpeed,Input.windCoefficient);
            for(int u=0;u<7;u++)
            for(int a=0;a<((u>=1&&u<=3)?3:1);a++)
            for(int c=0;c<(u==2?2:1);c++)
            for(int direction=0;direction<((u==4||u==6)?4:1);direction++)
            {
                int alternative=u==4?1:a;var f=LrfdScenario.Factors(u,alternative,c==1);
                var recipe=new Dictionary<string,float>{{"G",f[0]*Input.d},{"Q",f[1]*Input.l},
                    {"Roof",f[2]*alternatives[0]+f[3]*alternatives[1]+f[4]*alternatives[2]},
                    {x>=0?"WXp":"WXm",f[5]*wind*Mathf.Abs(x)}, {y>=0?"WYp":"WYm",f[5]*wind*Mathf.Abs(y)},
                    {direction<2?"EX":"EY",f[6]*Input.e*(direction%2==0?1:-1)}};
                string label="U"+(u+1)+" · A="+new[]{"Lr","S","R"}[alternative]+" · acomp="+(c==1?"W":"L")+
                    ((u==4||u==6)?" · "+new[]{"EX+","EX-","EY+","EY-"}[direction]:"");
                rows.Add(Combine(recipe,u+1,"LRFD_MOBILE_"+rows.Count,label));
            }
            Dataset=new LrfdDataset{modelHash=units.modelHash,roofArea=units.roofArea,equilibriumError=units.equilibriumError,
                scenario=JsonUtility.FromJson<LrfdInput>(JsonUtility.ToJson(Input)),variants=rows.ToArray(),
                notes="Cubierta equivalente y fachada nodal idealizada. E=±EX/±EY. P–My parcial; no certifica seguridad ni colapso."};
            foreach(var row in rows)UnityData.RegisterLrfdCase(row);
            Status="22 variantes U1–U7 · OpenSees precalculado · equilibrio verificado";return true;
        }
        catch(Exception e){Dataset=null;Status=e.Message;return false;}
    }
    private LrfdVariant Combine(Dictionary<string,float> recipe,int u,string name,string label)
    {
        var active=recipe.Where(p=>p.Value!=0).Select(p=>(unit:units.variants.Single(v=>v.name==p.Key),factor:p.Value)).ToArray();
        if(active.Length==0)active=new[]{(unit:units.variants.Single(v=>v.name=="G"),factor:0f)};
        var forces=active.Select(a=>a.unit.forces.ToDictionary(r=>r.id)).ToArray();
        var nodes=active.Select(a=>a.unit.displacements.ToDictionary(r=>r.node)).ToArray();
        var factors=active.Select(a=>a.factor).ToArray();
        var ids=forces[0].Keys.Where(id=>forces.All(m=>m.ContainsKey(id))).OrderBy(id=>id);
        var nodeIds=nodes[0].Keys.Where(id=>nodes.All(m=>m.ContainsKey(id))).OrderBy(id=>id);
        var result=new LrfdVariant{u=u,name=name,label=label};
        result.forces=ids.Select(id=>new LrfdForce{id=id,f=Enumerable.Range(0,12).Select(k=>(float)active.Select((a,i)=>(double)forces[i][id].f[k]*a.factor).Sum()).ToArray()}).ToArray();
        result.displacements=nodeIds.Select(id=>new DisplacementRecord{node=id,combo=name,
            ux=Sum(nodes,factors,id,d=>d.ux),uy=Sum(nodes,factors,id,d=>d.uy),
            uz=Sum(nodes,factors,id,d=>d.uz),rx=Sum(nodes,factors,id,d=>d.rx),
            ry=Sum(nodes,factors,id,d=>d.ry),rz=Sum(nodes,factors,id,d=>d.rz)}).ToArray();
        return result;
    }
    private static float Sum(Dictionary<int,DisplacementRecord>[] nodes,float[] factors,int id,Func<DisplacementRecord,float> field)
        =>(float)nodes.Select((map,i)=>(double)field(map[id])*factors[i]).Sum();
    private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    public sealed class Row
    {public LrfdVariant Variant;public FrameSectionForces Forces;public float Position,Ratio;public bool HasCapacity;}
    private readonly Dictionary<string,LrfdDesignCapacity> capacities=new Dictionary<string,LrfdDesignCapacity>();
    public Row[] Rows(ElementData element)
    {
        var rows=new Row[7];if(!Current || element==null || !UnityData.TryGetFrameGeometry(element.id,out var frame))return rows;
        string section=MobileStructuralTools.SectionId(element);var m=UnityData.GetMaterial(section);
        string key=section+":"+JsonUtility.ToJson(m);
        if(!capacities.TryGetValue(key,out var capacity)){capacity=new LrfdDesignCapacity(m,element.type=="columna");capacities[key]=capacity;}
        foreach(var variant in Dataset.variants)
        {
            var record=variant.forces.FirstOrDefault(f=>f.id==element.id);if(record==null)continue;
            for(int i=0;i<=40;i++)
            {
                var force=FrameForces.Evaluate(record.f,frame.Length,i/40f);
                float ratio=capacity.Available?capacity.Ratio(-force.N,force.My):Mathf.Abs(force.My);
                int index=variant.u-1;
                if(rows[index]==null||ratio>rows[index].Ratio)rows[index]=new Row{Variant=variant,Forces=force,Ratio=ratio,Position=i/40f,HasCapacity=capacity.Available};
            }
        }
        return rows;
    }
}
