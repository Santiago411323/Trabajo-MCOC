using System.Collections.Generic;
using UnityEngine;

public static class UnityData
{
    public static StructureData Structure;
    public static string ActiveCombo;
    public static bool UseBaseCaseFactors;
    public static float FactorG = 1f;
    public static float FactorQ = 0f;
    public static float FactorEX = 0f;
    public static float FactorEY = 0f;
    public static readonly Dictionary<int,float[]> MobileForces = new Dictionary<int,float[]>();
    public static readonly Dictionary<int,Vector3> MobileDisplacements = new Dictionary<int,Vector3>();

    public static Dictionary<string, List<DisplacementRecord>> DisplacementsByCombo;
    public static Dictionary<string, List<ElementForceRecord>> ElementForcesByCombo;

    private static Dictionary<string, PMCurveData> pmCurveLookup;
    private static Dictionary<string, SectionMaterialData> materialLookup;
    private static Dictionary<string, ComboInfo> comboLookup;
    private static readonly Dictionary<int, FrameGeometry> frames = new Dictionary<int, FrameGeometry>();
    private static readonly Dictionary<string, Dictionary<int, float[]>> localForces = new Dictionary<string, Dictionary<int, float[]>>();
    private static readonly Dictionary<int, SupportData> nodeSupports = new Dictionary<int, SupportData>();

    public static void LoadData(StructureData data)
    {
        Structure = data;
        MobileForces.Clear(); MobileDisplacements.Clear();
        ActiveCombo = null;
        UseBaseCaseFactors = false;
        BuildModelGeometry(data);
        localForces.Clear();

        if (data.p1l4 == null)
        {
            DisplacementsByCombo = new Dictionary<string, List<DisplacementRecord>>();
            ElementForcesByCombo = new Dictionary<string, List<ElementForceRecord>>();
            pmCurveLookup = new Dictionary<string, PMCurveData>();
            materialLookup = new Dictionary<string, SectionMaterialData>();
            comboLookup = new Dictionary<string, ComboInfo>();
            return;
        }

        comboLookup = new Dictionary<string, ComboInfo>();
        if (data.p1l4.combinations != null)
        {
            foreach (ComboInfo c in data.p1l4.combinations)
            {
                if (c != null && !string.IsNullOrEmpty(c.name) && !comboLookup.ContainsKey(c.name))
                {
                    comboLookup[c.name] = c;
                }
            }
        }

        DisplacementsByCombo = new Dictionary<string, List<DisplacementRecord>>();
        if (data.p1l4.displacements != null)
        {
            foreach (DisplacementRecord d in data.p1l4.displacements)
            {
                if (string.IsNullOrEmpty(d.combo)) continue;
                if (!DisplacementsByCombo.ContainsKey(d.combo))
                {
                    DisplacementsByCombo[d.combo] = new List<DisplacementRecord>();
                }
                DisplacementsByCombo[d.combo].Add(d);
            }
        }

        ElementForcesByCombo = new Dictionary<string, List<ElementForceRecord>>();
        if (data.p1l4.elementForces != null)
        {
            foreach (ElementForceRecord f in data.p1l4.elementForces)
            {
                if (f == null || string.IsNullOrEmpty(f.combo)) continue;
                if (!ElementForcesByCombo.ContainsKey(f.combo))
                {
                    ElementForcesByCombo[f.combo] = new List<ElementForceRecord>();
                }
                ElementForcesByCombo[f.combo].Add(f);
                if (!FrameForces.IsValid(f.f) || !frames.TryGetValue(f.id, out var frame)) continue;
                string coordinates = data.p1l4.elementForceCoordinates;
                float[] local = string.IsNullOrEmpty(coordinates) || coordinates == "global"
                    ? frame.ToLocal(f.f)
                    : coordinates == "local" ? (float[])f.f.Clone() : null;
                if (local == null) continue; // Unknown coordinate contracts must not display fabricated values.
                if (!localForces.ContainsKey(f.combo)) localForces[f.combo] = new Dictionary<int, float[]>();
                localForces[f.combo][f.id] = local;
            }
        }

        pmCurveLookup = new Dictionary<string, PMCurveData>();
        if (data.p1l4.pmCurves != null)
        {
            foreach (PMCurveData c in data.p1l4.pmCurves)
            {
                if (c != null && !string.IsNullOrEmpty(c.sectionId) && !pmCurveLookup.ContainsKey(c.sectionId))
                {
                    pmCurveLookup[c.sectionId] = c;
                }
            }
        }

        materialLookup = new Dictionary<string, SectionMaterialData>();
        if (data.p1l4.sectionMaterials != null)
        {
            foreach (SectionMaterialData m in data.p1l4.sectionMaterials)
            {
                if (m != null && !string.IsNullOrEmpty(m.sectionId) && !materialLookup.ContainsKey(m.sectionId))
                {
                    materialLookup[m.sectionId] = m;
                }
            }
        }
    }

