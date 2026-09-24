using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

// Runs the real UnityData/FrameForces source outside the editor. No mock math.
public static class UnityForceChecks
{
    // Legacy sourceId fields mix strings and numbers; Unity's loader accepts them.
    private class IdentifierConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            return reader.TokenType == JsonTokenType.Number
                ? reader.GetDouble().ToString(CultureInfo.InvariantCulture) : reader.GetString();
        }
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
    public class Reference
    {
        public Record[] records;
        public int[] fixedNodes;
    }
    public class Record
    {
        public string combo;
        public int id;
        public double[] local;
        public double length;
        public double[][] points;
    }
    private static int checks;
    private static double maxDifference;

    private static void Check(bool condition, string name)
    {
        checks++;
        if (!condition) throw new Exception(name);
    }
    private static void Near(double actual, double expected, string name, double tolerance = .01)
    {
        double error = Math.Abs(actual-expected);
        maxDifference = Math.Max(maxDifference, error);
        Check(!double.IsNaN(error) && error <= tolerance, $"{name}: {actual} != {expected}");
    }

    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static int Run(string[] args)
    {
        var surface=new SlabData {x0=0,y0=0,x1=10,y1=5,openings=new[]{new SlabOpening{x0=4,x1=4.01f,y0=1,y1=4}}};
        Check(surface.Contains(0,0),"slab boundary valid");
        Check(!surface.Contains(-.01f,2),"outside slab rejected");
        Check(!surface.Contains(float.NaN,2),"invalid coordinate rejected");
        Check(!surface.Contains(4.005f,2),"opening rejected");
        Check(!surface.CanMove(2,2,6,2),"cannot jump across narrow opening");
        Check(surface.CanMove(2,.5f,6,.5f),"path around opening valid");
        var nextSurface=new SlabData {x0=10,y0=0,x1=14,y1=5,z=0};
        var upperSurface=new SlabData {x0=0,y0=5,x1=10,y1=9,z=0};
        var disconnectedSurface=new SlabData {x0=14.2f,y0=0,x1=18,y1=5,z=0};
        var surfaces=new[]{surface,nextSurface,upperSurface,disconnectedSurface};
        Check(Object.ReferenceEquals(SlabNavigation.FindAdjacent(surface,surfaces,10.01f,2,1,0),nextSurface),"walk to connected right slab");
        Check(Object.ReferenceEquals(SlabNavigation.FindAdjacent(surface,surfaces,2,5.01f,0,1),upperSurface),"walk to connected upper slab");
        Check(SlabNavigation.FindAdjacent(nextSurface,surfaces,14.21f,2,1,0)==null,"gap is not automatic connectivity");
        Check(SlabNavigation.FindAdjacent(surface,surfaces,10.01f,2,-1,0)==null,"direction must match shared edge");
        var options = new JsonSerializerOptions { IncludeFields = true };
        options.Converters.Add(new IdentifierConverter());
        string json = File.ReadAllText(args[0]);
        var data = JsonSerializer.Deserialize<StructureData>(json, options);
        var reference = JsonSerializer.Deserialize<Reference>(File.ReadAllText(args[1]), options);
        string rawBefore = JsonSerializer.Serialize(data.p1l4.elementForces, options);
        UnityData.LoadData(data);
        foreach (Record r in reference.records)
        {
            float[] local = UnityData.GetElementForces(r.combo, r.id);
            Check(local != null, $"Missing {r.combo}/{r.id}");
            for (int c = 0; c < 12; c++) Near(local[c], r.local[c], $"local {r.combo}/{r.id}/{c}");
            Check(UnityData.TryGetFrameGeometry(r.id, out var frame), "geometry");
            Near(frame.Length, r.length, "analytic length", 1e-5);
            for (int i = 0; i < 5; i++)
            {
                Check(UnityData.TryGetSectionForces(r.id, r.combo, i*.25f, out var section), "section data");
                for (int c = 0; c < 6; c++) Near(section.Component(c), r.points[i][c], $"section {r.combo}/{r.id}/{i}/{c}");
            }
            // Correct sign for compression-positive P-M at the same section I.
            UnityData.TryGetSectionForces(r.id, r.combo, 0, out var atI);
            Near(-atI.N, r.local[0], "P=-N=local FxI");
        }
        Check(reference.records.Length == data.p1l4.elementForces.Length, "all exported records tested");
        Check(rawBefore == JsonSerializer.Serialize(data.p1l4.elementForces, options), "raw JSON must remain global/unmodified");

        var fixedNodes = new HashSet<int>(reference.fixedNodes);
        foreach (var node in data.nodes)
        {
            var support = UnityData.GetNodeSupport(node.id);
            Check((support != null) == fixedNodes.Contains(node.id), "actual/inferred support node " + node.id);
        }
        foreach (var support in data.supports)
            Check(Object.ReferenceEquals(support, UnityData.GetNodeSupport(support.node)), "preserve declared constraints");
        Check(UnityData.GetNodeSupport(261).type.Contains("inferido"), "automatic anchor is labelled");
        Check(UnityData.GetNodeSupport(54) == null, "ordinary beam joint is not a ground support");

        // Superposition must use local actions with their signs, not magnitudes.
        UnityData.UseBaseCaseFactors = true;
        foreach (var combo in data.p1l4.combinations)
        {
            UnityData.FactorG = combo.G; UnityData.FactorQ = combo.Q;
            UnityData.FactorEX = combo.EX; UnityData.FactorEY = combo.EY;
            foreach (var element in data.elements)
            {
                var expected = UnityData.GetElementForcesForCase(combo.name, element.id);
                var actual = UnityData.GetElementForces(combo.name, element.id);
                for (int c = 0; c < 12; c++) Near(actual[c], expected[c], "superposition " + combo.name);
            }
        }
        UnityData.FactorG = UnityData.FactorQ = UnityData.FactorEX = UnityData.FactorEY = 0;
        foreach (var e in data.elements)
            for (int i = 0; i < 5; i++)
            {
                Check(UnityData.TryGetSectionForces(e.id, "C1", i*.25f, out var zero), "zero load data");
                for (int c = 0; c < 6; c++) Near(zero.Component(c), 0, "zero factors: no phantom parabola", 0);
            }

        // Signed arbitrary combination and exact-case lookup while sliders are active.
        UnityData.FactorG = -1; UnityData.FactorQ = 2; UnityData.FactorEX = -.7f; UnityData.FactorEY = .9f;
        foreach (var e in data.elements)
        {
            float[] actual = UnityData.GetElementForces("C1", e.id);
            float[] g = UnityData.GetElementForcesForCase("G", e.id);
            float[] q = UnityData.GetElementForcesForCase("Q", e.id);
            float[] ex = UnityData.GetElementForcesForCase("EX", e.id);
            float[] ey = UnityData.GetElementForcesForCase("EY", e.id);
            for (int c = 0; c < 12; c++) Near(actual[c], -g[c]+2*q[c]-.7*ex[c]+.9*ey[c], "signed factors");
        }

        // Regression from the audit: the vertical column's axial force is not Fx global.
        UnityData.UseBaseCaseFactors = false;
        UnityData.TryGetSectionForces(84, "C1", .5f, out var beam);
        Near(beam.Mz, -8.5362158184, "E1_84 center Mz");
        float[] baseForces=(float[])UnityData.GetElementForces("C1",84).Clone();
        var extra=new float[12];extra[0]=-2;extra[6]=2;
        UnityData.MobileForces[84]=extra;
        UnityData.TryGetSectionForces(84,"C1",.5f,out var loaded);
        Near(loaded.N,beam.N+2,"global mobile increment added exactly once");
        Near(UnityData.GetElementForcesForCase("C1",84)[0],baseForces[0],"base result immutable");
        UnityData.MobileForces.Clear();
        Near(UnityData.GetElementForces("C1",84)[0],baseForces[0],"deactivation restores baseline");
        UnityData.TryGetSectionForces(272, "C1", .5f, out var column);
        Near(column.N, -2620.7558614332, "E1_272 compression");

        // Uniform-load reference beam: only legitimate end actions generate curvature.
        var fixedBeam = new float[12];
        fixedBeam[2]=fixedBeam[8]=30; fixedBeam[4]=-30; fixedBeam[10]=30;
        Near(FrameForces.Evaluate(fixedBeam, 6, 0).My, -30, "fixed I");
        Near(FrameForces.Evaluate(fixedBeam, 6, .5f).My, 15, "uniform midspan");
        Near(FrameForces.Evaluate(fixedBeam, 6, 1).My, -30, "fixed J");
        Near(FrameForces.Evaluate(fixedBeam, 6, 1).Vz, -30, "shear J sign");

        // Moving point load used by the Unity table: P=100 kN at midspan, L=10 m.
        var mobileI = FrameForces.EvaluateFixedFixedPointLoad(100, 10, .5f, 0);
        var mobileCenter = FrameForces.EvaluateFixedFixedPointLoad(100, 10, .5f, .5f);
        var mobileJ = FrameForces.EvaluateFixedFixedPointLoad(100, 10, .5f, 1);
        Near(mobileI.Vz, 50, "mobile reaction I");
        Near(mobileI.My, -125, "mobile fixed moment I");
        Near(mobileCenter.Vz, -50, "mobile shear after point load");
        Near(mobileCenter.My, 125, "mobile center moment");
        Near(mobileJ.My, -125, "mobile fixed moment J");

        // Invalid or absent forces must not be replaced with legacy approximate data.
        data.p1l4.elementForces = data.p1l4.elementForces.Where(r => !(r.combo=="Q" && r.id==84)).ToArray();
        UnityData.LoadData(data);
        UnityData.UseBaseCaseFactors = true;
        UnityData.FactorG=0; UnityData.FactorQ=1; UnityData.FactorEX=UnityData.FactorEY=0;
        Check(UnityData.GetElementForces("C1",84)==null, "missing contributing case rejected");
        UnityData.FactorQ=0;
        Check(UnityData.GetElementForces("C1",84).All(v=>v==0), "zero factor need not have missing case");

        // Explicit local exports must not be rotated a second time.
        data = JsonSerializer.Deserialize<StructureData>(json, options);
        var lookup = reference.records.ToDictionary(r => (r.combo,r.id));
        foreach (var r in data.p1l4.elementForces) r.f = lookup[(r.combo,r.id)].local.Select(v=>(float)v).ToArray();
        data.p1l4.elementForceCoordinates = "local";
        UnityData.LoadData(data);
        for (int c=0;c<12;c++) Near(UnityData.GetElementForces("C1",84)[c], lookup[("C1",84)].local[c], "local contract");
        data.p1l4.elementForceCoordinates = "unsupported";
        UnityData.LoadData(data);
        Check(UnityData.GetElementForces("C1",84)==null, "unknown coordinates rejected");
        data.p1l4.elementForceCoordinates = "local";
        data.p1l4.elementForces.First(r=>r.id==84 && r.combo=="C1").f[0] = float.NaN;
        UnityData.LoadData(data);
        Check(UnityData.GetElementForces("C1",84)==null, "NaN rejected");
        Check(!FrameGeometry.TryCreate(new NodeData(),new NodeData(),out var invalid), "zero length rejected");

        Console.WriteLine($"PASS: {checks} checks; {reference.records.Length} OpenSees records; maximum difference {maxDifference:G6} (tolerance 0.01 kN or kN*m).");
        return 0;
    }
}
