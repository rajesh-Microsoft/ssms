namespace SMMS.Api.Data.Tenancy;

/// <summary>
/// Resolves the current tenant from the request subdomain (e.g. "aadya.yourapp.com" -&gt; "aadya")
/// before any DbContext is used, and stores it in the request-scoped <see cref="ITenantContext"/>.
/// For local dev/testing against a plain host with no tenant subdomain, falls back to an
/// "X-Tenant" request header. Requests with no resolvable tenant are rejected with 404 before
/// they can reach any controller or touch the database.
/// </summary>
public class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantStore tenantStore, ITenantContext tenantContext)
    {
        var host = context.Request.Host.Host; // host only, no port
        var labels = host.Split('.');
        var subdomain = labels.Length > 1 ? labels[0] : null;

        var key = subdomain is not null && !string.Equals(subdomain, "www", StringComparison.OrdinalIgnoreCase)
            ? subdomain
            : context.Request.Headers["X-Tenant"].FirstOrDefault();

        var tenant = key is not null ? tenantStore.GetByKey(key) : null;
        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { message = "Unknown or unspecified society." });
            return;
        }

        tenantContext.Current = tenant;
        await next(context);
    }
}
