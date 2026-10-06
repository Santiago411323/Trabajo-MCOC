using System;
using System.Collections.Generic;

// Numerical criteria only. No Unity animation, guessed reinforcement or fracture.
public enum SeismicDemandState { NOT_EVALUATED, NORMAL, HIGH_DEMAND, NEAR_CAPACITY, CAPACITY_EXCEEDED }
public enum SeismicPMCheck { None, My, Mz, IndependentAxes }
public struct SeismicCapacityPoint
{
    public double P, M;
    public SeismicCapacityPoint(double p, double m) { P=p; M=m; }
}
public sealed class SeismicDamageProfile
{
    public double Width, Height, FcMPa;
    public SeismicCapacityPoint[] Curve;
    public SeismicPMCheck Check;
    public string Source, Limitation;
    public bool HasCracking => Width>0 && Height>0 && FcMPa>0 &&
        !double.IsNaN(FcMPa) && !double.IsInfinity(FcMPa) &&
        !double.IsInfinity(Width) && !double.IsInfinity(Height);
    // Project criterion for normal-weight concrete; gross elastic section.
    public double RuptureMPa => HasCracking ? .63*Math.Sqrt(FcMPa) : double.NaN;
    public bool HasCapacity => Check != SeismicPMCheck.None && SeismicDamageEvaluator.ValidCurve(Curve);
}
public struct SeismicDamageSample
{
    public double DCR, Demand, Capacity, Station, TensileMPa, CrackRatio;
    public bool AxialLimit;
    public SeismicDemandState State;
}
public struct SeismicCrackDemand
{
    public float Ratio, Depth, StartEdge;
}
public sealed class SeismicDamageHistory
{
    public const int CrackStations = 9;
    public const int CrackSlots = CrackStations*4;
    public SeismicDamageProfile Profile;
    public SeismicDamageSample[] Samples;
    public SeismicCrackDemand[] CrackPeaks;
    public int[] PeakDCRFrame;
    public int FirstCrack=-1, FirstHigh=-1, FirstNear=-1, FirstExceeded=-1;
    public SeismicCrackDemand CrackAt(int frame,int slot) => CrackPeaks[frame*CrackSlots+slot];
}

public static class SeismicDamageEvaluator
{
    public static bool ValidCurve(SeismicCapacityPoint[] curve)
    {
        if (curve == null || curve.Length<2) return false;
        double min=double.PositiveInfinity,max=double.NegativeInfinity;
        foreach (var p in curve)
        {
            if (!Finite(p.P) || !Finite(p.M) || p.M<0) return false;
            min=Math.Min(min,p.P); max=Math.Max(max,p.P);
        }
        return max>min;
    }
    private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

    // Returns no capacity when the axial coordinate is outside the envelope.
    // A zero capacity at a pole is preserved, never replaced by a nearby point.
    public static bool TryMomentCapacity(SeismicCapacityPoint[] curve,double p,out double capacity)
    {
        capacity=0;
        if (!ValidCurve(curve) || !Finite(p)) return false;
        double min=double.PositiveInfinity,max=double.NegativeInfinity;
        foreach(var point in curve) { min=Math.Min(min,point.P);max=Math.Max(max,point.P); }
        if(p<min-1e-6 || p>max+1e-6) return false;
        p=Math.Max(min,Math.Min(max,p));
        bool found=false;
        for(int i=0;i<curve.Length-1;i++)
        {
            var a=curve[i]; var b=curve[i+1];
            if(p<Math.Min(a.P,b.P)-1e-6 || p>Math.Max(a.P,b.P)+1e-6) continue;
            if(Math.Abs(b.P-a.P)<1e-9)
            { if(Math.Abs(p-a.P)<1e-6) {capacity=Math.Max(capacity,Math.Max(a.M,b.M));found=true;} continue; }
            double t=(p-a.P)/(b.P-a.P);
            capacity=Math.Max(capacity,a.M+(b.M-a.M)*t); found=true;
        }
        return found;
    }

    public static SeismicDemandState Classify(double dcr)
    {
        if(double.IsNaN(dcr) || dcr<0) return SeismicDemandState.NOT_EVALUATED;
        if(dcr>1+1e-6) return SeismicDemandState.CAPACITY_EXCEEDED;
        if(dcr>=.85) return SeismicDemandState.NEAR_CAPACITY;
        return dcr>=.60 ? SeismicDemandState.HIGH_DEMAND : SeismicDemandState.NORMAL;
    }

    public static bool LocalEquilibrium(float[] f,double length)
    {
        if(!FrameForces.IsValid(f) || length<=0) return false;
        // Valid for the current Transient model: all inertia is NODAL, element
        // mass is zero and element loads are uniform G+0.5Q. Reject other contracts.
        double qy=(f[1]+(double)f[7])/length,qz=(f[2]+(double)f[8])/length;
        double scale=Math.Max(1,Math.Max(Math.Abs(f[4])+Math.Abs(f[10]),Math.Abs(f[5])+Math.Abs(f[11])));
        double tolerance=Math.Max(.005,scale*2e-5);
        return Math.Abs(f[4]+f[2]*length-.5*qz*length*length+f[10])<=tolerance &&
               Math.Abs(f[5]-f[1]*length+.5*qy*length*length+f[11])<=tolerance;
    }

