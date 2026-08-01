using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SMMS.Api.Data;
using SMMS.Api.Data.Control;
using SMMS.Api.Data.Tenancy;
using SMMS.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Behind nginx (TLS terminator) the app receives plain HTTP; honor X-Forwarded-Proto/For so
// Request.Scheme becomes https and generated URLs/redirects are correct. nginx runs in a separate
// container, so its source IP isn't loopback — clear the default known proxy/network allowlist.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Multi-tenant: each society (tenant) has its own database. The tenant is resolved per-request
// (from the subdomain, or an X-Tenant header for local dev) by TenantResolutionMiddleware,
// which populates the scoped ITenantContext that the DbContext options factory below reads.
// Platform control plane: its own database (fixed connection string, not tenant-resolved).
builder.Services.AddDbContext<ControlDbContext>(options =>
    options.UseSqlServer(builder.Configuration["ControlPlane:ConnectionString"]
        ?? throw new InvalidOperationException("ControlPlane:ConnectionString is not configured.")));

// Tenant registry now comes from SmmsControlDb (enables runtime provisioning) instead of config.
// Registered concretely too so provisioning can call Reload() after adding/suspending a society.
builder.Services.AddSingleton<DbTenantStore>();
builder.Services.AddSingleton<ITenantStore>(sp => sp.GetRequiredService<DbTenantStore>());
builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.AddDbContext<SmmsDbContext>((sp, options) =>
{
    var tenant = sp.GetRequiredService<ITenantContext>().Current
        ?? throw new InvalidOperationException(
            "Tenant has not been resolved yet. Ensure TenantResolutionMiddleware runs (or " +
            "ITenantContext.Current is set) before the DbContext is used.");
    options.UseSqlServer(tenant.ConnectionString);
});

var jwtSettings = new JwtSettings
{
    Key = builder.Configuration["Jwt:Key"]
        ?? throw new InvalidOperationException(
            "Jwt:Key is not configured. Set it via 'dotnet user-secrets set \"Jwt:Key\" <value>' locally, " +
            "or via Azure Key Vault / App Service configuration in production."),
    Issuer = builder.Configuration["Jwt:Issuer"] ?? "SMMS.Api",
    Audience = builder.Configuration["Jwt:Audience"] ?? "SMMS.Client",
    ExpiryMinutes = builder.Configuration.GetValue("Jwt:ExpiryMinutes", 120)
};
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<PlatformTokenService>();
builder.Services.AddScoped<AuditService>();

// Platform control-plane services (super-admin surface).
builder.Services.AddScoped<SMMS.Api.Services.Control.PlatformAuditService>();
builder.Services.AddScoped<SMMS.Api.Services.Control.TenantProvisioningService>();
builder.Services.AddScoped<SMMS.Api.Services.Control.DemoDataService>();
builder.Services.AddHostedService<SMMS.Api.Services.Control.SubscriptionEnforcementService>();
builder.Services.AddHttpContextAccessor();

// Maintenance-payment module (dynamic UPI QR + proof upload + admin approval).
builder.Services.AddSingleton<SMMS.Api.Services.QrService>();
builder.Services.AddSingleton<SMMS.Api.Services.Storage.IFileStorage, SMMS.Api.Services.Storage.LocalFileStorage>();
builder.Services.AddScoped<SMMS.Api.Services.Payments.IPaymentGateway, SMMS.Api.Services.Payments.ManualUpiPaymentGateway>();
builder.Services.AddScoped<SMMS.Api.Services.Payments.PaymentService>();
builder.Services.AddScoped<SMMS.Api.Services.Payments.BankReconciliationService>();

// Maintenance billing module (manual monthly-invoice generation; scheduler added in a later phase).
builder.Services.AddScoped<SMMS.Api.Services.Billing.BillingService>();
// Maintenance rule engine: one strategy per calculation method (auto-discovered by the resolver),
// plus the data-driven calculation service used for both preview and invoice generation.
builder.Services.AddSingleton<SMMS.Api.Services.Billing.IChargeStrategy, SMMS.Api.Services.Billing.FixedAmountStrategy>();
builder.Services.AddSingleton<SMMS.Api.Services.Billing.IChargeStrategy, SMMS.Api.Services.Billing.PerSquareFootStrategy>();
builder.Services.AddSingleton<SMMS.Api.Services.Billing.IChargeStrategy, SMMS.Api.Services.Billing.PercentageStrategy>();
builder.Services.AddSingleton<SMMS.Api.Services.Billing.IChargeStrategy, SMMS.Api.Services.Billing.PerFlatTypeStrategy>();
builder.Services.AddSingleton<SMMS.Api.Services.Billing.IChargeStrategy, SMMS.Api.Services.Billing.PerSquareFootByFlatTypeStrategy>();
builder.Services.AddSingleton<SMMS.Api.Services.Billing.IChargeStrategy, SMMS.Api.Services.Billing.PerTowerStrategy>();
builder.Services.AddSingleton<SMMS.Api.Services.Billing.IChargeStrategy, SMMS.Api.Services.Billing.PerFloorStrategy>();
builder.Services.AddSingleton<SMMS.Api.Services.Billing.IChargeStrategy, SMMS.Api.Services.Billing.CustomPerFlatStrategy>();
builder.Services.AddSingleton<SMMS.Api.Services.Billing.IChargeStrategy, SMMS.Api.Services.Billing.ManualStrategy>();
builder.Services.AddSingleton<SMMS.Api.Services.Billing.ChargeStrategyResolver>();
builder.Services.AddScoped<SMMS.Api.Services.Billing.MaintenanceCalculationService>();
builder.Services.AddScoped<SMMS.Api.Services.Billing.OneTimeChargeService>();
builder.Services.AddScoped<SMMS.Api.Services.Billing.AdvanceService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key))
        };
    });
