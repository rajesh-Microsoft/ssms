namespace SMMS.Api.Data.Tenancy;

/// <summary>
/// Reads tenants from the "Tenants" configuration section (appsettings.json / env vars locally;
/// Azure App Configuration + Key Vault-backed connection strings in production). Registered as a
/// singleton behind <see cref="ITenantStore"/> so the backing source can be swapped later (e.g. for
/// an Azure App Configuration-backed implementation) without touching any calling code.
/// </summary>
public class ConfigTenantStore : ITenantStore
{
    private readonly Dictionary<string, TenantInfo> _tenants;

    public ConfigTenantStore(IConfiguration configuration)
    {
        _tenants = configuration.GetSection("Tenants").GetChildren()
            .Where(section => !string.IsNullOrEmpty(section["ConnectionString"]))
            .ToDictionary(
                section => section.Key,
                section => new TenantInfo(section.Key, section["DisplayName"] ?? section.Key, section["ConnectionString"]!),
                StringComparer.OrdinalIgnoreCase);

        if (_tenants.Count == 0)
        {
            throw new InvalidOperationException(
                "No tenants configured. Add at least one entry under the \"Tenants\" configuration section " +
                "(each with a ConnectionString and DisplayName).");
        }
    }

    public TenantInfo? GetByKey(string key) => _tenants.TryGetValue(key, out var tenant) ? tenant : null;

    public IEnumerable<TenantInfo> GetAll() => _tenants.Values;
}
