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
                EstimatedSizeDisplay = b.EstimatedSizeDisplay,
                TotalChapters = b.TotalChapters
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
                book.ClearChapters();
                book.DownloadStatusText = "Available to Download";
            });
            return;
        }

        var pdfFiles = Directory.GetFiles(bookDir, "*.pdf")
            .OrderBy(f =>
            {
                string name = Path.GetFileName(f).ToLowerInvariant();
                if (name.Contains("ps")) return -1;
                if (name.Contains("an")) return 9999;
                var m = Regex.Match(name, @"(\d+)\.pdf");
                return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 500;
            })
            .ToList();

        if (pdfFiles.Count == 0)
        {
            RunOnUIThread(() =>
            {
                book.IsDownloaded = false;
                book.PrimaryPdfPath = null;
                book.ClearChapters();
                book.DownloadStatusText = "Available to Download";
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

        // If only 1 PDF is present and book has multiple chapters, it was an incomplete download
        bool isComplete = pdfFiles.Count > 1 || book.TotalChapters <= 1;
        string statusText = isComplete
            ? $"{pdfFiles.Count} Chapter{(pdfFiles.Count > 1 ? "s" : "")} Downloaded"
            : $"1 of {book.TotalChapters} Chapters (Incomplete) • Download all";

        RunOnUIThread(() =>
        {
            book.IsDownloaded = isComplete;
            book.PrimaryPdfPath = primary;
            book.DownloadStatusText = statusText;
            book.UpdateChapters(chapterList);
        });
    }

    public async Task<string> DownloadBookAsync(
        NcertBookItem book,
        IProgress<(double Percent, string Status)>? progress = null,
        CancellationToken ct = default)
    {
        RunOnUIThread(() =>
        {
            book.IsDownloading = true;
            book.DownloadProgressPercent = 0;
            book.DownloadStatusText = "Connecting to NCERT server...";
        });
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
                            double pct = Math.Min(85.0, (double)totalRead / totalBytes * 85.0);
                            string mbStr = $"{totalRead / (1024.0 * 1024.0):F1} MB / {totalBytes / (1024.0 * 1024.0):F1} MB";
                            string status = $"Downloading {book.ShortTitle} ({pct:F0}%) • {mbStr}";
                            RunOnUIThread(() =>
                            {
                                book.DownloadProgressPercent = pct;
                                book.DownloadStatusText = status;
                            });
                            progress?.Report((pct, status));
                        }
                    }

                    if (File.Exists(tempZip) && new FileInfo(tempZip).Length > 100000)
                    {
                        RunOnUIThread(() =>
                        {
                            book.DownloadProgressPercent = 88;
                            book.DownloadStatusText = "Extracting all chapters...";
                        });
                        progress?.Report((88, "Extracting all chapters..."));

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

                        var extractedPdfs = Directory.GetFiles(bookDir, "*.pdf");
                        if (extractedPdfs.Length >= Math.Min(book.TotalChapters, 2))
                        {
                            zipSuccess = true;
                        }
                    }
                }
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

        // If ZIP failed or didn't extract the chapters, download ALL chapters individually
        if (!zipSuccess)
        {
            int totalExpected = Math.Max(book.TotalChapters, 1);
            RunOnUIThread(() =>
            {
                book.DownloadProgressPercent = 5;
                book.DownloadStatusText = $"Downloading {book.ShortTitle} (all {totalExpected} chapters)...";
            });
            progress?.Report((5, $"Downloading {book.ShortTitle} (all {totalExpected} chapters)..."));

            // 1. Download Prelims / Table of Contents (*ps.pdf)
            await DownloadSinglePdfFileAsync(book, "ps", bookDir, ct).ConfigureAwait(false);

            // 2. Download all chapters (01.pdf through N.pdf)
            int consecutiveFailures = 0;
            for (int ch = 1; ch <= totalExpected + 3; ch++)
            {
                ct.ThrowIfCancellationRequested();

                double pct = Math.Min(95.0, 5.0 + ((double)ch / totalExpected) * 88.0);
                string chNumStr = $"{ch:D2}";
                string statusMsg = $"Downloading Chapter {ch} of {totalExpected} ({pct:F0}%)...";

                RunOnUIThread(() =>
                {
                    book.DownloadProgressPercent = pct;
                    book.DownloadStatusText = statusMsg;
                });
                progress?.Report((pct, statusMsg));

                bool ok = await DownloadSinglePdfFileAsync(book, chNumStr, bookDir, ct).ConfigureAwait(false);
                if (ok)
                {
                    consecutiveFailures = 0;
                }
                else
                {
                    consecutiveFailures++;
                    if (ch >= totalExpected && consecutiveFailures >= 2)
                    {
                        break;
                    }
                }
            }

            // 3. Download Answers / Appendix (*an.pdf) if available
            await DownloadSinglePdfFileAsync(book, "an", bookDir, ct).ConfigureAwait(false);
        }

        PopulateBookFromDisk(book);
        RunOnUIThread(() =>
        {
            book.IsDownloading = false;
            book.DownloadProgressPercent = 100;
            book.DownloadStatusText = $"{book.Chapters.Count} Chapters Downloaded • Ready";
            book.IsDownloaded = true;
        });
        progress?.Report((100, $"{book.Chapters.Count} Chapters Downloaded • Ready"));

        return book.PrimaryPdfPath ?? string.Empty;
    }

    private async Task<bool> DownloadSinglePdfFileAsync(
        NcertBookItem book,
        string suffix,
        string bookDir,
        CancellationToken ct)
    {
        string fileName = $"{book.Code}{suffix}.pdf";
        string destPath = Path.Combine(bookDir, fileName);

        if (File.Exists(destPath) && new FileInfo(destPath).Length > 10240)
        {
            return true; // Already downloaded
        }

        string url = $"https://ncert.nic.in/textbook/pdf/{fileName}";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            req.Headers.Add("Referer", "https://ncert.nic.in/");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return false;
            }

            await using var contentStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await contentStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);

            return File.Exists(destPath) && new FileInfo(destPath).Length > 1024;
        }
        catch
        {
            try
            {
                if (File.Exists(destPath)) File.Delete(destPath);
            }
            catch
            {
            }
            return false;
        }
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
            book.ClearChapters();
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
