using System.IO;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace StudySuit.PdfReader.Services;

/// <summary>
/// Minimal, high-performance, lightweight OCR engine utilizing Windows 10/11 built-in WinRT OcrEngine.
/// Zero external binaries or model files required; hardware-accelerated and native to Windows.
/// </summary>
public sealed class WindowsOcrService : IOcrService
{
    private OcrEngine? _engine;
    private bool _initialized;

    public bool IsSupported
    {
        get
        {
            EnsureEngine();
            return _engine is not null;
        }
    }

    public string RecognizerLanguage
    {
        get
        {
            EnsureEngine();
            return _engine?.RecognizerLanguage?.LanguageTag ?? "None";
        }
    }

    private void EnsureEngine()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            _engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (_engine is null)
            {
                var enUs = new Windows.Globalization.Language("en-US");
                if (OcrEngine.IsLanguageSupported(enUs))
                {
                    _engine = OcrEngine.TryCreateFromLanguage(enUs);
                }
            }
            if (_engine is null)
            {
                var enGb = new Windows.Globalization.Language("en-GB");
                if (OcrEngine.IsLanguageSupported(enGb))
                {
                    _engine = OcrEngine.TryCreateFromLanguage(enGb);
                }
            }
            if (_engine is null)
            {
                var fallback = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                if (fallback is not null)
                {
                    _engine = OcrEngine.TryCreateFromLanguage(fallback);
                }
            }
        }
        catch
        {
            _engine = null;
        }
    }

    public async Task<string> RecognizeTextAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        EnsureEngine();
        if (_engine is null || imageBytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            using var randomAccessStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(randomAccessStream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(imageBytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
            }

            randomAccessStream.Seek(0);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(randomAccessStream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            var result = await _engine.RecognizeAsync(softwareBitmap);
            if (result is null || result.Lines.Count == 0)
            {
                return string.Empty;
            }

            var lines = result.Lines
                .Select(l => l.Text.Trim())
                .Where(t => !string.IsNullOrEmpty(t));

            return string.Join(" ", lines);
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task<IReadOnlyList<OcrWordBox>> RecognizeWordsAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        EnsureEngine();
        if (_engine is null || imageBytes.Length == 0)
        {
            return [];
        }

        try
        {
            using var randomAccessStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(randomAccessStream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(imageBytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
            }

            randomAccessStream.Seek(0);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(randomAccessStream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            var result = await _engine.RecognizeAsync(softwareBitmap);
            if (result is null || result.Lines.Count == 0)
            {
                return [];
            }

            var words = new List<OcrWordBox>();
            foreach (var line in result.Lines)
            {
                foreach (var word in line.Words)
                {
                    string text = word.Text?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(text))
                    {
                        words.Add(new OcrWordBox(
                            text,
                            word.BoundingRect.X,
                            word.BoundingRect.Y,
                            word.BoundingRect.Width,
                            word.BoundingRect.Height));
                    }
                }
            }

            return words;
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<OcrWordBox>> RecognizeWordsFromBitmapAsync(BitmapSource bitmap, CancellationToken cancellationToken = default)
    {
        EnsureEngine();
        if (_engine is null || bitmap is null)
        {
            return [];
        }

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            byte[] bytes = ms.ToArray();

            return await RecognizeWordsAsync(bytes, cancellationToken);
        }
        catch
        {
            return [];
        }
    }

    public async Task<string> RecognizeTextFromBitmapAsync(BitmapSource bitmap, CancellationToken cancellationToken = default)
    {
        EnsureEngine();
        if (_engine is null || bitmap is null)
        {
            return string.Empty;
        }

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            byte[] bytes = ms.ToArray();

            return await RecognizeTextAsync(bytes, cancellationToken);
        }
        catch
        {
            return string.Empty;
        }
    }
}
