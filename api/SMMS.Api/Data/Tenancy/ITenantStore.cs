namespace SMMS.Api.Data.Tenancy;

/// <summary>Resolves tenant metadata (connection string, display name) by tenant key.</summary>
public interface ITenantStore
{
    TenantInfo? GetByKey(string key);

    IEnumerable<TenantInfo> GetAll();
}
