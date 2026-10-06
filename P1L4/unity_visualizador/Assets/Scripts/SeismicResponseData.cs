using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

[Serializable] public class SeismicRecordCatalog { public SeismicRecord[] records; }
[Serializable] public class SeismicRecord
{
    public string id, name, file, format, units, source, note;
}
[Serializable] public class SeismicMember
{
    public int id, nodeI, nodeJ;
    public string elementTag, type, sectionId;
    public float width_m, height_m;
}
[Serializable] public class SeismicModal
{
    public float[] periods_s, achievedModalDamping;
    public float dampingRatio, alphaM, betaKInitial;
    public int[] dampingModes;
}
[Serializable] public class SeismicMaximum
{
    public float value_m, time;
    public int nodeTag;
}
[Serializable] public class SeismicMetadata
{
    public int version, frameCount, stride;
    public bool complete, truncated;
    public string id, analysis, recordId, recordName, recordSource, recordNote, direction;
    public string modelHash, binaryFile, binarySha256, units, nodeResponse, forceResponse;
    public float intensity, pgaOriginal_m_s2, pgaApplied_m_s2, recordDt, dt, duration;
    public int[] nodeTags;
    public float[] nodeCoordinates, recordAcceleration_m_s2;
    public SeismicMember[] members;
    public SeismicModal modal;
    public SeismicMaximum maximumDisplacement;
}

// Only graphics interpolate. ReadNode/ReadEndForce preserve each exported sample.
public sealed class SeismicResponseData
{
    public SeismicMetadata Metadata { get; private set; }
    public readonly Dictionary<int, int> NodeIndex = new Dictionary<int, int>();
    public readonly Dictionary<int, int> MemberIndex = new Dictionary<int, int>();
    private float[] samples;

    public static string Hash(byte[] data)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }

    public static SeismicResponseData Load(string path, StructureData structure, string modelHash)
    {
        SeismicMetadata m = JsonUtility.FromJson<SeismicMetadata>(File.ReadAllText(path));
        if (m == null || m.version != 1 || !m.complete || m.nodeTags == null || m.members == null ||
            m.nodeCoordinates == null || m.frameCount < 2 || m.dt <= 0 || m.duration <= 0 ||
            m.nodeCoordinates.Length != m.nodeTags.Length * 3)
            throw new InvalidDataException("Contrato sísmico incompleto o inválido.");
        if (m.modelHash != modelHash)
            throw new InvalidDataException("El análisis corresponde a otro modelo. Ejecute de nuevo OpenSees.");
        if (Path.GetFileName(m.binaryFile) != m.binaryFile)
            throw new InvalidDataException("Ruta binaria inválida.");
        byte[] payload = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path), m.binaryFile));
        int stride = checked(1 + 3*m.nodeTags.Length + 12*m.members.Length);
        long expected = 20L + 4L*stride*m.frameCount;
        if (!BitConverter.IsLittleEndian || m.stride != stride || payload.LongLength != expected ||
            System.Text.Encoding.ASCII.GetString(payload, 0, 8) != "MCOCSIS1" ||
            BitConverter.ToInt32(payload, 8) != m.nodeTags.Length ||
            BitConverter.ToInt32(payload, 12) != m.members.Length ||
            BitConverter.ToInt32(payload, 16) != m.frameCount || Hash(payload) != m.binarySha256)
            throw new InvalidDataException("Archivo sísmico truncado, corrupto o incompatible.");
        var response = new SeismicResponseData { Metadata = m, samples = new float[checked(stride*m.frameCount)] };
        Buffer.BlockCopy(payload, 20, response.samples, 0, payload.Length-20);
        foreach (float value in response.samples)
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidDataException("Respuesta no finita.");
        var currentNodes = new Dictionary<int, NodeData>();
        foreach (NodeData node in structure.nodes) currentNodes.Add(node.id, node);
        for (int n = 0; n < m.nodeTags.Length; n++)
        {
            int id = m.nodeTags[n];
            if (!currentNodes.TryGetValue(id, out NodeData node) ||
                Mathf.Abs(node.x-m.nodeCoordinates[n*3]) > 0.0001f ||
                Mathf.Abs(node.y-m.nodeCoordinates[n*3+1]) > 0.0001f ||
                Mathf.Abs(node.z-m.nodeCoordinates[n*3+2]) > 0.0001f)
                throw new InvalidDataException("El nodo " + id + " no coincide con el modelo visible.");
            response.NodeIndex.Add(id,n);
        }
        var currentMembers = new Dictionary<int, ElementData>();
        foreach (ElementData member in structure.elements) currentMembers.Add(member.id, member);
        for (int e = 0; e < m.members.Length; e++)
        {
            SeismicMember member = m.members[e];
            if (!currentMembers.TryGetValue(member.id, out ElementData current) ||
                current.nodeI != member.nodeI || current.nodeJ != member.nodeJ ||
                current.elementTag != member.elementTag || current.type != member.type ||
                Mathf.Abs(current.width_m-member.width_m) > 0.0001f ||
                Mathf.Abs(current.height_m-member.height_m) > 0.0001f ||
                !response.NodeIndex.ContainsKey(member.nodeI) || !response.NodeIndex.ContainsKey(member.nodeJ))
                throw new InvalidDataException("El elemento " + member.id + " no coincide con el análisis.");
            response.MemberIndex.Add(member.id,e);
        }
        for (int frame = 0; frame < m.frameCount; frame++)
            if (Mathf.Abs(response.Time(frame)-frame*m.dt) > Mathf.Max(0.0001f,m.dt*.01f))
                throw new InvalidDataException("Timeline inconsistente.");
        return response;
    }

    public float Time(int frame) => samples[frame*Metadata.stride];
    public int FrameAt(float time) => Mathf.Clamp(Mathf.FloorToInt(time/Metadata.dt+0.00001f),0,Metadata.frameCount-1);
    public Vector3 OriginalNode(int index) => new Vector3(Metadata.nodeCoordinates[index*3],
        Metadata.nodeCoordinates[index*3+2],Metadata.nodeCoordinates[index*3+1]);
    public Vector3 ReadNode(int frame, int index)
    {
        int offset = frame*Metadata.stride+1+3*index;
        return new Vector3(samples[offset],samples[offset+2],samples[offset+1]);
    }
    public float ReadEndForce(int frame, int member, int component) =>
        samples[frame*Metadata.stride+1+Metadata.nodeTags.Length*3+member*12+component];
    public float Acceleration(float time)
    {
        float index = time/Metadata.recordDt;
        int i = Mathf.FloorToInt(index);
        float[] a = Metadata.recordAcceleration_m_s2;
        if (i < 0 || a == null || i >= a.Length-1) return 0;
        return Mathf.Lerp(a[i],a[i+1],index-i)*Metadata.intensity;
    }
}
