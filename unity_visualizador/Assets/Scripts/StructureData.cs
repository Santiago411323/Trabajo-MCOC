using System;

[Serializable]
public class StructureData
{
    public string units;
    public NodeData[] nodes;
    public ElementData[] elements;
    public PointLoadData[] pointLoads;
}

[Serializable]
public class PointLoadData
{
    public int node;
    public float fx;
    public float fy;
    public float fz;
    public float mx;
    public float my;
    public float mz;
}

[Serializable]
public class NodeData
{
    public int id;
    public float x;
    public float y;
    public float z;
}

[Serializable]
public class ElementData
{
    public int id;
    public string type;
    public int nodeI;
    public int nodeJ;
    public float uniformLoad;
    public float axialI;
    public float axialJ;
    public float shearI;
    public float shearJ;
    public float momentI;
    public float momentJ;
}