    public static SeismicDamageSample Evaluate(float[] f,double length,SeismicDamageProfile profile)
    {
        if(!LocalEquilibrium(f,length)) throw new ArgumentException("No se verifica equilibrio local para reconstruir la sección.");
        var result=new SeismicDamageSample { DCR=double.NaN,CrackRatio=double.NaN,TensileMPa=double.NaN,
            State=SeismicDemandState.NOT_EVALUATED };
        double maxTension=0;
        // Discrete spatial check, documented: 41 sections including I and J.
        for(int k=0;k<=40;k++)
        {
            float s=k/40f;
            FrameSectionForces section=FrameForces.Evaluate(f,length,s);
            if(profile.HasCracking)
            {
                double sigma=TensileStress(section,profile);
                if(sigma>maxTension) {maxTension=sigma;if(!profile.HasCapacity) result.Station=s;}
            }
            if(!profile.HasCapacity) continue;
            double p=-section.N,cap;
            double min=double.PositiveInfinity,max=double.NegativeInfinity;
            foreach(var point in profile.Curve) {min=Math.Min(min,point.P);max=Math.Max(max,point.P);}
            double demand=profile.Check == SeismicPMCheck.My ? Math.Abs(section.My) :
                profile.Check == SeismicPMCheck.Mz ? Math.Abs(section.Mz) : Math.Max(Math.Abs(section.My),Math.Abs(section.Mz));
            bool axial=!TryMomentCapacity(profile.Curve,p,out cap);
            double ratio;
            if(axial)
            {
                cap=Math.Abs(p<min ? min : max); demand=Math.Abs(p);
                ratio=cap>1e-9 ? demand/cap : double.PositiveInfinity;
            }
            else
            {
                ratio=cap>1e-9 ? demand/cap : demand>1e-6 ? double.PositiveInfinity : 0;
                // Preserve the axial pole: P=Pn,M=0 has utilization 1, not zero.
                double axialCap=Math.Abs(p<0 ? min : max);
                double axialRatio=axialCap>1e-9 ? Math.Abs(p)/axialCap : Math.Abs(p)>1e-6 ? double.PositiveInfinity : 0;
                if(axialRatio>=ratio) {ratio=axialRatio;cap=axialCap;demand=Math.Abs(p);axial=true;}
            }
            if(double.IsNaN(result.DCR) || ratio>result.DCR)
            {
                result.DCR=ratio;result.Demand=demand;result.Capacity=cap;result.Station=s;result.AxialLimit=axial;
            }
        }
        if(profile.HasCracking)
        { result.TensileMPa=maxTension;result.CrackRatio=maxTension/profile.RuptureMPa; }
        result.State=Classify(result.DCR);
        return result;
    }
    public static double TensileStress(FrameSectionForces section,SeismicDamageProfile p) =>
        Math.Max(0,(section.N/(p.Width*p.Height)+
            6*Math.Abs(section.My)/(p.Width*p.Height*p.Height)+
            6*Math.Abs(section.Mz)/(p.Height*p.Width*p.Width))/1000.0);

    // Face order: local z-, z+, y+, y-. Renderer local X corresponds to structural local y.
    public static SeismicCrackDemand FaceDemand(FrameSectionForces section,SeismicDamageProfile p,int face)
    {
        if(!p.HasCracking) return default;
        double n=section.N/(p.Width*p.Height)/1000.0;
        double my=6*section.My/(p.Width*p.Height*p.Height)/1000.0;
        double mz=6*section.Mz/(p.Height*p.Width*p.Width)/1000.0;
        double center=face==0 ? n+my : face==1 ? n-my : face==2 ? n+mz : n-mz;
        double cross=face<2 ? Math.Abs(mz) : Math.Abs(my);
        double maximum=Math.Max(0,center+cross);
        double depth=cross>1e-9 ? Math.Max(0,Math.Min(1,maximum/(2*cross))) : maximum>0 ? 1 : 0;
        float edge=face<2 ? (mz>=0 ? 1 : -1) : (my>=0 ? -1 : 1);
        return new SeismicCrackDemand { Ratio=(float)(maximum/p.RuptureMPa),Depth=(float)depth,StartEdge=edge };
    }

    public static SeismicDamageHistory Build(int count,Func<int,float[]> readForces,double length,SeismicDamageProfile profile)
    {
        if(count<1) throw new ArgumentException("Historia vacía.");
        var h=new SeismicDamageHistory { Profile=profile,Samples=new SeismicDamageSample[count],
            CrackPeaks=new SeismicCrackDemand[count*SeismicDamageHistory.CrackSlots],PeakDCRFrame=new int[count] };
        int peak=0;
        for(int frame=0;frame<count;frame++)
        {
            float[] f=readForces(frame);
            var result=Evaluate(f,length,profile); h.Samples[frame]=result;
            if(frame>0 && !double.IsNaN(result.DCR) && result.DCR>h.Samples[peak].DCR) peak=frame;
            h.PeakDCRFrame[frame]=peak;
            if(h.FirstCrack<0 && result.CrackRatio>1+1e-6) h.FirstCrack=frame;
            if(h.FirstHigh<0 && result.DCR>=.6) h.FirstHigh=frame;
            if(h.FirstNear<0 && result.DCR>=.85) h.FirstNear=frame;
            if(h.FirstExceeded<0 && result.DCR>1+1e-6) h.FirstExceeded=frame;
            for(int station=0;station<SeismicDamageHistory.CrackStations;station++)
            {
                var section=FrameForces.Evaluate(f,length,station/(float)(SeismicDamageHistory.CrackStations-1));
                for(int face=0;face<4;face++)
                {
                    int slot=station*4+face,index=frame*SeismicDamageHistory.CrackSlots+slot;
                    var current=FaceDemand(section,profile,face);
                    var previous=frame>0 ? h.CrackPeaks[index-SeismicDamageHistory.CrackSlots] : default;
                    h.CrackPeaks[index]=current.Ratio>previous.Ratio ? current : previous;
                }
            }
        }
        return h;
    }
}
