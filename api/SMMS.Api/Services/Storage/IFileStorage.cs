namespace SMMS.Api.Services.Storage;

/// <summary>
/// Abstraction over binary file storage for user uploads (payment screenshots, etc.).
/// The local-disk implementation is used in dev/Docker; swapping to Azure Blob Storage later
/// is a single DI registration change with no impact on controllers or services.
/// </summary>
public interface IFileStorage
{
    /// <summary>Persists <paramref name="content"/> and returns a server-relative, storage-owned
    /// path (never a client-supplied name) that can later be passed to <see cref="ReadAsync"/>.</summary>
    Task<string> SaveAsync(Stream content, string subfolder, string originalFileName, CancellationToken ct = default);

    /// <summary>Reads a previously stored file, or null if it no longer exists.</summary>
    Task<byte[]?> ReadAsync(string storedPath, CancellationToken ct = default);

    void Delete(string storedPath);
}
