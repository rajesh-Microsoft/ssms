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

    // Product-portal & marketing hosts that carry NO tenant (e.g. ssms.yuvaansoft.shop,
    // www.yuvaansoft.shop). They only serve public/anonymous endpoints such as society
    // self-registration, so requests pass through with no tenant resolved.
    private static readonly HashSet<string> PublicHostKeys =
        new(StringComparer.OrdinalIgnoreCase) { "www", "ssms" };

    public async Task InvokeAsync(HttpContext context, ITenantStore tenantStore, ITenantContext tenantContext)
    {
        var host = context.Request.Host.Host; // host only, no port
        var labels = host.Split('.');
        var subdomain = labels.Length > 1 ? labels[0] : null;

        // Public portal / marketing host: let anonymous endpoints (e.g. registration) through
        // without a tenant, instead of rejecting "ssms" as an unknown society.
        if (subdomain is not null && PublicHostKeys.Contains(subdomain))
        {
            await next(context);
            return;
        }

        var key = subdomain is not null
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

        // Pending society: registered but not yet approved/provisioned (no database). Block serving.
        if (string.Equals(tenant.Status, SocietyStatus.Pending, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "This society registration is pending approval."
            });
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

        // Subscription/licence enforcement: block once the paid term has lapsed. The background
        // SubscriptionEnforcementService flips Status to "Expired", but we also check the date
        // in real time so access is cut off immediately at expiry (before the next sweep runs).
        var expired = string.Equals(tenant.Status, SocietyStatus.Expired, StringComparison.OrdinalIgnoreCase)
            || (tenant.ExpiryDate is { } exp && exp.Date < DateTime.UtcNow.Date);
        if (expired)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "This society's subscription has expired. Please contact the platform administrator to renew."
            });
            return;
        }

        tenantContext.Current = tenant;
        await next(context);
    }
}
