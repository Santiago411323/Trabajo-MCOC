using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Generated binary packages stay local; an incomplete/stale mobile build is rejected.
public sealed class MobileSeismicBuildCheck : IPreprocessBuildWithReport
{
    public int callbackOrder => -50;
    public void OnPreprocessBuild(BuildReport report)
    {
        if(report.summary.platform==BuildTarget.iOS || report.summary.platform==BuildTarget.Android) Validate();
    }
    public static void Validate()
    {
        var lrfd=new MobileLrfd();if(!lrfd.Load())throw new BuildFailedException(lrfd.Status);
        string root=Path.Combine(Application.dataPath,"Resources");
        string modelHash=SeismicResponseData.Hash(File.ReadAllBytes(Path.Combine(root,"estructura_p1l4_unity.json")));
        foreach(string direction in new[]{"X","Y"})foreach(int intensity in new[]{25,50,100,150})
        {
            string id="el_centro_1940_ns_"+direction+"_i"+intensity.ToString("000");
            string metadata=Path.Combine(root,"MobileSeismic",id+".json");
            string payload=Path.Combine(root,"MobileSeismic",id+"_payload.bytes");
            if(!File.Exists(metadata)||!File.Exists(payload))
                throw new BuildFailedException("Faltan resultados moviles. Genera las corridas X/Y y ejecuta P1L4/seismic/pack_mobile.py.");
            var m=JsonUtility.FromJson<SeismicMetadata>(File.ReadAllText(metadata));
            if(m==null||m.modelHash!=modelHash||!m.complete||m.truncated||m.direction!=direction||Mathf.Abs(m.intensity-intensity/100f)>.0001f)
                throw new BuildFailedException("Caso movil incompatible: "+id);
            using(var sha=SHA256.Create())using(var file=File.OpenRead(payload))using(var zip=new GZipStream(file,CompressionMode.Decompress))
                if(BitConverter.ToString(sha.ComputeHash(zip)).Replace("-","").ToLowerInvariant()!=m.binarySha256)
                    throw new BuildFailedException("Caso movil corrupto: "+id);
        }
        Debug.Log("[Mobile seismic build] PASS: ocho respuestas completas; modelo y binarios verificados.");
    }
}
