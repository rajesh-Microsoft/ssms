namespace SMMS.Api.Services.Storage;

/// <summary>
/// One place to decide whether an uploaded file is acceptable. Payment proofs, complaint photos
/// and reimbursement bills each grew their own copy of these two checks; a third divergent copy
/// is how one of them quietly ends up accepting a 40 MB executable.
/// </summary>
public static class UploadRules
{
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>Screenshots and photos.</summary>
    public static readonly string[] Images = ["image/"];

    /// <summary>Bills and invoices, which are as often a PDF as a photo.</summary>
    public static readonly string[] ImagesAndPdf = ["image/", "application/pdf"];

    /// <summary>Returns null when the file is acceptable, or a message safe to show the user.</summary>
    public static string? Validate(IFormFile file, string[] allowedPrefixes, long maxBytes = MaxBytes)
    {
        if (file is null || file.Length == 0) return "The file is empty.";
        if (file.Length > maxBytes) return $"Files must be under {maxBytes / (1024 * 1024)} MB.";

        var type = file.ContentType ?? string.Empty;
        if (!allowedPrefixes.Any(p => type.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            var readable = allowedPrefixes.Contains("application/pdf") ? "images and PDFs" : "image files";
            return $"Only {readable} are allowed.";
        }

        return null;
    }
}
