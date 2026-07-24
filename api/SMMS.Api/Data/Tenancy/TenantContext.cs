namespace SMMS.Api.Data.Tenancy;

/// <summary>Scoped per-request holder for the tenant resolved by <see cref="TenantResolutionMiddleware"/>.</summary>
public interface ITenantContext
{
    TenantInfo? Current { get; set; }
}

public class TenantContext : ITenantContext
{
    public TenantInfo? Current { get; set; }
}
