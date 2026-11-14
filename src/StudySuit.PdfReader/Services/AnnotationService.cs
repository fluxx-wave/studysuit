using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public interface IAnnotationService
{
    IReadOnlyList<PdfAnnotation> LoadAnnotations(string pdfFilePath);
    void SaveAnnotations(string pdfFilePath, IReadOnlyList<PdfAnnotation> annotations);
    IReadOnlyList<RecentAnnotationSummary> GetRecentAnnotations(int maxCount = 20);
    int GetTotalAnnotationCount();
    string ExportToMarkdown(PdfDocumentInfo documentInfo, IReadOnlyList<PdfAnnotation> annotations);
}

public sealed class AnnotationService : IAnnotationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _annotationsDirectory;

    public AnnotationService(string? customDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(customDirectory))
        {
            _annotationsDirectory = customDirectory;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _annotationsDirectory = Path.Combine(appData, "StudySuit.PdfReader", "annotations");
        }
    }

    public IReadOnlyList<PdfAnnotation> LoadAnnotations(string pdfFilePath)
    {
        try
        {
            string storePath = GetStorePath(pdfFilePath);
            if (!File.Exists(storePath))
            {
                return [];
            }

            string json = File.ReadAllText(storePath);
            var store = JsonSerializer.Deserialize<DocumentAnnotationStore>(json, JsonOptions);
            return store?.Annotations ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveAnnotations(string pdfFilePath, IReadOnlyList<PdfAnnotation> annotations)
    {
        try
        {
            string storePath = GetStorePath(pdfFilePath);
            if (annotations.Count == 0)
            {
                if (File.Exists(storePath))
                {
                    File.Delete(storePath);
                }

                return;
            }

            Directory.CreateDirectory(_annotationsDirectory);
            var store = new DocumentAnnotationStore
            {
                FilePath = pdfFilePath,
                FileName = Path.GetFileName(pdfFilePath),
                UpdatedUtc = DateTime.UtcNow,
                Annotations = [.. annotations]
            };

            string json = JsonSerializer.Serialize(store, JsonOptions);
            File.WriteAllText(storePath, json);
        }
        catch
        {
            // Non-fatal persistence error; keep UI responsive.
        }
    }

    public IReadOnlyList<RecentAnnotationSummary> GetRecentAnnotations(int maxCount = 20)
    {
        var results = new List<RecentAnnotationSummary>();
        if (!Directory.Exists(_annotationsDirectory))
        {
            return results;
        }

        foreach (string file in Directory.EnumerateFiles(_annotationsDirectory, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var store = JsonSerializer.Deserialize<DocumentAnnotationStore>(json, JsonOptions);
                if (store is null || string.IsNullOrWhiteSpace(store.FilePath))
                {
                    continue;
                }

                foreach (var ann in store.Annotations)
                {
                    results.Add(new RecentAnnotationSummary
                    {
                        FilePath = store.FilePath,
                        FileName = string.IsNullOrWhiteSpace(store.FileName)
                            ? Path.GetFileName(store.FilePath)
                            : store.FileName,
                        PageNumber = ann.PageNumber,
                        Type = ann.Type,
                        ColorHex = ann.ColorHex,
                        PreviewText = ann.SummaryText,
                        CreatedUtc = ann.CreatedUtc
                    });
                }
            }
            catch
            {
                // Skip malformed store files gracefully.
            }
        }

        return results
            .OrderByDescending(r => r.CreatedUtc)
            .Take(maxCount)
            .ToList();
    }

    public int GetTotalAnnotationCount()
    {
        if (!Directory.Exists(_annotationsDirectory))
        {
            return 0;
        }

        int total = 0;
        foreach (string file in Directory.EnumerateFiles(_annotationsDirectory, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var store = JsonSerializer.Deserialize<DocumentAnnotationStore>(json, JsonOptions);
                total += store?.Annotations.Count ?? 0;
            }
            catch
            {
            }
        }

        return total;
    }

    public string ExportToMarkdown(PdfDocumentInfo documentInfo, IReadOnlyList<PdfAnnotation> annotations)
    {
        var sb = new StringBuilder();
        string title = string.IsNullOrWhiteSpace(documentInfo.Title)
            ? documentInfo.FileName
            : documentInfo.Title;

        sb.AppendLine($"# Study Notes: {title}");
        sb.AppendLine();
        sb.AppendLine($"- **File:** `{documentInfo.FileName}`");
        if (!string.IsNullOrWhiteSpace(documentInfo.Author))
        {
            sb.AppendLine($"- **Author:** {documentInfo.Author}");
        }
        sb.AppendLine($"- **Pages:** {documentInfo.PageCount}");
        sb.AppendLine($"- **Total Annotations:** {annotations.Count}");
        sb.AppendLine();

        foreach (var pageGroup in annotations.OrderBy(a => a.PageNumber).ThenBy(a => a.CreatedUtc).GroupBy(a => a.PageNumber))
        {
            sb.AppendLine($"## Page {pageGroup.Key}");
            sb.AppendLine();

            foreach (var ann in pageGroup)
            {
                if (!string.IsNullOrWhiteSpace(ann.Text))
                {
                    sb.AppendLine($"> [{ann.TypeDisplay}] {ann.Text}");
                    if (!string.IsNullOrWhiteSpace(ann.NoteComment))
                    {
                        sb.AppendLine($"  - **Note:** {ann.NoteComment}");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(ann.NoteComment))
                {
                    sb.AppendLine($"- **[{ann.TypeDisplay}]** {ann.NoteComment}");
                }
                else
                {
                    sb.AppendLine($"- **[{ann.TypeDisplay}]** *(Visual markup)*");
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private string GetStorePath(string pdfFilePath)
    {
        string normalized = Path.GetFullPath(pdfFilePath).Trim().ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        string hex = Convert.ToHexString(hash).ToLowerInvariant();
        return Path.Combine(_annotationsDirectory, $"{hex}.json");
    }
}

