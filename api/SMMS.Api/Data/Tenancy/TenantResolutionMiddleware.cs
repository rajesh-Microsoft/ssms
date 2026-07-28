namespace SMMS.Api.Data.Tenancy;

using SMMS.Api.Models.Control;

/// <summary>
/// Resolves the current tenant from the request subdomain (e.g. "aadya.yourapp.com" -&gt; "aadya")
/// before any DbContext is used, and stores it in the request-scoped <see cref="ITenantContext"/>.
/// For local dev/testing against a plain host with no tenant subdomain, falls back to an
/// "X-Tenant" request header. Requests with no resolvable tenant are rejected with 404 before
/// they can reach any controller or touch the database.
/// The reserved "admin" key routes to the platform control plane (super-admin surface): no tenant
/// is resolved, and <c>HttpContext.Items["IsControlPlane"]</c> is flagged so the cross-tenant guard
/// skips its tenant-claim check for super-admin tokens (which legitimately have no tenant claim).
/// </summary>
public class TenantResolutionMiddleware(RequestDelegate next)
{
    private const string ControlPlaneKey = "admin";

    public async Task InvokeAsync(HttpContext context, ITenantStore tenantStore, ITenantContext tenantContext)
    {
        var host = context.Request.Host.Host; // host only, no port
        var labels = host.Split('.');
        var subdomain = labels.Length > 1 ? labels[0] : null;

        var key = subdomain is not null && !string.Equals(subdomain, "www", StringComparison.OrdinalIgnoreCase)
            ? subdomain
            : context.Request.Headers["X-Tenant"].FirstOrDefault();

        // Platform control plane (super-admin) surface: no tenant, served by platform controllers.
        if (string.Equals(key, ControlPlaneKey, StringComparison.OrdinalIgnoreCase))
        {
            context.Items["IsControlPlane"] = true;
            await next(context);
            return;
        }

        var tenant = key is not null ? tenantStore.GetByKey(key) : null;
        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { message = "Unknown or unspecified society." });
            return;
        }

        // Suspended society: resolvable but blocked until the platform reactivates it.
        if (string.Equals(tenant.Status, SocietyStatus.Suspended, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "This society is currently suspended. Please contact the platform administrator."
            });
            return;
        }

        tenantContext.Current = tenant;
        await next(context);
    }
}
