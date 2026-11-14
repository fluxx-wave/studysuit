using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFtoImage;
using SkiaSharp;
using StudySuit.PdfReader.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Outline;

namespace StudySuit.PdfReader.Services;

public sealed class PdfService : IPdfService
{
    private readonly SemaphoreSlim _pdfiumGate = new(1, 1);

    public async Task<PdfDocumentInfo> OpenDocumentAsync(
        string filePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("PDF file not found.", filePath);
        }

        // First read metadata, bookmarks, and page dimensions via PdfPig (pure managed).
        var pigInfo = await Task.Run(() => ReadPdfPigMetadata(filePath, password, cancellationToken), cancellationToken);

        // Also query PDFium page sizes (handles CropBox/MediaBox and intrinsic rotation consistently with rendering).
        IReadOnlyList<PdfPageSize> pdfiumSizes = await GetPdfiumPageSizesAsync(filePath, password, cancellationToken);
        IReadOnlyList<PdfPageSize> finalSizes = pdfiumSizes.Count == pigInfo.PageCount
            ? pdfiumSizes
            : pigInfo.PageSizes;

        return new PdfDocumentInfo
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Title = pigInfo.Title,
            Author = pigInfo.Author,
            Subject = pigInfo.Subject,
            Creator = pigInfo.Creator,
            Producer = pigInfo.Producer,
            PdfVersion = pigInfo.PdfVersion,
            CreationDate = pigInfo.CreationDate,
            FileSizeBytes = new FileInfo(filePath).Length,
            PageCount = pdfiumSizes.Count > 0 ? pdfiumSizes.Count : pigInfo.PageCount,
            PageSizes = finalSizes,
            Bookmarks = pigInfo.Bookmarks
        };
    }

    public async Task<BitmapSource> RenderPageAsync(
        string filePath,
        int pageNumber,
        int dpi,
        int rotationDegrees,
        bool invertColors = false,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page number must be >= 1.");
        }

        int clampedDpi = Math.Clamp(dpi, 24, 400);
        var options = new PDFtoImage.RenderOptions
        {
            Dpi = clampedDpi,
            Rotation = ToPdfRotation(rotationDegrees),
            WithAnnotations = true,
            WithFormFill = true,
            WithAspectRatio = true
        };

        await _pdfiumGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using SKBitmap rendered = Conversion.ToImage(
                    stream,
                    page: (Index)(pageNumber - 1),
                    leaveOpen: true,
                    password: password,
                    options: options);

                return ConvertToFrozenBitmapSource(rendered, invertColors);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPasswordException(ex))
        {
            throw new PdfPasswordRequiredException(
                "A password is required to open this PDF.",
                !string.IsNullOrEmpty(password),
                ex);
        }
        finally
        {
            _pdfiumGate.Release();
        }
    }

    public Task<IReadOnlyList<PdfWordBox>> GetPageWordsAsync(
        string filePath,
        int pageNumber,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<PdfWordBox>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = OpenPdfPig(filePath, password);
            if (pageNumber < 1 || pageNumber > document.NumberOfPages)
            {
                return [];
            }

            var page = document.GetPage(pageNumber);
            return ExtractNormalizedWords(page);
        }, cancellationToken);
    }

    public Task<string> GetPageTextAsync(
        string filePath,
        int pageNumber,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = OpenPdfPig(filePath, password);
            if (pageNumber < 1 || pageNumber > document.NumberOfPages)
            {
                return string.Empty;
            }

            var page = document.GetPage(pageNumber);
            var words = page.GetWords();
            return string.Join(' ', words.Select(w => w.Text));
        }, cancellationToken);
    }

    public Task<IReadOnlyList<SearchMatch>> SearchAsync(
        string filePath,
        string query,
        string? password = null,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<SearchMatch>>([]);
        }

        string trimmedQuery = query.Trim();

        return Task.Run<IReadOnlyList<SearchMatch>>(() =>
        {
            var results = new List<SearchMatch>();
            using var document = OpenPdfPig(filePath, password);

            int matchIndex = 0;
            for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(pageNum);
                var words = ExtractNormalizedWords(page);
                if (words.Count == 0)
                {
                    continue;
                }

                foreach (var hit in FindWordMatches(words, trimmedQuery, caseSensitive))
                {
                    results.Add(new SearchMatch
                    {
                        MatchIndex = matchIndex++,
                        PageNumber = pageNum,
                        Snippet = hit.Snippet,
                        Rects = hit.Rects
                    });
                }
            }

            return results;
        }, cancellationToken);
    }

    public async Task ExportPageAsPngAsync(
        string filePath,
        int pageNumber,
        string outputPngPath,
        int dpi,
        int rotationDegrees,
        bool invertColors = false,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        BitmapSource bitmap = await RenderPageAsync(
            filePath,
            pageNumber,
            dpi,
            rotationDegrees,
            invertColors,
            password,
            cancellationToken).ConfigureAwait(false);

        await Task.Run(() =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var outStream = File.Create(outputPngPath);
            encoder.Save(outStream);
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static IReadOnlyList<(string Snippet, IReadOnlyList<NormalizedRect> Rects)> FindWordMatches(
        IReadOnlyList<PdfWordBox> words,
        string query,
        bool caseSensitive)
    {
        var matches = new List<(string Snippet, IReadOnlyList<NormalizedRect> Rects)>();
        if (words.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return matches;
        }

        var sb = new StringBuilder();
        var wordSpans = new List<(int Start, int Length, int WordIdx)>(words.Count);
        for (int i = 0; i < words.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            int start = sb.Length;
            sb.Append(words[i].Text);
            wordSpans.Add((start, words[i].Text.Length, i));
        }

        string fullText = sb.ToString();
        StringComparison comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        int searchFrom = 0;
        while (searchFrom < fullText.Length)
        {
            int foundIdx = fullText.IndexOf(query, searchFrom, comparison);
            if (foundIdx < 0)
            {
                break;
            }

            int foundEnd = foundIdx + query.Length;
            var hitRects = new List<NormalizedRect>();

            foreach (var (start, length, wordIdx) in wordSpans)
            {
                int wordEnd = start + length;
                if (wordEnd > foundIdx && start < foundEnd)
                {
                    hitRects.Add(words[wordIdx].Bounds);
                }
                else if (start >= foundEnd)
                {
                    break;
                }
            }

            if (hitRects.Count > 0)
            {
                int snippetStart = Math.Max(0, foundIdx - 32);
                int snippetEnd = Math.Min(fullText.Length, foundEnd + 40);
                string prefix = snippetStart > 0 ? "…" : string.Empty;
                string suffix = snippetEnd < fullText.Length ? "…" : string.Empty;
                string snippet = string.Concat(prefix, fullText[snippetStart..snippetEnd].Trim(), suffix);
                matches.Add((snippet, hitRects));
            }

            searchFrom = foundIdx + Math.Max(1, query.Length);
        }

        return matches;
    }

    private async Task<IReadOnlyList<PdfPageSize>> GetPdfiumPageSizesAsync(
        string filePath,
        string? password,
        CancellationToken cancellationToken)
    {
        await _pdfiumGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run<IReadOnlyList<PdfPageSize>>(() =>
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var sizes = Conversion.GetPageSizes(stream, leaveOpen: true, password: password);
                // PDFium returns sizes in PDF points (1/72 inch)
                return sizes.Select(s => new PdfPageSize(
                    Math.Max(1, s.Width),
                    Math.Max(1, s.Height))).ToList();
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPasswordException(ex))
        {
            throw new PdfPasswordRequiredException(
                "A password is required to open this PDF.",
                !string.IsNullOrEmpty(password),
                ex);
        }
        finally
        {
            _pdfiumGate.Release();
        }
    }

    private static PdfDocument OpenPdfPig(string filePath, string? password)
    {
        try
        {
            var options = new ParsingOptions();
            if (password is not null)
            {
                options.Password = password;
            }

            return PdfDocument.Open(filePath, options);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new PdfPasswordRequiredException(
                "A password is required to open this PDF.",
                !string.IsNullOrEmpty(password),
                ex);
        }
    }

    private static PdfDocumentInfo ReadPdfPigMetadata(string filePath, string? password, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var document = OpenPdfPig(filePath, password);

        var pageSizes = new List<PdfPageSize>(document.NumberOfPages);
        for (int i = 1; i <= document.NumberOfPages; i++)
        {
            ct.ThrowIfCancellationRequested();
            var page = document.GetPage(i);
            pageSizes.Add(new PdfPageSize(Math.Max(1, page.Width), Math.Max(1, page.Height)));
        }

        var bookmarks = new List<PdfBookmarkItem>();
        if (document.TryGetBookmarks(out var rawBookmarks) && rawBookmarks.Roots is not null)
        {
            foreach (var root in rawBookmarks.Roots)
            {
                bookmarks.Add(ConvertBookmark(root));
            }
        }

        var info = document.Information;
        return new PdfDocumentInfo
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Title = info.Title ?? string.Empty,
            Author = info.Author ?? string.Empty,
            Subject = info.Subject ?? string.Empty,
            Creator = info.Creator ?? string.Empty,
            Producer = info.Producer ?? string.Empty,
            PdfVersion = document.Version.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            CreationDate = info.CreationDate ?? string.Empty,
            FileSizeBytes = new FileInfo(filePath).Length,
            PageCount = document.NumberOfPages,
            PageSizes = pageSizes,
            Bookmarks = bookmarks
        };
    }

    private static PdfBookmarkItem ConvertBookmark(BookmarkNode node)
    {
        int? pageNum = node is DocumentBookmarkNode docNode && docNode.PageNumber > 0
            ? docNode.PageNumber
            : null;

        var item = new PdfBookmarkItem
        {
            Title = string.IsNullOrWhiteSpace(node.Title) ? "(Untitled)" : node.Title.Trim(),
            PageNumber = pageNum
        };

        if (node.Children is not null)
        {
            foreach (var child in node.Children)
            {
                item.Children.Add(ConvertBookmark(child));
            }
        }

        return item;
    }

    private static List<PdfWordBox> ExtractNormalizedWords(UglyToad.PdfPig.Content.Page page)
    {
        var crop = page.CropBox.Bounds;
        double originX = crop.Width > 0 ? crop.Left : 0;
        double originY = crop.Height > 0 ? crop.Bottom : 0;
        double boxW = crop.Width > 0 ? crop.Width : page.Width;
        double boxH = crop.Height > 0 ? crop.Height : page.Height;

        if (boxW <= 0 || boxH <= 0)
        {
            return [];
        }

        var list = new List<PdfWordBox>();
        foreach (var word in page.GetWords())
        {
            if (string.IsNullOrWhiteSpace(word.Text))
            {
                continue;
            }

            var b = word.BoundingBox;
            double x = Math.Clamp((b.Left - originX) / boxW, 0, 1);
            double y = Math.Clamp(1.0 - (b.Top - originY) / boxH, 0, 1);
            double w = Math.Clamp(b.Width / boxW, 0, 1 - x);
            double h = Math.Clamp(b.Height / boxH, 0, 1 - y);

            if (w > 0 && h > 0)
            {
                list.Add(new PdfWordBox(word.Text, new NormalizedRect(x, y, w, h)));
            }
        }

        return list;
    }

    private static BitmapSource ConvertToFrozenBitmapSource(SKBitmap source, bool invertColors)
    {
        SKBitmap? converted = null;
        SKBitmap target = source;
        if (source.ColorType != SKColorType.Bgra8888)
        {
            converted = source.Copy(SKColorType.Bgra8888);
            target = converted ?? throw new InvalidOperationException("Failed to convert rendered PDF bitmap to Bgra8888.");
        }

        try
        {
            int width = target.Width;
            int height = target.Height;
            int stride = target.RowBytes;
            byte[] pixels = target.GetPixelSpan().ToArray();

            if (invertColors)
            {
                // Invert RGB channels in BGRA32 for comfortable Night Reading Mode while keeping alpha intact.
                for (int i = 0; i + 3 < pixels.Length; i += 4)
                {
                    pixels[i] = (byte)(255 - pixels[i]);
                    pixels[i + 1] = (byte)(255 - pixels[i + 1]);
                    pixels[i + 2] = (byte)(255 - pixels[i + 2]);
                }
            }

            var bitmap = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                stride);
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            converted?.Dispose();
        }
    }

    private static PdfRotation ToPdfRotation(int rotationDegrees)
    {
        int normalized = ((rotationDegrees % 360) + 360) % 360;
        return normalized switch
        {
            90 => PdfRotation.Rotate90,
            180 => PdfRotation.Rotate180,
            270 => PdfRotation.Rotate270,
            _ => PdfRotation.Rotate0
        };
    }

    private static bool IsPasswordException(Exception ex)
    {
        return ex is PdfPasswordRequiredException ||
               ex is PdfDocumentEncryptedException ||
               ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);
    }
}
