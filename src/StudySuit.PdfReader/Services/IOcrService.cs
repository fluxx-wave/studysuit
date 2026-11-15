using System.Windows.Media.Imaging;

namespace StudySuit.PdfReader.Services;

/// <summary>
/// Minimal, efficient, lightweight OCR engine interface for visual text extraction from circled areas.
/// </summary>
public interface IOcrService
{
    bool IsSupported { get; }
    string RecognizerLanguage { get; }
    Task<string> RecognizeTextFromBitmapAsync(BitmapSource bitmap, CancellationToken cancellationToken = default);
    Task<string> RecognizeTextAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
}

