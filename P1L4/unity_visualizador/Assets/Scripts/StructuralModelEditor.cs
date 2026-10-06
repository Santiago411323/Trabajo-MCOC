using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Process = System.Diagnostics.Process;

[Serializable]
public class StructuralModelEditFile
{
    public string version = "1.0";
    public string savedAtUtc;
    public StructuralElementEdit[] elements = new StructuralElementEdit[0];
}

[Serializable]
public class StructuralElementEdit
{
    public int elementId;
    public string elementTag;
    public string sourceId;
    public string elementType;
    public bool isWall;
    public float width_m;
    public float height_m;
    public float fc_MPa;
    public float fy_MPa;
    public float barDiameter_mm;
    public int topBars;
    public int bottomBars;
    public int sideBarsEach;
    public float cover_mm;
    public int stirrupCount;
    public float stirrupDiameter_mm;
    public float stirrupSpacing_mm;
    public int stirrupLegs;
}

// Parametric editor shared by the inspector and the OpenSees exporter.
// The base Python model remains untouched: edits live in P1L4/model_edits.json.
public class StructuralModelEditor : MonoBehaviour
{
    public static bool ResultsStale { get; private set; }
    public bool AnalysisRunning => analysisProcess != null;
    public string Status => status;

    private readonly List<StructuralElementEdit> edits = new List<StructuralElementEdit>();
    private ElementSelectable bound;
    private Process analysisProcess;
    private string status = "Sin cambios pendientes.";
    private Vector3 originalScale;
    private float originalWidth, originalHeight;
    private string originalSectionId, originalSectionName, originalPmSectionId;

    private string widthText, heightText, fcText, fyText, diameterText;
    private string topText, bottomText, sideText, coverText;
    private string stirrupCountText, stirrupDiameterText, stirrupSpacingText, stirrupLegsText;

