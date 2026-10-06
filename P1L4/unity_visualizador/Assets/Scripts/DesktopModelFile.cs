using System.IO;
using UnityEngine;

// Separate desktop analysis: phone resources and their precomputed responses remain unchanged.
public static class DesktopModelFile
{
    public static string ModelPath => Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","desktop_model","estructura_p1l4_desktop.json"));
    public static bool Available => (Application.isEditor || !Application.isMobilePlatform) && VisualCafe.UseDesktopLayout && File.Exists(ModelPath);
    public static string ActivePath => Available ? ModelPath : Path.Combine(Application.dataPath,"Resources","estructura_p1l4_unity.json");
    public static string ResultsDirectory(string seismicPath) => Path.Combine(seismicPath,Available?"desktop_results":"results");
    public static string SlabCatalogJson
    {
        get
        {
            if(Available)
            {
                string path=Path.Combine(Path.GetDirectoryName(ModelPath),"slab_load_surfaces.json");
                if(File.Exists(path)) return File.ReadAllText(path);
            }
            return Resources.Load<TextAsset>("slab_load_surfaces")?.text;
        }
    }
}
