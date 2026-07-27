namespace SMMS.Api.Services.Storage;

/// <summary>
/// Stores uploads on the local filesystem under a configurable base directory
/// ("FileStorage:BasePath", default &lt;ContentRoot&gt;/uploads). Files are given a random,
/// storage-owned name so a malicious client can never control the path on disk, and every
/// read is constrained to the base directory to prevent path-traversal.
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private readonly string _basePath;

    public LocalFileStorage(IConfiguration config, IWebHostEnvironment env)
    {
        _basePath = config["FileStorage:BasePath"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(env.ContentRootPath, "uploads");
        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> SaveAsync(Stream content, string subfolder, string originalFileName, CancellationToken ct = default)
    {
        var safeSub = SanitizeSegment(subfolder);
        var ext = Path.GetExtension(originalFileName);
        if (ext.Length > 10 || ext.Any(c => !char.IsLetterOrDigit(c) && c != '.')) ext = string.Empty;

        var relativePath = Path.Combine(safeSub, $"{Guid.NewGuid():N}{ext}");
        var fullPath = Path.Combine(_basePath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(fs, ct);
        }

        // Always store with forward slashes so the value is portable across OSes.
        return relativePath.Replace('\\', '/');
    }

    public async Task<byte[]?> ReadAsync(string storedPath, CancellationToken ct = default)
    {
        var fullPath = ResolveWithinBase(storedPath);
        if (fullPath is null || !File.Exists(fullPath)) return null;
        return await File.ReadAllBytesAsync(fullPath, ct);
    }

    public void Delete(string storedPath)
    {
        var fullPath = ResolveWithinBase(storedPath);
        if (fullPath is not null && File.Exists(fullPath)) File.Delete(fullPath);
    }

    /// <summary>Resolves a stored relative path to an absolute one, returning null if the result
    /// would escape the base directory (path-traversal defense).</summary>
    private string? ResolveWithinBase(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;
        var baseFull = Path.GetFullPath(_basePath);
        var candidate = Path.GetFullPath(Path.Combine(baseFull, storedPath));
        return candidate.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase) ? candidate : null;
    }

    private static string SanitizeSegment(string segment)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(segment.Where(c => !invalid.Contains(c) && c != '.').ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "misc" : clean;
    }
}
