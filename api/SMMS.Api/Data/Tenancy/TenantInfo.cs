namespace SMMS.Api.Data.Tenancy;

/// <summary>Identifies one society/client tenant and where its data lives.</summary>
public record TenantInfo(string Key, string DisplayName, string ConnectionString, string Status = "Active");
