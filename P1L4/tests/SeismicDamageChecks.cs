using System;
using System.IO;
using System.Linq;
using System.Text.Json;

// Standalone checks compile the real numerical Unity source, not a copied algorithm.
public class NodeData { public int id; public float x,y,z; }
public static class SeismicDamageChecks
{
    private static int count;
    private static void Check(bool ok,string name) { count++;if(!ok) throw new Exception("FAIL: "+name); }
    private static float[] Constant(double n,double my,double mz)
    {
        // Local resisting end actions, section N tension positive.
        return new[] {(float)-n,0,0,0,(float)my,(float)mz,(float)n,0,0,0,(float)-my,(float)-mz};
    }
    private static void Close(double actual,double expected,string name,double tol=1e-5) => Check(Math.Abs(actual-expected)<tol,name);
    private static SeismicDamageProfile Fixture() => new SeismicDamageProfile {
        Width=.6,Height=.8,FcMPa=25,Check=SeismicPMCheck.IndependentAxes,
        Curve=new[]{new SeismicCapacityPoint(-1000,0),new SeismicCapacityPoint(0,200),new SeismicCapacityPoint(1000,0)}
    };
    private static void UnitChecks()
    {
        var p=Fixture();
        Close(p.RuptureMPa,3.15,"fr en MPa");
        double capacity;
        Check(SeismicDamageEvaluator.TryMomentCapacity(p.Curve,500,out capacity),"interpolación disponible");
        Close(capacity,100,"capacidad interpolada");
        Check(!SeismicDamageEvaluator.TryMomentCapacity(p.Curve,1001,out capacity),"no usar punto cercano fuera de curva");
        var pure=SeismicDamageEvaluator.Evaluate(Constant(-1000,0,0),8,p);
        Close(pure.DCR,1,"polo axial correcto");
        Check(pure.AxialLimit && pure.State==SeismicDemandState.NEAR_CAPACITY,"polo axial no es NORMAL");
        var outside=SeismicDamageEvaluator.Evaluate(Constant(-1200,0,0),8,p);
        Close(outside.DCR,1.2,"excedencia axial");
        Check(outside.State==SeismicDemandState.CAPACITY_EXCEEDED,"estado excedencia axial");
        var bending=SeismicDamageEvaluator.Evaluate(Constant(0,210,100),8,p);
        Close(bending.DCR,1.05,"chequeos por ejes independientes, no inventar interacción");
        var missing=Fixture();missing.Curve=null;
        var result=SeismicDamageEvaluator.Evaluate(Constant(0,200,0),8,missing);
        Check(double.IsNaN(result.DCR) && result.State==SeismicDemandState.NOT_EVALUATED,"capacidad ausente no pasa como cero");
        Close(result.TensileMPa,200/(.6*.8*.8/6)/1000,"tensión elástica y unidades");
        var compressed=SeismicDamageEvaluator.Evaluate(Constant(-3000,0,0),8,p);
        Close(compressed.TensileMPa,0,"compresión pura no genera grietas flexurales");
        var section=FrameForces.Evaluate(Constant(0,250,0),8,0);
        var bottom=SeismicDamageEvaluator.FaceDemand(section,p,0);
        var top=SeismicDamageEvaluator.FaceDemand(section,p,1);
        Check(bottom.Ratio>1 && top.Ratio==0,"cara de tracción con My positivo");
        var reversed=FrameForces.Evaluate(Constant(0,-250,0),8,0);
        Check(SeismicDamageEvaluator.FaceDemand(reversed,p,1).Ratio>1 &&
            SeismicDamageEvaluator.FaceDemand(reversed,p,0).Ratio==0,"inversión de momento invierte cara");
        // Uniform 10 kN/m gravity, L=8: M(mid)=80, no beam end moment.
        float[] uniform={0,0,40,0,0,0,0,0,40,0,0,0};
        var u=SeismicDamageEvaluator.Evaluate(uniform,8,p);
        Close(u.TensileMPa,80/(.6*.8*.8/6)/1000,"fisuración dentro del vano, no solo extremos");
        var history=SeismicDamageEvaluator.Build(4,f=>Constant(0,new[]{0,150,220,0}[f],0),8,p);
        Check(history.FirstCrack==2 && history.FirstExceeded==2,"primer instante crítico");
        Check(history.CrackAt(0,0).Ratio==0 && history.CrackAt(1,0).Ratio<1,"sin grietas futuras");
        Check(history.CrackAt(3,0).Ratio>=history.CrackAt(2,0).Ratio,"historial gráfico conserva máximos");
        Check(history.PeakDCRFrame[3]==2 && history.Samples[3].DCR==0,"separa actual de historial");
        float[] inconsistent=Constant(0,10,0);inconsistent[10]=0;
        Check(!SeismicDamageEvaluator.LocalEquilibrium(inconsistent,8),"rechaza reconstrucción que no verifica equilibrio");
        try {SeismicDamageEvaluator.Evaluate(inconsistent,8,p);Check(false,"debía rechazar");}
        catch(ArgumentException) {Check(true,"reconstrucción rechazada");}
    }
    private sealed class Response : IDisposable
    {
        public readonly JsonDocument Meta;
        public readonly BinaryReader File;
        public readonly int Frames,Stride,Nodes;
        public readonly JsonElement[] Members;
        public Response(string path)
        {
            Meta=JsonDocument.Parse(System.IO.File.ReadAllText(path));var m=Meta.RootElement;
            Frames=m.GetProperty("frameCount").GetInt32();Stride=m.GetProperty("stride").GetInt32();Nodes=m.GetProperty("nodeTags").GetArrayLength();
            Members=m.GetProperty("members").EnumerateArray().ToArray();
            File=new BinaryReader(System.IO.File.OpenRead(Path.Combine(Path.GetDirectoryName(path),m.GetProperty("binaryFile").GetString())));
        }
        public float[] Forces(int frame,int member)
        {
            File.BaseStream.Position=20L+4L*((long)frame*Stride+1+Nodes*3+member*12);
            var f=new float[12];for(int k=0;k<12;k++) f[k]=File.ReadSingle();return f;
        }
        public void Dispose() {File.Dispose();Meta.Dispose();}
    }
    private static void RealChecks(string root)
    {
        using var model=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"P1L4/unity_visualizador/Assets/Resources/estructura_p1l4_unity.json")));
        var curves=model.RootElement.GetProperty("p1l4").GetProperty("pmCurves").EnumerateArray().ToArray();
        using var response=new Response(Path.Combine(root,"P1L4/seismic/results/el_centro_1940_ns_X_i150.json"));
        for(int frame=0;frame<response.Frames;frame+=43)
            for(int e=0;e<response.Members.Length;e++)
            {
                var member=response.Members[e];
                if(member.GetProperty("type").GetString()=="brazo_rigido") continue;
                int ni=member.GetProperty("nodeI").GetInt32(),nj=member.GetProperty("nodeJ").GetInt32();
                var nodes=model.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
                var i=nodes.First(n=>n.GetProperty("id").GetInt32()==ni);var j=nodes.First(n=>n.GetProperty("id").GetInt32()==nj);
                double length=Math.Sqrt(new[]{"x","y","z"}.Sum(c=>Math.Pow(i.GetProperty(c).GetDouble()-j.GetProperty(c).GetDouble(),2)));
                Check(SeismicDamageEvaluator.LocalEquilibrium(response.Forces(frame,e),length),"equilibrio real "+member.GetProperty("elementTag").GetString());
            }
        foreach(string type in new[]{"viga","columna","muro_eq"})
        {
            int e=Array.FindIndex(response.Members,m=>m.GetProperty("type").GetString()==type);
            var member=response.Members[e];
            double b=member.GetProperty("width_m").GetDouble(),h=member.GetProperty("height_m").GetDouble();
            var profile=new SeismicDamageProfile {Width=b,Height=h,FcMPa=type=="muro_eq" ? 30 : 25,Check=SeismicPMCheck.None};
            if(type!="viga" || curves.Any(c=>c.GetProperty("sectionId").GetString()==member.GetProperty("sectionId").GetString()))
            {
                var curve=curves.First(c=>c.GetProperty("sectionId").GetString()==(type=="viga" ? member.GetProperty("sectionId").GetString() : type=="columna" ? "COL70/70_FIBER" : "W_0.20x3.40"));
                profile.Curve=curve.GetProperty("points").EnumerateArray().Select(p=>new SeismicCapacityPoint(p.GetProperty("P_kN").GetDouble(),Math.Abs(p.GetProperty("M_kN_m").GetDouble()))).ToArray();
                profile.Check=SeismicPMCheck.My; // Current 18-bar column curve has only its exported bending axis.
            }
            var nodeArray=model.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
            var i=nodeArray.First(n=>n.GetProperty("id").GetInt32()==member.GetProperty("nodeI").GetInt32());
            var j=nodeArray.First(n=>n.GetProperty("id").GetInt32()==member.GetProperty("nodeJ").GetInt32());
            double length=Math.Sqrt(new[]{"x","y","z"}.Sum(c=>Math.Pow(i.GetProperty(c).GetDouble()-j.GetProperty(c).GetDouble(),2)));
            var history=SeismicDamageEvaluator.Build(response.Frames,f=>response.Forces(f,e),length,profile);
            if(type=="viga" && !profile.HasCapacity) Check(history.Samples.All(s=>double.IsNaN(s.DCR)),"viga sin acero no inventa capacidad");
            else Check(history.Samples.All(s=>!double.IsNaN(s.DCR)),"curva real disponible "+type);
            Console.WriteLine(type+" "+member.GetProperty("elementTag").GetString()+": fisura frame="+history.FirstCrack+", excedencia frame="+history.FirstExceeded);
        }
    }
    public static void Main(string[] args)
    {UnitChecks();RealChecks(args[0]);Console.WriteLine("PASS: "+count+" comprobaciones del evaluador real (unidades, polos, caras, historial y fuerzas Transient).");}
}