    public static Vector3 GetNodeDisplacement(string combo, int nodeId)
    {
        return GetBaseNodeDisplacement(combo,nodeId) +
            (MobileDisplacements.TryGetValue(nodeId,out var increment) ? increment : Vector3.zero);
    }

    private static Vector3 GetBaseNodeDisplacement(string combo, int nodeId)
    {
        if (UseBaseCaseFactors)
        {
            return GetNodeDisplacementForCombo("G", nodeId) * FactorG +
                   GetNodeDisplacementForCombo("Q", nodeId) * FactorQ +
                   GetNodeDisplacementForCombo("EX", nodeId) * FactorEX +
                   GetNodeDisplacementForCombo("EY", nodeId) * FactorEY;
        }

        return GetNodeDisplacementForCombo(combo, nodeId);
    }

    private static Vector3 GetNodeDisplacementForCombo(string combo, int nodeId)
    {
        if (string.IsNullOrEmpty(combo) || DisplacementsByCombo == null || !DisplacementsByCombo.TryGetValue(combo, out var list) || list == null)
        {
            return Vector3.zero;
        }

        foreach (DisplacementRecord d in list)
        {
            if (d.node == nodeId)
            {
                return new Vector3(d.ux, d.uz, d.uy);
            }
        }

        return Vector3.zero;
    }

    public static float[] GetElementForces(string combo, int elementId)
    {
        float[] source=GetBaseElementForces(combo,elementId);
        if(source==null) return null;
        float[] extra=MobileForces.TryGetValue(elementId,out var increment) ? increment : null;
        if(extra==null) return source;
        float[] total=(float[])source.Clone();
        for(int i=0;i<12;i++) total[i]+=extra[i];
        return total;
    }

    private static float[] GetBaseElementForces(string combo, int elementId)
    {
        if (UseBaseCaseFactors)
        {
            if (!frames.ContainsKey(elementId)) return null;
            float[] result = new float[12];
            if (!AddScaledForces(result, GetElementForcesForCombo("G", elementId), FactorG) ||
                !AddScaledForces(result, GetElementForcesForCombo("Q", elementId), FactorQ) ||
                !AddScaledForces(result, GetElementForcesForCombo("EX", elementId), FactorEX) ||
                !AddScaledForces(result, GetElementForcesForCombo("EY", elementId), FactorEY)) return null;
            return result;
        }

        return GetElementForcesForCombo(combo, elementId);
    }

    private static float[] GetElementForcesForCombo(string combo, int elementId)
    {
        if (string.IsNullOrEmpty(combo) || !localForces.TryGetValue(combo, out var byElement))
        {
            return null;
        }

        return byElement.TryGetValue(elementId, out var forces) ? forces : null;
    }

    public static float[] GetElementForcesForCase(string combo, int elementId)
    {
        return GetElementForcesForCombo(combo, elementId);
    }

    private static bool AddScaledForces(float[] target, float[] source, float factor)
    {
        if (factor == 0f) return true;
        if (!FrameForces.IsValid(source) || float.IsNaN(factor) || float.IsInfinity(factor)) return false;

        int count = Mathf.Min(target.Length, source.Length);
        for (int i = 0; i < count; i++)
        {
            target[i] += source[i] * factor;
        }
        return FrameForces.IsValid(target);
    }

    // Both accessors above return LOCAL resisting end actions. Raw JSON records
    // remain untouched in Structure/ElementForcesByCombo for traceability.
    public static bool TryGetFrameGeometry(int elementId, out FrameGeometry frame)
    {
        return frames.TryGetValue(elementId, out frame);
    }

    public static bool TryGetSectionForces(int elementId, string combo, float t, out FrameSectionForces values)
    {
        values = default(FrameSectionForces);
        float[] local = GetElementForces(combo, elementId);
        if (!FrameForces.IsValid(local) || !frames.TryGetValue(elementId, out var frame)) return false;
        values = FrameForces.Evaluate(local, frame.Length, t);
        return true;
    }

    public static Vector3 AxisToUnity(double[] axis)
    {
        return new Vector3((float)axis[0], (float)axis[2], (float)axis[1]);
    }

