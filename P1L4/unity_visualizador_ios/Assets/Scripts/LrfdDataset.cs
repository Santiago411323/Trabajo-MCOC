using System;

[Serializable] public sealed class LrfdInput
{
    public float d,l,roof,snowDepth,waterDepth,snowDensity,windSpeed,windCoefficient,windAngle,e;
}
[Serializable] public sealed class LrfdForce {public int id;public float[] f;}
[Serializable] public sealed class LrfdVariant
{public int u;public string name,label;public LrfdForce[] forces;public DisplacementRecord[] displacements;}
[Serializable] public sealed class LrfdDataset
{public string modelHash,notes;public LrfdInput scenario;public LrfdVariant[] variants;public float roofArea,equilibriumError;}
