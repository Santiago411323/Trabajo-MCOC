using System;
using System.IO;
using System.Text;
using UnityEngine;

public partial class ElementResultsPanel
{
    private GUIStyle auditWarningStyle;

    private void DrawAudit(ElementSelectable element, float x, float y, float width)
    {
        StructuralAuditReport report = StructuralAuditReport.Build(element, manualCheckPosition, forceIndex);
        if (auditWarningStyle == null)
        {
            auditWarningStyle = new GUIStyle(textStyle) { fontStyle = FontStyle.Bold };
            auditWarningStyle.normal.textColor = new Color(1f, .78f, .27f);
        }
        GUI.Label(new Rect(x + 7f, y, width - 305f, 22f), "CADENA DE EVIDENCIA DEL ELEMENTO", titleStyle);
        if (GUI.Button(new Rect(x + width - 164f, y, 164f, 24f), "EXPORTAR FICHA .TXT"))
            SaveAuditReport(element, report);
        if (GUI.Button(new Rect(x + width - 296f, y, 124f, 24f), "COPIAR RUTA"))
        {
            SaveAuditReport(element, report);
            if (!string.IsNullOrEmpty(auditExportPath))
            {
                GUIUtility.systemCopyBuffer = auditExportPath;
                auditExportStatus = "Ruta copiada. Péguela en el Explorador de archivos (Ctrl+V).";
            }
        }
        y += 30f;
        if (!string.IsNullOrEmpty(auditExportStatus))
        {
            GUI.Label(new Rect(x + 8f, y, width - 16f, 34f), auditExportStatus, mutedStyle);
            y += 39f;
        }
        if (element.data != null && GUI.Button(new Rect(x + 7f, y, width - 14f, 24f),
            "IR AL DIAGRAMA Y VER EL CÁLCULO EN ESTA POSICIÓN"))
        {
            manualCheckOpen = true;
            SetView(ResultView.Forces);
            resultsScroll.y = 190f;
        }
        y += element.data != null ? 32f : 4f;
        float startY = y;
        int charsPerLine = Mathf.Max(38, Mathf.FloorToInt((width - 28f) / 7f));
        foreach (AuditRow row in report.Rows)
        {
            if (row.Heading)
            {
                y += 7f;
                GUI.Label(new Rect(x + 8f, y, width - 16f, 21f), row.Text, titleStyle);
                y += 27f;
                continue;
            }
            int lines = Mathf.Clamp(Mathf.CeilToInt(row.Text.Length / (float)charsPerLine), 1, 4);
            float height = lines * 17f + 4f;
            GUIStyle style = row.Tone == AuditTone.Pass ? successStyle :
                row.Tone == AuditTone.Warning ? auditWarningStyle :
                row.Tone == AuditTone.Fail ? failureStyle : textStyle;
            GUI.Label(new Rect(x + 13f, y, width - 26f, height), row.Text, style);
            y += height + 2f;
        }
        auditContentHeight = Mathf.Max(400f, y - startY + startY + 32f - (IsBeam(element) ? 60f : 89f));
    }

    private void SaveAuditReport(ElementSelectable element, StructuralAuditReport report)
    {
        try
        {
            string folder = Path.Combine(Application.persistentDataPath, "InformesEstructurales");
            Directory.CreateDirectory(folder);
            string tag = element.data != null ? element.data.elementTag : "Muro_" + element.wallId;
            string name = SafeFileName(tag + "_" + UnityData.ActiveCombo + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")) + ".txt";
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, "Fecha: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                "\nEscenario: " + UnityData.GetActiveLoadLabel() + "\n" + report.AsText(), new UTF8Encoding(false));
            auditExportPath = path;
            auditExportStatus = "Ficha guardada: " + Path.GetFileName(path);
        }
        catch (Exception error)
        {
            auditExportPath = null;
            auditExportStatus = "No se pudo guardar la ficha: " + error.Message;
        }
    }

    private static string SafeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return name;
    }
}