    public static SupportData GetNodeSupport(int nodeId)
    {
        return nodeSupports.TryGetValue(nodeId, out var support) ? support : null;
    }

    private static void BuildModelGeometry(StructureData data)
    {
        frames.Clear();
        nodeSupports.Clear();
        var nodes = new Dictionary<int, NodeData>();
        var graph = new Dictionary<int, HashSet<int>>();
        foreach (NodeData node in data.nodes ?? new NodeData[0]) nodes[node.id] = node;
        foreach (SupportData support in data.supports ?? new SupportData[0])
            if (nodes.ContainsKey(support.node)) nodeSupports[support.node] = support;
        foreach (ElementData element in data.elements ?? new ElementData[0])
        {
            if (!nodes.TryGetValue(element.nodeI, out var ni) || !nodes.TryGetValue(element.nodeJ, out var nj)) continue;
            if (FrameGeometry.TryCreate(ni, nj, out var frame)) frames[element.id] = frame;
            if (!graph.ContainsKey(ni.id)) graph[ni.id] = new HashSet<int>();
            if (!graph.ContainsKey(nj.id)) graph[nj.id] = new HashSet<int>();
            graph[ni.id].Add(nj.id);
            graph[nj.id].Add(ni.id);
        }

        // The legacy P1L3 model adds constraints not listed in the JSON.
        // Reproduce only their description; do not change the source model/data.
        // Explicit future coordinate contracts must provide their own constraints.
        if (data.p1l4 == null || !string.IsNullOrEmpty(data.p1l4.elementForceCoordinates)) return;
        foreach (int nodeId in nodes.Keys)
            if (!graph.ContainsKey(nodeId) && !nodeSupports.ContainsKey(nodeId))
                nodeSupports[nodeId] = InferredSupport(nodeId, "Nodo aislado fijado por P1L3 (inferido)");
        var visited = new HashSet<int>();
        var orderedNodes = new List<int>(graph.Keys);
        orderedNodes.Sort();
        foreach (int seed in orderedNodes)
        {
            if (!visited.Add(seed)) continue;
            int anchor = seed;
            bool supported = false;
            var stack = new Stack<int>();
            stack.Push(seed);
            while (stack.Count > 0)
            {
                int nodeId = stack.Pop();
                supported |= nodeSupports.ContainsKey(nodeId);
                if (nodes[nodeId].z < nodes[anchor].z || (nodes[nodeId].z == nodes[anchor].z && nodeId < anchor)) anchor = nodeId;
                foreach (int next in graph[nodeId]) if (visited.Add(next)) stack.Push(next);
            }
            if (!supported) nodeSupports[anchor] = InferredSupport(anchor, "Empotramiento automatico P1L3: componente desconectada (inferido)");
        }
    }

    private static SupportData InferredSupport(int nodeId, string description)
    {
        return new SupportData { node = nodeId, type = description, ux = 1, uy = 1, uz = 1, rx = 1, ry = 1, rz = 1 };
    }

    public static string GetActiveLoadLabel()
    {
        if (UseBaseCaseFactors)
        {
            return $"Superposicion: {FactorG:0.##}G + {FactorQ:0.##}Q + {FactorEX:0.##}EX + {FactorEY:0.##}EY";
        }
        return GetComboLabel(ActiveCombo);
    }

    public static PMCurveData GetPMCurve(string sectionId)
    {
        if (string.IsNullOrEmpty(sectionId) || pmCurveLookup == null) return null;
        return pmCurveLookup.TryGetValue(sectionId, out var curve) ? curve : null;
    }

    public static SectionMaterialData GetMaterial(string sectionId)
    {
        if (string.IsNullOrEmpty(sectionId) || materialLookup == null) return null;
        return materialLookup.TryGetValue(sectionId, out var mat) ? mat : null;
    }

    public static string GetComboLabel(string combo)
    {
        if (string.IsNullOrEmpty(combo)) return "sin combinacion activa";
        if (comboLookup != null && comboLookup.TryGetValue(combo, out var info) && info != null && !string.IsNullOrEmpty(info.label))
        {
            if (info.label.StartsWith(combo + ":")) return info.label;
            return $"{combo}: {info.label}";
        }
        return combo;
    }

    public static ComboInfo GetComboInfo(string combo)
    {
        if (string.IsNullOrEmpty(combo) || comboLookup == null) return null;
        return comboLookup.TryGetValue(combo, out var info) ? info : null;
    }
}
