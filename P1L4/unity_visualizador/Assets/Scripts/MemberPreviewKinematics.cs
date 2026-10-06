using UnityEngine;

public static class MemberPreviewKinematics
{
    // Displacement vectors and direction in Unity (X,Z,Y of OpenSees).
    // Rotations remain in structural OpenSees XYZ; transform the cross product,
    // not the axial rotation vector, through the reflected coordinate mapping.
    public static Vector3 Interpolate(Vector3 ui,Vector3 uj,Vector3 ri,Vector3 rj,
                                      Vector3 direction,float length,float t,bool useRotations)
    {
        Vector3 dir=direction.normalized,u=Vector3.Lerp(ui,uj,t);
        if(!useRotations)return u;
        Vector3 structuralDir=new Vector3(dir.x,dir.z,dir.y);
        Vector3 ci=Vector3.Cross(ri,structuralDir),cj=Vector3.Cross(rj,structuralDir);
        Vector3 ti=new Vector3(ci.x,ci.z,ci.y)*length,tj=new Vector3(cj.x,cj.z,cj.y)*length;
        float h1=2*t*t*t-3*t*t+1,h2=t*t*t-2*t*t+t,h3=-2*t*t*t+3*t*t,h4=t*t*t-t*t;
        Vector3 cubic=h1*ui+h2*ti+h3*uj+h4*tj;
        return cubic-dir*Vector3.Dot(cubic,dir)+dir*Vector3.Dot(u,dir);
    }
}
