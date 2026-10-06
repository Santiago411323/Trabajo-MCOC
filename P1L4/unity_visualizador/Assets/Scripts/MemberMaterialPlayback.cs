using System;

// Lectura de puntos exportados, no un nuevo análisis ni una ley de daño.
public static class MemberMaterialPlayback
{
    public sealed class State
    {
        public bool Available;
        public float Phi, Moment, Axial, Eps0, MaxSteel, MaxConcrete, CrackMoment;
        public int Index, LastIndex;
        public string Stage;
        public float StrainAt(float yMetres) { return Eps0-Phi*yMetres; } // compresión +
    }

    public static State Sample(SectionMaterialData m,PMCurveData curve,float progress)
    {
        var state=new State();
        if(m==null || curve==null || curve.momentCurvature==null ||
            curve.momentCurvature.Length==0 || m.concreteFibersY<=0 || m.h_m<=0) return state;
        var points=curve.momentCurvature;
        float limit=m.epscu<0?-m.epscu:.003f;
        int last=points.Length-1;
        for(int i=0;i<points.Length;i++)
            if(points[i]!=null && points[i].max_concrete_strain>=limit){last=i;break;}
        state.LastIndex=last;
        state.CrackMoment=.63f*(float)Math.Sqrt(m.fc_MPa)*m.b_m*m.h_m*m.h_m*1000/6;
        state.Available=true;
        int index=(int)Math.Round(Math.Max(0,Math.Min(1,progress))*(last+1))-1;
        state.Index=index;
        if(index<0){state.Stage="ORIGEN · SIN CARGA";return state;}
        var p=points[Math.Min(index,last)];
        if(p==null || p.phi_1_m<=0 || float.IsNaN(p.max_concrete_strain))
        {state.Available=false;return state;}
        state.Phi=p.phi_1_m;state.Moment=p.M_kN_m;state.Axial=p.P_kN;
        state.MaxSteel=p.max_steel_strain;state.MaxConcrete=p.max_concrete_strain;
        // La fibra más comprimida está a -h/2+h/(2ny). Recuperar exactamente eps0.
        state.Eps0=p.max_concrete_strain-p.phi_1_m*(m.h_m/2-m.h_m/(2*m.concreteFibersY));
        state.Stage=p.max_concrete_strain>=limit?"LÍMITE DEL HORMIGÓN · εcu":
            p.steel_yielded?"FLUENCIA DEL ACERO":p.M_kN_m>=state.CrackMoment?
            "SOBRE Mcr DE REFERENCIA":"BAJO Mcr DE REFERENCIA";
        return state;
    }
}
