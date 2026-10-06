using System;
using System.Collections.Generic;

// Verificación explicativa de sección rectangular: ACI 318-08, capítulos 9/10.
// No altera fuerzas OpenSees ni sustituye la curva de capacidad exportada.
public static class ReinforcementAssessment
{
    public sealed class Bar { public double Depth, WidthPosition, Area; }
    public sealed class Result
    {
        public bool Valid, UltimateAvailable, MinimumOK, MaximumOK, IsColumn;
        public string Reason, Failure;
        public double B, H, D, Dt, As, Ag, RhoTotal, AsTensionRow, RhoFlexural,
            AsMin, AsMax, Cmax, Amax, RhoMin, RhoMaxReference, RhoBalancedReference,
            Beta1, EpsY, C, EpsT, Phi, P, Mn, Residual;
        public Bar[] Bars;
    }

    public static Result Evaluate(SectionMaterialData m, bool column, double pKN,
                                  bool reverse = false, bool rotate = false)
    {
        var r = new Result { IsColumn = column, P = pKN, Reason = "Datos insuficientes." };
        if (m == null) return r;
        r.B = (rotate ? m.h_m : m.b_m) * 1000.0;
        r.H = (rotate ? m.b_m : m.h_m) * 1000.0;
        double cover = m.cover_mm, fc = m.fc_MPa, fy = m.fy_MPa;
        double es = m.Es_MPa > 0 ? m.Es_MPa : 200000;
        int count = m.topBars + m.bottomBars + 2 * m.sideBarsEach;
        if (r.B <= 2*cover || r.H <= 2*cover || cover <= 0 || fc <= 0 || fy <= 0 ||
            m.barDiameter_mm <= 0 || m.topBars < 0 || m.bottomBars < 0 ||
            m.sideBarsEach < 0 || count <= 0 || count != m.steelBars ||
            double.IsNaN(pKN) || double.IsInfinity(pKN)) return r;
        double area = Math.PI * m.barDiameter_mm * m.barDiameter_mm / 4;
        var bars = new List<Bar>();
        double originalB = m.b_m*1000.0, originalH = m.h_m*1000.0;
        Action<double,double> add = (x,y) => {
            double depth = rotate ? x : y;
            bars.Add(new Bar { Depth = reverse ? r.H-depth : depth,
                              WidthPosition = rotate ? originalH-y : x, Area = area });
        };
        for (int i=0;i<m.topBars;i++)
            add(m.topBars==1 ? originalB/2 : cover+(originalB-2*cover)*i/(m.topBars-1),cover);
        for (int i=0;i<m.bottomBars;i++)
            add(m.bottomBars==1 ? originalB/2 : cover+(originalB-2*cover)*i/(m.bottomBars-1),originalH-cover);
        for (int i=1;i<=m.sideBarsEach;i++)
        {
            double depth = cover+(originalH-2*cover)*i/(m.sideBarsEach+1);
            add(cover,depth); add(originalB-cover,depth);
        }
        r.Bars = bars.ToArray(); r.As = count*area; r.Ag = r.B*r.H;
        r.RhoTotal = r.As/r.Ag; r.Dt = r.H-cover;
        // Comparación conservadora de mínimo: solo fila extrema traccionada.
        double sumDepth = 0;
        foreach (var bar in r.Bars)
            if (Math.Abs(bar.Depth-r.Dt)<.001) { r.AsTensionRow+=bar.Area; sumDepth+=bar.Area*bar.Depth; }
        r.D = r.AsTensionRow>0 ? sumDepth/r.AsTensionRow : r.Dt;
        r.RhoFlexural = r.AsTensionRow/(r.B*r.D);
        r.Beta1 = Math.Max(.65, .85-.05*Math.Max(0,fc-28)/7);
        r.EpsY = fy/es;
        r.RhoMin = Math.Max(.25*Math.Sqrt(fc)/fy,1.4/fy);
        // Solo referencias de una sección simplemente armada, P=0.
        r.RhoBalancedReference = .85*r.Beta1*fc/fy * .003/(.003+r.EpsY);
        r.Cmax = .375*r.D;
        r.Amax = r.Beta1*r.Cmax;
        r.AsMin = column ? .01*r.Ag : r.RhoMin*r.B*r.D;
        r.AsMax = column ? .08*r.Ag : .85*fc*r.B*r.Amax/fy;
        r.RhoMaxReference = .85*fc*r.B*r.Amax/fy/(r.B*r.D);
        r.MinimumOK = (column ? r.As : r.AsTensionRow) >= r.AsMin;
        r.Valid = true; r.Reason = null;
        double lo = r.H*1e-8, hi = r.H*1e7;
        double target = pKN*1000;
        double lower = Axial(r,fc,fy,es,lo), upper = Axial(r,fc,fy,es,hi);
        if (target <= lower || target >= upper)
        {
            r.Reason = "P fuera del dominio de equilibrio de esta sección; no se clasifica la falla.";
            r.MaximumOK = column && r.As<=r.AsMax;
            return r;
        }
        for (int i=0;i<110;i++)
        {
            double mid=(lo+hi)/2;
            if(Axial(r,fc,fy,es,mid)>target) hi=mid; else lo=mid;
        }
        r.C=(lo+hi)/2;
        r.Residual=(Axial(r,fc,fy,es,r.C)-target)/1000;
        r.EpsT=Math.Max(0,.003*(r.Dt-r.C)/r.C);
        r.UltimateAvailable=Math.Abs(r.Residual)<.001;
        r.MaximumOK = column ? r.As<=r.AsMax : r.UltimateAvailable && r.EpsT>=.004;
        r.Phi = r.EpsT<=r.EpsY ? .65 : r.EpsT>=.005 ? .90 :
            .65+.25*(r.EpsT-r.EpsY)/(.005-r.EpsY);
        r.Failure = r.EpsT<=r.EpsY ? "CONTROLADA POR COMPRESIÓN" :
            r.EpsT>=.005 ? "CONTROLADA POR TRACCIÓN" : "TRANSICIÓN";
        double a=Math.Min(r.H,r.Beta1*r.C);
        double moment=.85*fc*r.B*a*(r.H/2-a/2);
        foreach(var bar in r.Bars)
        {
            double strain=.003*(r.C-bar.Depth)/r.C;
            double stress=Math.Max(-fy,Math.Min(fy,es*strain));
            double force=stress*bar.Area-.85*fc*ConcreteOverlap(bar,a);
            moment+=force*(r.H/2-bar.Depth);
        }
        r.Mn=Math.Abs(moment)/1e6;
        return r;
    }

    private static double Axial(Result r,double fc,double fy,double es,double c)
    {
        double a=Math.Min(r.H,r.Beta1*c), force=.85*fc*r.B*a;
        foreach(var bar in r.Bars)
        {
            double stress=Math.Max(-fy,Math.Min(fy,es*.003*(c-bar.Depth)/c));
            force+=stress*bar.Area-.85*fc*ConcreteOverlap(bar,a);
        }
        return force;
    }

    private static double ConcreteOverlap(Bar bar,double a)
    {
        // Intersección circular continua: evita saltos falsos de P al cruzar una barra.
        double radius=Math.Sqrt(bar.Area/Math.PI), t=(a-bar.Depth)/radius;
        if(t<=-1) return 0;
        if(t>=1) return bar.Area;
        return bar.Area*(Math.Acos(-t)+t*Math.Sqrt(1-t*t))/Math.PI;
    }
}
