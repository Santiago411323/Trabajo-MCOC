using System;

// Structural coordinates (X,Y,Z), before the Unity display swaps Y and Z.
// Mirrors P1L3.build_model: Linear vecxz=(0,0,1), or (1,0,0) for vertical members.
public sealed class FrameGeometry
{
    public double Length { get; private set; }
    public double[] X { get; private set; }
    public double[] Y { get; private set; }
    public double[] Z { get; private set; }

    public static bool TryCreate(NodeData ni, NodeData nj, out FrameGeometry geometry)
    {
        geometry = null;
        if (ni == null || nj == null) return false;
        double[] x = { (double)nj.x - ni.x, (double)nj.y - ni.y, (double)nj.z - ni.z };
        double length = Math.Sqrt(Dot(x, x));
        if (double.IsNaN(length) || double.IsInfinity(length) || length < 1e-6) return false;
        for (int i = 0; i < 3; i++) x[i] /= length;
        double[] reference = Math.Abs(x[2]) > 0.90 ? new double[] { 1, 0, 0 } : new double[] { 0, 0, 1 };
        double[] y = Cross(reference, x);
        double yn = Math.Sqrt(Dot(y, y));
        for (int i = 0; i < 3; i++) y[i] /= yn;
        geometry = new FrameGeometry { Length = length, X = x, Y = y, Z = Cross(x, y) };
        return true;
    }

    public float[] ToLocal(float[] global)
    {
        if (!FrameForces.IsValid(global)) return null;
        float[] local = new float[12];
        double[][] axes = { X, Y, Z };
        for (int start = 0; start < 12; start += 3)
            for (int axis = 0; axis < 3; axis++)
                local[start + axis] = (float)(global[start] * axes[axis][0] +
                    global[start + 1] * axes[axis][1] + global[start + 2] * axes[axis][2]);
        return local;
    }

    private static double Dot(double[] a, double[] b)
    {
        return a[0]*b[0] + a[1]*b[1] + a[2]*b[2];
    }

    private static double[] Cross(double[] a, double[] b)
    {
        return new[] { a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0] };
    }
}

public struct FrameSectionForces
{
    public float N, Vy, Vz, T, My, Mz;

    public float Component(int index)
    {
        switch (index)
        {
            case 0: return N;
            case 1: return Vy;
            case 2: return Vz;
            case 3: return T;
            case 4: return My;
            case 5: return Mz;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}

public static class FrameForces
{
    public static bool IsValid(float[] forces)
    {
        if (forces == null || forces.Length != 12) return false;
        foreach (float value in forces)
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;
        return true;
    }

    // Input: local OpenSees resisting end actions, NOT section values.
    // N is tension-positive. Vy,Vz,T,My,Mz use the I-face convention;
    // their corresponding section values at J are the negatives of the J actions.
    // P1L3 applies nodal loads only: N,V,T are constant and M is linear.
    // The transverse-load terms also reproduce equilibrium for uniform eleLoads.
    // Never add ElementData.uniformLoad: it is not a load of the active analysis.
    public static FrameSectionForces Evaluate(float[] f, double length, float t)
    {
        if (!IsValid(f) || length <= 0 || double.IsNaN(length) || double.IsInfinity(length))
            throw new ArgumentException("Se requieren doce acciones locales y longitud analitica positiva.");
        double s = Math.Max(0, Math.Min(1, t));
        double x = length * s;
        double qy = ((double)f[1] + f[7]) / length;
        double qz = ((double)f[2] + f[8]) / length;
        return new FrameSectionForces {
            N = (float)(-f[0] * (1-s) + f[6]*s),
            Vy = (float)(f[1] - qy*x), Vz = (float)(f[2] - qz*x),
            T = (float)(f[3]*(1-s) - f[9]*s),
            My = (float)(f[4] + f[2]*x - .5*qz*x*x),
            Mz = (float)(f[5] - f[1]*x + .5*qy*x*x)
        };
    }
}
