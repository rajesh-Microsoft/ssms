using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data.Control;

namespace SMMS.Api.Data.Tenancy;

/// <summary>Tenant registry backed by SmmsControlDb (Societies table). Replaces ConfigTenantStore
/// and enables runtime society provisioning. Caches in memory; call Reload() after changes.</summary>
public class DbTenantStore : ITenantStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _connectionTemplate;
    private readonly object _lock = new();
    private Dictionary<string, TenantInfo>? _cache;

    public DbTenantStore(IServiceScopeFactory scopeFactory, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _connectionTemplate = configuration["ControlPlane:TenantConnectionTemplate"]
            ?? throw new InvalidOperationException(
                "ControlPlane:TenantConnectionTemplate is not configured (must contain a {DbName} placeholder).");
    }

    private Dictionary<string, TenantInfo> Cache
    {
        get
        {
            if (_cache is null) Reload();
            return _cache!;
        }
    }

    public TenantInfo? GetByKey(string key) => Cache.TryGetValue(key, out var tenant) ? tenant : null;

    public IEnumerable<TenantInfo> GetAll() => Cache.Values;

    /// <summary>Reloads the cache from the control database. Call after a society is added or its status changes.</summary>
    public void Reload()
    {
        lock (_lock)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

            _cache = db.Societies.AsNoTracking().ToList().ToDictionary(
                s => s.Key,
                s => new TenantInfo(
                    s.Key,
                    s.DisplayName,
                    _connectionTemplate.Replace("{DbName}", s.DbName),
                    s.Status),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