builder.Services.AddAuthorization(options =>
{
    // Super-admin (platform control plane) surface. Granted by a PlatformTokenService JWT
    // carrying "platform_role=SuperAdmin" and no tenant claim.
    options.AddPolicy("SuperAdmin", policy => policy.RequireClaim("platform_role", "SuperAdmin"));
});

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontEnd", policy =>
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// Ensure the control-plane database exists and has its first super admin + society records,
// BEFORE the per-tenant migration loop below reads the society list from it.
using (var controlScope = app.Services.CreateScope())
{
    const int maxRetries = 10;
    for (int retry = 1; retry <= maxRetries; retry++)
    {
        try
        {
            Console.WriteLine($"[control] Connecting to SmmsControlDb (attempt {retry}/{maxRetries})...");
            var controlDb = controlScope.ServiceProvider.GetRequiredService<ControlDbContext>();
            controlDb.Database.Migrate();
            ControlDbSeeder.Seed(controlDb, builder.Configuration);
            Console.WriteLine("[control] Control plane database is ready.");
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[control] Connection failed: {ex.Message}");
            if (retry == maxRetries) throw;
            Thread.Sleep(TimeSpan.FromSeconds(5));
        }
    }
}

// Apply pending EF Core migrations and seed baseline data for every configured tenant on startup.
// A fresh scope (and therefore a fresh DbContext/connection) is created per attempt so a failed
// connection to one tenant's database can't leave a broken DbContext behind for the retry.
using (var rootScope = app.Services.CreateScope())
{
    var tenantStore = rootScope.ServiceProvider.GetRequiredService<ITenantStore>();

    foreach (var tenant in tenantStore.GetAll())
    {
        const int maxRetries = 10;

        for (int retry = 1; retry <= maxRetries; retry++)
        {
            try
            {
                Console.WriteLine($"[{tenant.Key}] Connecting to SQL Server (attempt {retry}/{maxRetries})...");

                using var tenantScope = app.Services.CreateScope();
                tenantScope.ServiceProvider.GetRequiredService<ITenantContext>().Current = tenant;
                var db = tenantScope.ServiceProvider.GetRequiredService<SmmsDbContext>();

                db.Database.Migrate();
                DbSeeder.Seed(db, tenant.DisplayName);

                Console.WriteLine($"[{tenant.Key}] Database is ready.");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{tenant.Key}] Connection failed: {ex.Message}");

                if (retry == maxRetries)
                    throw;

                Thread.Sleep(TimeSpan.FromSeconds(5));
            }
        }
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Must run first so downstream middleware sees the client's real scheme/IP from nginx.
app.UseForwardedHeaders();

app.UseHttpsRedirection();

app.UseCors("FrontEnd");

// Must run before authentication/authorization and before any controller touches the DbContext.
app.UseMiddleware<TenantResolutionMiddleware>();

app.UseAuthentication();

// Defense in depth: a JWT is only valid for the tenant it was issued by. Without this, a token
// leaked/replayed against a different society's subdomain would still authenticate (just against
// the wrong tenant's database), which is a broken-access-control risk in a shared deployment.
app.Use(async (context, next) =>
{
    // The platform control-plane surface has no resolved tenant and uses super-admin tokens
    // (which carry no tenant claim), so the tenant-match check does not apply there.
    var isControlPlane = context.Items.ContainsKey("IsControlPlane");

    if (!isControlPlane && context.User.Identity?.IsAuthenticated == true)
    {
        var tokenTenant = context.User.FindFirst("tenant")?.Value;
        var resolvedTenant = context.RequestServices.GetRequiredService<ITenantContext>().Current?.Key;

        if (!string.Equals(tokenTenant, resolvedTenant, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "Token is not valid for this society." });
            return;
        }
    }

    await next(context);
});

app.UseAuthorization();

app.MapControllers();

app.Run();
