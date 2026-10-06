using System;
using System.IO;
using System.Linq;
using System.Text.Json;

public static class DesignComparisonChecks
{
    static int count;
    static void Check(bool condition,string message){count++;if(!condition)throw new Exception("Design comparison: "+message);}
    static void Close(double a,double b,double tolerance,string message){Check(Math.Abs(a-b)<=tolerance,message);}
    public static void Run(string directory,JsonSerializerOptions options)
    {
        string Read(string name)=>File.ReadAllText(Path.Combine(directory,name));
        StructureData Clone(StructureData d)=>JsonSerializer.Deserialize<StructureData>(JsonSerializer.Serialize(d,options),options);
        var before=JsonSerializer.Deserialize<StructureData>(Read("before.json"),options);
        var after=JsonSerializer.Deserialize<StructureData>(Read("after.json"),options);
        var a=new DesignComparisonSnapshot(before,"before", "a");var b=new DesignComparisonSnapshot(after,"after", "b");
        var loads=new DesignComparisonLoads{G=1,Q=.5f,EX=.3f,EY=.2f,Combo="C1"};
        Check(a.Validate()==null&&b.Validate()==null,"Both endpoints are complete exported responses");
        Check(a.CompatibleWith(b)==null,"Width/reinforcement edits preserve compatibility");
        string key="E1_72";var fa=a.Forces(key,loads);var fb=b.Forces(key,loads);
        var expected=JsonDocument.Parse(Read("expected.json")).RootElement.GetProperty("forcesC1").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
        for(int i=0;i<12;i++)Close(fb[i],expected[i],.002,"After forces agree with explicit OpenSees C1 component "+i);
        var original=before.p1l4.elementForces.First(r=>r.combo=="C1"&&r.id==a.Members[key].id);
        for(int i=0;i<12;i++)Close(fa[i],original.f[i],.002,"Before base superposition agrees with stored C1");
        var ma=DesignComparisonReading.Evaluate(a,key,loads);var mb=DesignComparisonReading.Evaluate(b,key,loads);
        Check(ma.Available&&mb.Available,"Real selected beam metrics available");
        Close(ma.Width,.6,1e-6,"Before width");Close(mb.Width,.7,1e-6,"After width");
        Close(ma.SteelMm2,12*Math.PI*25*25/4,.1,"Before real reinforcement");Close(mb.SteelMm2,12*Math.PI*28*28/4,.1,"After real reinforcement");
        Check(ma.CapacityAvailable&&mb.CapacityAvailable,"Nominal P-My available on both curves");
        Check(Math.Abs(ma.Moment-mb.Moment)>.001,"Section edit changes OpenSees response");
        Check(mb.SelfWeightKN>ma.SelfWeightKN,"Larger section changes computed self-weight reference");
        int affected=0;
        foreach(var pair in a.Members)if(pair.Key!=key&&(pair.Value.type=="viga"||pair.Value.type=="columna"))
        {
            var oldR=DesignComparisonReading.Evaluate(a,pair.Key,loads);var newR=DesignComparisonReading.Evaluate(b,pair.Key,loads);
            if(oldR.Available&&newR.Available&&Math.Abs(oldR.Moment-newR.Moment)>.0001)affected++;
        }
        Check(affected>1,"Redistribution shown on real other members, not only selected beam");
        Check(!DesignComparisonReading.TryPercent(0,2,out _),"No fake infinity percent for zero baseline");
        Check(DesignComparisonReading.TryPercent(100,80,out var percent),"Valid percent difference");Close(percent,-20,1e-5,"Signed reduction");
        a.TryFrame(key,out var member,out var frame);a.TryDisplacement(member.nodeI,loads,out var di);a.TryDisplacement(member.nodeJ,loads,out var dj);
        var ui=a.MemberDisplacement(key,loads,0);var uj=a.MemberDisplacement(key,loads,1);
        Close(ui.x,di.ux,1e-7,"Interpolated displacement preserves I");Close(ui.y,di.uz,1e-7,"Unity axis conversion I");Close(uj.z,dj.uy,1e-7,"Interpolated displacement preserves J");
        var changed=Clone(after);changed.nodes[0].x+=1;
        Check(a.CompatibleWith(new DesignComparisonSnapshot(changed,"bad", "c"))!=null,"Reject geometry changes");
        changed=Clone(after);changed.supports[0].ux=1-changed.supports[0].ux;
        Check(a.CompatibleWith(new DesignComparisonSnapshot(changed,"bad", "c"))!=null,"Reject changed supports");
        changed=Clone(after);changed.Q_kN_m2+=1;
        Check(a.CompatibleWith(new DesignComparisonSnapshot(changed,"bad", "c"))!=null,"Reject changed physical load scenario");
        changed=Clone(after);changed.p1l4.analysisModel.diafragmas[0].masterFixity[0]=1;
        Check(a.CompatibleWith(new DesignComparisonSnapshot(changed,"bad", "c"))!=null,"Reject changed diaphragm fixity");
        changed=Clone(after);changed.p1l4.elementForces=changed.p1l4.elementForces.Where(r=>r.combo!="EX").ToArray();
        var missing=new DesignComparisonSnapshot(changed,"bad", "c");
        Check(missing.Validate()!=null && missing.Forces(key,loads)==null,"Missing case is not replaced by zero");
        changed=Clone(after);changed.p1l4.displacements=changed.p1l4.displacements.Where(r=>r.node!=member.nodeI).ToArray();
        missing=new DesignComparisonSnapshot(changed,"bad", "c");
        Check(!DesignComparisonReading.Evaluate(missing,key,loads).Available,"Missing node response is not a zero displacement");
        // Synthetic contract check: material metadata cannot alter the global force response.
        changed=Clone(before);var mat=changed.p1l4.sectionMaterials.First(m=>m.sectionId==member.sectionId);mat.Ast_mm2*=2;
        var metadata=new DesignComparisonSnapshot(changed,"unit_contract", "d");
        var sameForces=metadata.Forces(key,loads);
        for(int i=0;i<12;i++)Close(fa[i],sameForces[i],0,"Reinforcement metadata does not scale forces");
        Console.WriteLine("PASS: "+count+" before/after checks; "+affected+" other members respond to the isolated section change.");
    }
}
