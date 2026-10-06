using System;

// Combinaciones de los apuntes aportados por el usuario; no certificación normativa.
public static class LrfdScenario
{
    public static readonly string[] Names={"1 · 1,4D","2 · 1,2D + 1,6L + 0,5A",
        "3 · 1,2D + 1,6A + (L / 0,8W)","4 · 1,2D + 1,6W + L + 0,5A",
        "5 · 1,2D + 1,4E + L + 0,2S","6 · 0,9D + 1,6W","7 · 0,9D + 1,4E"};
    // D,L,Lr,S,R,W,E. A es una alternativa, no la suma de las tres.
    public static float[] Factors(int combination,int alternative,bool accompanyingWind)
    {
        if(combination<0 || combination>6 || alternative<0 || alternative>2)throw new ArgumentOutOfRangeException();
        var f=new float[7];int a=2+alternative;
        switch(combination)
        {
            case 0:f[0]=1.4f;break;
            case 1:f[0]=1.2f;f[1]=1.6f;f[a]=.5f;break;
            case 2:f[0]=1.2f;f[a]=1.6f;f[accompanyingWind?5:1]=accompanyingWind?.8f:1;break;
            case 3:f[0]=1.2f;f[5]=1.6f;f[1]=1;f[a]=.5f;break;
            case 4:f[0]=1.2f;f[6]=1.4f;f[1]=1;f[3]=.2f;break;
            case 5:f[0]=.9f;f[5]=1.6f;break;
            case 6:f[0]=.9f;f[6]=1.4f;break;
        }
        return f;
    }
    public static float SnowPressure(float depthMetres,float densityKgM3)=>depthMetres*densityKgM3*9.80665f/1000;
    public static float WaterPressure(float depthMetres)=>depthMetres*9.80665f;
    public static float WindPressure(float speedMetresSecond,float coefficient)=>.613f*speedMetresSecond*speedMetresSecond*coefficient/1000;
    public static float RetainedDepth(float rateMmHour,float seconds,float timeScale)=>rateMmHour/1000*seconds*timeScale/3600;
    public static bool HasUnavailableResponse(float[] factors,float roof,float snow,float water,float wind,float seismic)
    {return factors[2]*roof!=0 || factors[3]*snow!=0 || factors[4]*water!=0 || factors[5]*wind!=0 || factors[6]*seismic!=0;}
}