    private string ConfigPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "model_edits.json"));
    private string ResultsPath => DesktopModelFile.ActivePath;
    private string ExporterPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", DesktopModelFile.Available ? "actualizar_hueco_muros_desktop.py" : "exportar_resultados_unity.py"));
    private string RootPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

    private void OnEnable()
    {
        LoadEdits();
        RefreshStaleState();
    }

    private void OnDisable()
    {
        if (analysisProcess != null)
        {
            analysisProcess.Dispose();
            analysisProcess = null;
        }
    }

    private void Update()
    {
        if (analysisProcess == null) return;
        bool finished;
        try { finished = analysisProcess.HasExited; }
        catch { finished = true; }
        if (!finished) return;

        int exitCode = -1;
        try { exitCode = analysisProcess.ExitCode; }
        catch { }
        analysisProcess.Dispose();
        analysisProcess = null;

        if (exitCode != 0)
        {
            ResultsStale = true;
            status = "ERROR DE ANÁLISIS — revise la consola de Python/OpenSees.";
            UnityEngine.Debug.LogError("[StructuralModelEditor] El exportador terminó con código " + exitCode + ".");
            return;
        }

        try
        {
            string json = File.ReadAllText(ResultsPath);
            StructureViewer.RuntimeJsonOverride = json;
            ResultsStale = false;
            status = "ANÁLISIS COMPLETO — recargando modelo y resultados.";
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.name);
        }
        catch (Exception exception)
        {
            ResultsStale = true;
            status = "OpenSees terminó, pero Unity no pudo recargar el JSON.";
            UnityEngine.Debug.LogError("[StructuralModelEditor] " + exception);
        }
    }

    public void DrawEditor(ElementSelectable element, float x, float y, float width)
    {
        Bind(element);
        if (bound == null)
        {
            GUI.Label(new Rect(x, y, width, 30f), "Seleccione una viga, columna o muro.");
            return;
        }

        string tag = ElementTag(bound);
        GUI.Label(new Rect(x, y, width, 22f), "EDITOR PARAMÉTRICO — " + tag);
        y += 25f;
        GUI.Label(new Rect(x, y, width, 34f),
            "Las dimensiones modifican la rigidez y el peso propio del modelo elástico. " +
            "La armadura actualiza fibras, P–M y momento–curvatura al reanalizar.");
        y += 39f;

        float columnGap = 18f;
        float columnWidth = (width - columnGap) * .5f;
        float left = x;
        float right = x + columnWidth + columnGap;

        GUI.Box(new Rect(left, y, columnWidth, 174f), GUIContent.none);
        GUI.Label(new Rect(left + 10f, y + 7f, columnWidth - 20f, 20f), "SECCIÓN Y MATERIALES");
        float ly = y + 32f;
        widthText = Field(left + 10f, ref ly, columnWidth - 20f, bound.isWall ? "Espesor [m]" : "Ancho b [m]", widthText);
        heightText = Field(left + 10f, ref ly, columnWidth - 20f, bound.isWall ? "Largo equivalente [m]" : "Altura h [m]", heightText);
        fcText = Field(left + 10f, ref ly, columnWidth - 20f, "f'c [MPa]", fcText);
        fyText = Field(left + 10f, ref ly, columnWidth - 20f, "fy [MPa]", fyText);

        GUI.Box(new Rect(right, y, columnWidth, 174f), GUIContent.none);
        GUI.Label(new Rect(right + 10f, y + 7f, columnWidth - 20f, 20f), "ARMADURA LONGITUDINAL");
        float ry = y + 32f;
        diameterText = Field(right + 10f, ref ry, columnWidth - 20f, "Diámetro [mm]", diameterText);
        topText = Field(right + 10f, ref ry, columnWidth - 20f, "Barras superiores", topText);
        bottomText = Field(right + 10f, ref ry, columnWidth - 20f, "Barras inferiores", bottomText);
        sideText = Field(right + 10f, ref ry, columnWidth - 20f, "Barras por lado (×2)", sideText);
        coverText = Field(right + 10f, ref ry, columnWidth - 20f, "Al centro barra [mm]", coverText);

        y += 184f;
        if (bound.data != null && bound.data.type == "viga")
        {
            GUI.Label(new Rect(x,y,width,22f),"ARMADURA TRANSVERSAL — tramo de estribos; no implica cobertura de todo el vano"); y+=26f;
            float sy=y;
            stirrupCountText=Field(left,ref sy,columnWidth,"Cantidad de estribos",stirrupCountText);
            stirrupDiameterText=Field(left,ref sy,columnWidth,"Diámetro [mm]",stirrupDiameterText);
            sy=y;
            stirrupSpacingText=Field(right,ref sy,columnWidth,"Separación [mm]",stirrupSpacingText);
            stirrupLegsText=Field(right,ref sy,columnWidth,"Ramas resistentes",stirrupLegsText);
            y+=57f;
        }
        if (TryReadFields(out StructuralElementEdit candidate, out string error))
        {
            int bars = candidate.topBars + candidate.bottomBars + 2 * candidate.sideBarsEach;
            float ast = bars * Mathf.PI * candidate.barDiameter_mm * candidate.barDiameter_mm / 4f;
            float rho = 100f * ast / Mathf.Max(1f, candidate.width_m * candidate.height_m * 1e6f);
            float area = candidate.width_m * candidate.height_m;
            GUI.Label(new Rect(x, y, width, 22f),
                $"VISTA PREVIA: A={area:0.####} m²   |   {bars} barras Ø{candidate.barDiameter_mm:0.#}   |   Ast={ast:0.#} mm²   |   ρ={rho:0.###}%");
        }
        else
        {
            GUI.Label(new Rect(x, y, width, 22f), "REVISAR DATOS: " + error);
        }
        y += 27f;

        float buttonGap = 7f;
        float buttonWidth = (width - 2f * buttonGap) / 3f;
        if (GUI.Button(new Rect(x, y, buttonWidth, 28f), "DESCARTAR CAMPOS")) Bind(bound, true);
        if (GUI.Button(new Rect(x + buttonWidth + buttonGap, y, buttonWidth, 28f), "GUARDAR + PREVIEW"))
            ApplyPreviewAndSave();
        GUI.enabled = analysisProcess == null;
        if (GUI.Button(new Rect(x + 2f * (buttonWidth + buttonGap), y, buttonWidth, 28f), "REANALIZAR OPENSEES"))
            RunAnalysis();
        GUI.enabled = true;
        y += 35f;

        Color previous = GUI.color;
        GUI.color = ResultsStale ? new Color(1f, .72f, .25f) : new Color(.3f, 1f, .48f);
        GUI.Label(new Rect(x, y, width, 36f), status);
        GUI.color = previous;
    }

    private static string Field(float x, ref float y, float width, string label, string value)
    {
        float labelWidth = Mathf.Min(170f, width * .56f);
        GUI.Label(new Rect(x, y, labelWidth, 22f), label);
        value = GUI.TextField(new Rect(x + labelWidth, y, width - labelWidth, 22f), value ?? "");
        y += 26f;
        return value;
    }

    private void Bind(ElementSelectable element, bool force = false)
    {
        if (!force && element == bound) return;
        bound = element;
        if (bound == null) return;

        originalScale = bound.transform.localScale;
        originalSectionId = bound.data != null ? bound.data.sectionId : null;
        originalSectionName = bound.data != null ? bound.data.seccion : null;
        originalPmSectionId = bound.pmSectionId;
        originalWidth = bound.isWall ? bound.wallThickness : bound.data != null ? bound.data.width_m : 0f;
        originalHeight = bound.isWall ? bound.wallLength : bound.data != null ? bound.data.height_m : 0f;
        SectionMaterialData material = UnityData.GetMaterial(bound.pmSectionId) ??
            UnityData.GetMaterial(bound.data != null ? bound.data.sectionId : null) ??
            UnityData.GetMaterial(bound.data != null ? bound.data.seccion : null);
        StructuralElementEdit saved = FindEdit(bound);

        float width = saved != null ? saved.width_m : Mathf.Max(.01f, originalWidth);
        float height = saved != null ? saved.height_m : Mathf.Max(.01f, originalHeight);
        float fc = saved != null ? saved.fc_MPa : material != null && material.fc_MPa > 0f ? material.fc_MPa : bound.isWall ? 30f : 25f;
        float fy = saved != null ? saved.fy_MPa : material != null && material.fy_MPa > 0f ? material.fy_MPa : 420f;
        bool beam=bound.data!=null && bound.data.type=="viga";
        float diameter = saved != null ? saved.barDiameter_mm : material != null && material.barDiameter_mm > 0f ? material.barDiameter_mm : 25f;
        int top = saved != null ? saved.topBars : material != null && material.steelBars > 0 ? material.topBars : beam ? 4 : 5;
        int bottom = saved != null ? saved.bottomBars : material != null && material.steelBars > 0 ? material.bottomBars : beam ? 4 : 5;
        int side = saved != null ? saved.sideBarsEach : material != null && material.steelBars > 0 ? material.sideBarsEach : beam ? 2 : 4;
        float cover = saved != null ? saved.cover_mm : material != null && material.cover_mm > 0f ? material.cover_mm : 50f;

        widthText = Format(width); heightText = Format(height); fcText = Format(fc); fyText = Format(fy);
        diameterText = Format(diameter); topText = top.ToString(); bottomText = bottom.ToString();
        sideText = side.ToString(); coverText = Format(cover);
        stirrupCountText=(saved!=null && saved.stirrupCount>0 ? saved.stirrupCount : material!=null && material.stirrupCount>0 ? material.stirrupCount : 17).ToString();
        stirrupDiameterText=Format(saved!=null && saved.stirrupDiameter_mm>0 ? saved.stirrupDiameter_mm : material!=null && material.stirrupDiameter_mm>0 ? material.stirrupDiameter_mm : 10);
        stirrupSpacingText=Format(saved!=null && saved.stirrupSpacing_mm>0 ? saved.stirrupSpacing_mm : material!=null && material.stirrupSpacing_mm>0 ? material.stirrupSpacing_mm : 100);
        stirrupLegsText=(saved!=null && saved.stirrupLegs>0 ? saved.stirrupLegs : material!=null && material.stirrupLegs>0 ? material.stirrupLegs : 4).ToString();
        status = ResultsStale ? "CAMBIOS GUARDADOS — resultados pendientes de reanálisis." : "Modelo y resultados sincronizados.";
    }

    private void ApplyPreviewAndSave()
    {
        if (!TryReadFields(out StructuralElementEdit edit, out string error))
        {
            status = "NO SE GUARDÓ: " + error;
            return;
        }

        int index = edits.FindIndex(existing => SameElement(existing, bound));
        if (index >= 0) edits[index] = edit; else edits.Add(edit);
        SaveEdits();
        ApplyPreview(edit);
        ResultsStale = true;
        status = "CAMBIOS GUARDADOS — resultados desactualizados. Presione REANALIZAR OPENSEES.";
    }

    private void ApplyPreview(StructuralElementEdit edit)
    {
        string sectionId = "EDIT_" + Sanitize(edit.elementTag);
        if (bound.isWall)
        {
            float thicknessRatio = originalWidth > .0001f ? originalScale.x / originalWidth : 1f;
            bound.wallThickness = edit.width_m;
            bound.wallLength = edit.height_m;
            bound.transform.localScale = new Vector3(edit.width_m * thicknessRatio, originalScale.y, edit.height_m);
        }
        else if (bound.data != null)
        {
            bound.data.width_m = edit.width_m;
            bound.data.height_m = edit.height_m;
            bound.data.sectionId = sectionId;
            bound.data.seccion = sectionId;
            bound.pmSectionId = sectionId;
            bound.transform.localScale = new Vector3(edit.width_m, originalScale.y, edit.height_m);
        }

        int bars = edit.topBars + edit.bottomBars + 2 * edit.sideBarsEach;
        float ast = bars * Mathf.PI * edit.barDiameter_mm * edit.barDiameter_mm / 4f;
        UnityData.UpsertMaterial(new SectionMaterialData {
            sectionId = sectionId, elementType = edit.elementType,
            materialName = $"Editado en Unity: H-{edit.fc_MPa:0.#} / fy={edit.fy_MPa:0.#}",
            fc_MPa = edit.fc_MPa, fy_MPa = edit.fy_MPa,
            E_MPa = 4700f * Mathf.Sqrt(edit.fc_MPa), Es_MPa = 200000f,
            b_m = edit.width_m, h_m = edit.height_m, steelBars = bars,
            barDiameter_mm = edit.barDiameter_mm, Ast_mm2 = ast,
            rho_percent = 100f * ast / Mathf.Max(1f, edit.width_m * edit.height_m * 1e6f),
            effectiveDepth_mm = edit.height_m * 1000f - edit.cover_mm,
            topBars = edit.topBars, bottomBars = edit.bottomBars, sideBarsEach = edit.sideBarsEach,
            cover_mm = edit.cover_mm, concreteFibersX = 20, concreteFibersY = 20,
            stirrupCount=edit.stirrupCount,stirrupDiameter_mm=edit.stirrupDiameter_mm,
            stirrupSpacing_mm=edit.stirrupSpacing_mm,stirrupLegs=edit.stirrupLegs,
            concreteModel = "Concrete01", steelModel = "Steel01",
            note = "Vista previa paramétrica; ejecutar OpenSees para actualizar resultados."
        });
    }

    private void RunAnalysis()
    {
        if (!TryReadFields(out StructuralElementEdit _, out string validationError))
        {
            status = "NO SE PUEDE ANALIZAR: " + validationError;
            return;
        }
        ApplyPreviewAndSave();
        if (!ResultsStale || analysisProcess != null) return;
        if (!File.Exists(ExporterPath))
        {
            status = "No se encontró P1L4/exportar_resultados_unity.py.";
            return;
        }

        string arguments = $"\"{ExporterPath}\" --model-edits \"{ConfigPath}\"";
        foreach (string candidate in PythonCandidates())
        {
            analysisProcess = StartPython(candidate, arguments);
            if (analysisProcess != null) break;
        }
        if (analysisProcess == null)
        {
            status = "No se pudo iniciar Python. Compruebe python/py y openseespy.";
            return;
        }
        status = "ANALIZANDO… Unity continúa activo mientras OpenSees recalcula G, Q, EX, EY, C1, C2 y C3.";
    }

    private Process StartPython(string executable, string arguments)
    {
        try
        {
            var start=new ProcessStartInfo {
                FileName = executable, Arguments = arguments, WorkingDirectory = RootPath,
                UseShellExecute = false, CreateNoWindow = true
            };
            string packages=Path.Combine(RootPath,".venv","Lib","site-packages");
            if(Directory.Exists(packages)) start.EnvironmentVariables["PYTHONPATH"]=packages+Path.PathSeparator+(Environment.GetEnvironmentVariable("PYTHONPATH") ?? "");
            string cache=Path.Combine(Path.GetDirectoryName(ExporterPath),"seismic","cache");
            Directory.CreateDirectory(cache);start.EnvironmentVariables["MPLCONFIGDIR"]=cache;
            return Process.Start(start);
        }
        catch { return null; }
    }

    private IEnumerable<string> PythonCandidates()
    {
        string configured = Environment.GetEnvironmentVariable("MCOC_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured)) yield return configured.Trim();
        string runtimePath=Path.Combine(Path.GetDirectoryName(ExporterPath),"seismic","runtime.local.json");
        if(File.Exists(runtimePath))
        {
            var runtime=JsonUtility.FromJson<SeismicPythonRuntime>(File.ReadAllText(runtimePath));
            if(runtime!=null && File.Exists(runtime.executable)) yield return runtime.executable;
        }
        string pathFile = Path.Combine(Path.GetDirectoryName(ExporterPath) ?? "", "python_path.txt");
        if (File.Exists(pathFile))
        {
            string fromFile = File.ReadAllText(pathFile).Trim();
            if (!string.IsNullOrEmpty(fromFile)) yield return fromFile;
        }
        string rootVenv = Path.Combine(RootPath, ".venv", "Scripts", "python.exe");
        if (File.Exists(rootVenv)) yield return rootVenv;
        string p1l4Venv = Path.Combine(Path.GetDirectoryName(ExporterPath) ?? "", ".venv", "Scripts", "python.exe");
        if (File.Exists(p1l4Venv)) yield return p1l4Venv;
        yield return "python";
        yield return "py";
    }

    private bool TryReadFields(out StructuralElementEdit edit, out string error)
    {
        edit = null; error = "";
        if (bound == null) { error = "sin elemento seleccionado"; return false; }
        if (!TryFloat(widthText, out float width) || width < .05f || width > 12f) { error = "dimensión b fuera de 0,05–12 m"; return false; }
        if (!TryFloat(heightText, out float height) || height < .05f || height > 20f) { error = "dimensión h fuera de 0,05–20 m"; return false; }
        if (!TryFloat(fcText, out float fc) || fc < 10f || fc > 120f) { error = "f'c fuera de 10–120 MPa"; return false; }
        if (!TryFloat(fyText, out float fy) || fy < 200f || fy > 800f) { error = "fy fuera de 200–800 MPa"; return false; }
        if (!TryFloat(diameterText, out float diameter) || diameter < 6f || diameter > 60f) { error = "diámetro fuera de 6–60 mm"; return false; }
        if (!int.TryParse(topText, out int top) || top < 0 || top > 40 || !int.TryParse(bottomText, out int bottom) || bottom < 0 || bottom > 40 || !int.TryParse(sideText, out int side) || side < 0 || side > 40) { error = "cantidad de barras inválida"; return false; }
        if (top + bottom + 2 * side < 2) { error = "se requieren al menos dos barras"; return false; }
        if (!TryFloat(coverText, out float cover) || cover < 10f || cover > 200f || 2f * cover >= Mathf.Min(width, height) * 1000f) { error = "recubrimiento incompatible con la sección"; return false; }
        int stirrupCount=0,legs=0;float stirrupDiameter=0,spacing=0;
        if(bound.data!=null && bound.data.type=="viga" &&
            (!int.TryParse(stirrupCountText,out stirrupCount) || stirrupCount<1 || stirrupCount>1000 ||
            !int.TryParse(stirrupLegsText,out legs) || legs<2 || legs>12 ||
            !TryFloat(stirrupDiameterText,out stirrupDiameter) || stirrupDiameter<6 || stirrupDiameter>32 ||
            !TryFloat(stirrupSpacingText,out spacing) || spacing<20 || spacing>1000))
        {error="estribos: cantidad 1–1000, ramas 2–12, Ø6–32 mm, separación 20–1000 mm";return false;}

        edit = new StructuralElementEdit {
            elementId = bound.isWall ? bound.wallId : bound.data.id,
            elementTag = ElementTag(bound), sourceId = bound.isWall ? bound.wallSourceId : bound.data.sourceId,
            elementType = bound.isWall ? "muro" : bound.data.type, isWall = bound.isWall,
            width_m = width, height_m = height, fc_MPa = fc, fy_MPa = fy,
            barDiameter_mm = diameter, topBars = top, bottomBars = bottom,
            sideBarsEach = side, cover_mm = cover,
            stirrupCount=stirrupCount,stirrupDiameter_mm=stirrupDiameter,stirrupSpacing_mm=spacing,stirrupLegs=legs
        };
        return true;
    }

    private void LoadEdits()
    {
        edits.Clear();
        if (!File.Exists(ConfigPath)) return;
        try
        {
            StructuralModelEditFile file = JsonUtility.FromJson<StructuralModelEditFile>(File.ReadAllText(ConfigPath));
            if (file != null && file.elements != null) edits.AddRange(file.elements);
        }
        catch (Exception exception) { UnityEngine.Debug.LogWarning("[StructuralModelEditor] " + exception.Message); }
    }

    private void SaveEdits()
    {
        StructuralModelEditFile file = new StructuralModelEditFile {
            version = "1.0", savedAtUtc = DateTime.UtcNow.ToString("o"), elements = edits.ToArray()
        };
        File.WriteAllText(ConfigPath, JsonUtility.ToJson(file, true));
    }

    private void RefreshStaleState()
    {
        ResultsStale = File.Exists(ConfigPath) && (!File.Exists(ResultsPath) ||
            File.GetLastWriteTimeUtc(ConfigPath) > File.GetLastWriteTimeUtc(ResultsPath));
    }

    private StructuralElementEdit FindEdit(ElementSelectable element) => edits.Find(edit => SameElement(edit, element));
    private static bool SameElement(StructuralElementEdit edit, ElementSelectable element)
    {
        if (edit == null || element == null || edit.isWall != element.isWall) return false;
        if (element.isWall) return edit.elementId == element.wallId || (!string.IsNullOrEmpty(edit.sourceId) && edit.sourceId == element.wallSourceId);
        return element.data != null && (edit.elementId == element.data.id || (!string.IsNullOrEmpty(edit.elementTag) && edit.elementTag == element.data.elementTag));
    }

    private static string ElementTag(ElementSelectable element) => element.isWall
        ? (!string.IsNullOrEmpty(element.wallSourceId) ? element.wallSourceId : "W" + element.wallId)
        : element.data != null && !string.IsNullOrEmpty(element.data.elementTag) ? element.data.elementTag
        : element.data != null ? element.data.id.ToString() : "ELEMENT";
    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value)) return "SECTION";
        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++) if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_') chars[i] = '_';
        return new string(chars).Trim('_');
    }
    private static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static bool TryFloat(string text, out float value)
    {
        return float.TryParse((text ?? "").Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out value);
    }
}
