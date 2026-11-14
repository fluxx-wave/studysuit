using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public interface INcertLibraryService
{
    IReadOnlyList<NcertBookItem> GetAllBooks();
    string GetLibraryRootDirectory();
    Task RefreshDownloadStatesAsync(IEnumerable<NcertBookItem> books);
    Task<string> DownloadBookAsync(
        NcertBookItem book,
        IProgress<(double Percent, string Status)>? progress = null,
        CancellationToken ct = default);
    Task DeleteBookAsync(NcertBookItem book);
}

public sealed class NcertLibraryService : INcertLibraryService
{
    private readonly HttpClient _http;
    private readonly string _libraryRoot;

    public NcertLibraryService(HttpClient? httpClient = null, string? customLibraryRoot = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromHours(1) };
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }
        if (!_http.DefaultRequestHeaders.Contains("Referer"))
        {
            _http.DefaultRequestHeaders.Add("Referer", "https://ncert.nic.in/");
        }

        _libraryRoot = customLibraryRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StudySuit.PdfReader",
            "library",
            "ncert");

        try
        {
            Directory.CreateDirectory(_libraryRoot);
        }
        catch
        {
        }
    }

    public string GetLibraryRootDirectory() => _libraryRoot;

    public IReadOnlyList<NcertBookItem> GetAllBooks()
    {
        // Return a fresh list
        var list = new List<NcertBookItem>();
        foreach (var b in NcertLibraryCatalog.AllBooks)
        {
            var item = new NcertBookItem
            {
                Code = b.Code,
                Title = b.Title,
                ShortTitle = b.ShortTitle,
                ClassNumber = b.ClassNumber,
                Subject = b.Subject,
                SubjectCategory = b.SubjectCategory,
                Description = b.Description,
                BadgeColorHex = b.BadgeColorHex,
                EstimatedSizeDisplay = b.EstimatedSizeDisplay
            };
            PopulateBookFromDisk(item);
            list.Add(item);
        }

        return list;
    }

    public Task RefreshDownloadStatesAsync(IEnumerable<NcertBookItem> books)
    {
        foreach (var b in books)
        {
            PopulateBookFromDisk(b);
        }

        return Task.CompletedTask;
    }

    private static void RunOnUIThread(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    private void PopulateBookFromDisk(NcertBookItem book)
    {
        string bookDir = Path.Combine(_libraryRoot, book.Code);
        if (!Directory.Exists(bookDir))
        {
            RunOnUIThread(() =>
            {
                book.IsDownloaded = false;
                book.PrimaryPdfPath = null;
                book.Chapters.Clear();
            });
            return;
        }

        var pdfFiles = Directory.GetFiles(bookDir, "*.pdf")
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pdfFiles.Count == 0)
        {
            RunOnUIThread(() =>
            {
                book.IsDownloaded = false;
                book.PrimaryPdfPath = null;
                book.Chapters.Clear();
            });
            return;
        }

        string? preferredPrimary = null;
        int chapterIdx = 1;
        var chapterList = new List<NcertChapterItem>();

        foreach (var path in pdfFiles)
        {
            string fname = Path.GetFileName(path);
            int chNum = ExtractChapterNumber(fname, chapterIdx);

            string title = FormatChapterTitle(book, fname, chNum);
            chapterList.Add(new NcertChapterItem
            {
                ChapterNumber = chNum,
                Title = title,
                FileName = fname,
                FilePath = path,
                IsAvailable = true
            });

            // Look for chapter 1 (e.g. *01.pdf) or first available non-preliminary PDF
            if (preferredPrimary is null && (fname.Contains("01", StringComparison.OrdinalIgnoreCase) || !fname.Contains("ps", StringComparison.OrdinalIgnoreCase)))
            {
                preferredPrimary = path;
            }

            chapterIdx++;
        }

        string primary = preferredPrimary ?? pdfFiles[0];
        string statusText = $"{pdfFiles.Count} Chapter{(pdfFiles.Count > 1 ? "s" : "")} Downloaded";

        RunOnUIThread(() =>
        {
            book.IsDownloaded = true;
            book.PrimaryPdfPath = primary;
            book.DownloadStatusText = statusText;
            book.Chapters.Clear();
            foreach (var ch in chapterList)
            {
                book.Chapters.Add(ch);
            }
        });
    }

    public async Task<string> DownloadBookAsync(
        NcertBookItem book,
        IProgress<(double Percent, string Status)>? progress = null,
        CancellationToken ct = default)
    {
        book.IsDownloading = true;
        book.DownloadProgressPercent = 0;
        book.DownloadStatusText = "Connecting to NCERT server...";
        progress?.Report((2, "Connecting to NCERT server..."));

        string bookDir = Path.Combine(_libraryRoot, book.Code);
        Directory.CreateDirectory(bookDir);

        string zipUrl = $"https://ncert.nic.in/textbook/pdf/{book.Code}dd.zip";
        string tempZip = Path.Combine(bookDir, $"temp_{book.Code}.zip");

        bool zipSuccess = false;
        try
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, zipUrl))
            {
                req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                req.Headers.Add("Referer", "https://ncert.nic.in/");

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    long totalBytes = resp.Content.Headers.ContentLength ?? 40000000;
                    long totalRead = 0;

                    await using var contentStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    await using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                    byte[] buffer = new byte[81920];
                    int read;
                    long lastReportTick = Environment.TickCount64;

                    while ((read = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        totalRead += read;

                        long now = Environment.TickCount64;
                        if (now - lastReportTick >= 100 || totalRead == totalBytes)
                        {
                            lastReportTick = now;
                            double pct = Math.Min(90.0, (double)totalRead / totalBytes * 90.0);
                            string mbStr = $"{totalRead / (1024.0 * 1024.0):F1} MB / {totalBytes / (1024.0 * 1024.0):F1} MB";
                            book.DownloadProgressPercent = pct;
                            book.DownloadStatusText = $"Downloading {book.ShortTitle} ({pct:F0}%) • {mbStr}";
                            progress?.Report((pct, book.DownloadStatusText));
                        }
                    }

                    zipSuccess = true;
                }
            }

            if (zipSuccess && File.Exists(tempZip))
            {
                book.DownloadProgressPercent = 92;
                book.DownloadStatusText = "Extracting chapters...";
                progress?.Report((92, "Extracting chapters..."));

                await Task.Run(() =>
                {
                    using var archive = ZipFile.OpenRead(tempZip);
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            string destPath = Path.Combine(bookDir, Path.GetFileName(entry.FullName));
                            entry.ExtractToFile(destPath, overwrite: true);
                        }
                    }
                }, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            zipSuccess = false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempZip))
                {
                    File.Delete(tempZip);
                }
            }
            catch
            {
            }
        }

        // If zip failed or produced no PDFs, fallback to downloading chapter 1 directly
        var existingPdfs = Directory.GetFiles(bookDir, "*.pdf");
        if (existingPdfs.Length == 0)
        {
            string ch1Url = $"https://ncert.nic.in/textbook/pdf/{book.Code}01.pdf";
            string ch1Dest = Path.Combine(bookDir, $"{book.Code}01.pdf");

            book.DownloadStatusText = "Downloading Chapter 1...";
            progress?.Report((50, "Downloading Chapter 1..."));

            using var chReq = new HttpRequestMessage(HttpMethod.Get, ch1Url);
            chReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            chReq.Headers.Add("Referer", "https://ncert.nic.in/");

            using var chResp = await _http.SendAsync(chReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            chResp.EnsureSuccessStatusCode();

            await using var contentStream = await chResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var fileStream = new FileStream(ch1Dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await contentStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
        }

        PopulateBookFromDisk(book);
        RunOnUIThread(() =>
        {
            book.IsDownloading = false;
            book.DownloadProgressPercent = 100;
            book.DownloadStatusText = "Downloaded and ready!";
        });
        progress?.Report((100, "Downloaded and ready!"));

        return book.PrimaryPdfPath ?? string.Empty;
    }

    public Task DeleteBookAsync(NcertBookItem book)
    {
        string bookDir = Path.Combine(_libraryRoot, book.Code);
        try
        {
            if (Directory.Exists(bookDir))
            {
                Directory.Delete(bookDir, recursive: true);
            }
        }
        catch
        {
        }

        RunOnUIThread(() =>
        {
            book.IsDownloaded = false;
            book.PrimaryPdfPath = null;
            book.Chapters.Clear();
            book.DownloadStatusText = "Available to Download";
        });
        return Task.CompletedTask;
    }

    private static int ExtractChapterNumber(string fileName, int defaultIndex)
    {
        var match = Regex.Match(fileName, @"(\d+)\.pdf$", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
        {
            return num;
        }

        return defaultIndex;
    }

    private static string FormatChapterTitle(NcertBookItem book, string fileName, int chNum)
    {
        if (fileName.Contains("ps", StringComparison.OrdinalIgnoreCase))
        {
            return "Prelims & Table of Contents";
        }

        if (fileName.Contains("an", StringComparison.OrdinalIgnoreCase))
        {
            return "Answers & Solutions";
        }

        return $"Chapter {chNum}";
    }
}

