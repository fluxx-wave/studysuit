using System.Windows.Media.Imaging;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public sealed class PdfPasswordRequiredException : Exception
{
    public bool IncorrectPassword { get; }

    public PdfPasswordRequiredException(string message, bool incorrectPassword = false, Exception? innerException = null)
        : base(message, innerException)
    {
        IncorrectPassword = incorrectPassword;
    }
}

public interface IPdfService
{
    Task<PdfDocumentInfo> OpenDocumentAsync(string filePath, string? password = null, CancellationToken cancellationToken = default);

    Task<BitmapSource> RenderPageAsync(
        string filePath,
        int pageNumber,
        int dpi,
        int rotationDegrees,
        bool invertColors = false,
        string? password = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PdfWordBox>> GetPageWordsAsync(
        string filePath,
        int pageNumber,
        string? password = null,
        CancellationToken cancellationToken = default);

    Task<string> GetPageTextAsync(
        string filePath,
        int pageNumber,
        string? password = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchMatch>> SearchAsync(
        string filePath,
        string query,
        string? password = null,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default);

    Task ExportPageAsPngAsync(
        string filePath,
        int pageNumber,
        string outputPngPath,
        int dpi,
        int rotationDegrees,
        bool invertColors = false,
        string? password = null,
        CancellationToken cancellationToken = default);
}

