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
        Check(rawBefore == JsonSerializer.Serialize(data.p1l4.elementForces, options), "raw JSON records must remain unmodified");
        Check(data.p1l4.elementForceCoordinates == "local", "exporter declares localForce contract");

        var fixedNodes = new HashSet<int>(reference.fixedNodes);
        foreach (var node in data.nodes)
        {
            var support = UnityData.GetNodeSupport(node.id);
            Check((support != null) == fixedNodes.Contains(node.id), "actual/inferred support node " + node.id);
        }
        foreach (var support in data.supports)
            Check(Object.ReferenceEquals(support, UnityData.GetNodeSupport(support.node)), "preserve declared constraints");
        // Modelo corregido: vigas partidas en uniones T/X, sin anclajes automaticos.
        foreach (var support in data.supports)
            Check(support.type == null || !support.type.Contains("anclaje"), "no automatic anchors " + support.node);
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

        // Regresiones (modelo con cargas distribuidas y conectividad corregida):
        // la viga E1_84 flexiona en My (vertical) y la columna E1_272 esta comprimida.
        UnityData.UseBaseCaseFactors = false;
        UnityData.TryGetSectionForces(84, "C1", .5f, out var beam);
        Near(beam.Mz, 0.0, "E1_84 center Mz (diafragma rigido: sin flexion en el plano)");
        Near(beam.My, 71.7036110959, "E1_84 center My (parabolic span moment)");
        UnityData.TryGetSectionForces(272, "C1", .5f, out var column);
        Near(column.N, -4064.5462725412, "E1_272 compression");

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

        // Razon demanda/capacidad P-M: fuera de la envolvente nunca debe dar C=0.
        var curve = new PMCurveData { points = new[] {
            new PMPoint { P_kN = -100, M_kN_m = 0 }, new PMPoint { P_kN = 0, M_kN_m = 50 },
            new PMPoint { P_kN = 200, M_kN_m = 80 }, new PMPoint { P_kN = 400, M_kN_m = 0 } } };
        Near(UnityData.CapacityRatio(curve, 100, 32.5f), 0.5, "C interpolado");
        Check(UnityData.CapacityRatio(curve, -150, 10) >= UnityData.OutOfCurveRatio, "traccion mayor que la capacidad: no cumple");
        Check(UnityData.CapacityRatio(curve, 500, 0) >= UnityData.OutOfCurveRatio, "compresion mayor que la capacidad: no cumple");
        Check(UnityData.CapacityRatio(curve, -100, 5) >= UnityData.OutOfCurveRatio, "M>0 en la punta de traccion: no cumple");

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
