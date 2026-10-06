using System;

public static class DemandRadarRanking
{
    public enum Metric { Capacity, Moment, Shear, Compression, Tension }
    public sealed class Reading
    {
        public bool Available, OutsideCurve;
        public float Value, Position, N, Vy, Vz, My, Mz;
    }
    public static Reading Evaluate(float[] forces,double length,PMCurveData curve,Metric metric)
    {
        var result=new Reading();
        if(!FrameForces.IsValid(forces) || length<=0 || double.IsNaN(length) || double.IsInfinity(length))return result;
        if(metric==Metric.Capacity && !ValidCapacity(curve))return result;
        for(int i=0;i<=40;i++)
        {
            float t=i/40f;var f=FrameForces.Evaluate(forces,length,t);
            float value=metric==Metric.Capacity?UnityData.CapacityRatio(curve,-f.N,Math.Abs(f.My)):
                metric==Metric.Moment?(float)Math.Sqrt((double)f.My*f.My+(double)f.Mz*f.Mz):
                metric==Metric.Shear?(float)Math.Sqrt((double)f.Vy*f.Vy+(double)f.Vz*f.Vz):
                metric==Metric.Compression?Math.Max(0,-f.N):Math.Max(0,f.N);
            if(float.IsNaN(value) || float.IsInfinity(value))return new Reading();
            if(!result.Available || value>result.Value)
            {
                result.Available=true;result.Value=value;result.Position=t;
                result.N=f.N;result.Vy=f.Vy;result.Vz=f.Vz;result.My=f.My;result.Mz=f.Mz;
                result.OutsideCurve=metric==Metric.Capacity && value>=UnityData.OutOfCurveRatio;
            }
        }
        return result;
    }
    private static bool ValidCapacity(PMCurveData curve)
    {
        if(curve==null || curve.points==null || curve.points.Length<2)return false;
        bool positiveMoment=false;
        foreach(var p in curve.points)
        {
            if(p==null || float.IsNaN(p.P_kN) || float.IsInfinity(p.P_kN) || float.IsNaN(p.M_kN_m) || float.IsInfinity(p.M_kN_m))return false;
            if(p.M_kN_m>0)positiveMoment=true;
        }
        return positiveMoment;
    }
}
