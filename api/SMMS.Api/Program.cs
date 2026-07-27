using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SMMS.Api.Data;
using SMMS.Api.Data.Tenancy;
using SMMS.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Multi-tenant: each society (tenant) has its own database. The tenant is resolved per-request
// (from the subdomain, or an X-Tenant header for local dev) by TenantResolutionMiddleware,
// which populates the scoped ITenantContext that the DbContext options factory below reads.
builder.Services.AddSingleton<ITenantStore, ConfigTenantStore>();
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
builder.Services.AddScoped<AuditService>();
builder.Services.AddHttpContextAccessor();

// Maintenance-payment module (dynamic UPI QR + proof upload + admin approval).
builder.Services.AddSingleton<SMMS.Api.Services.QrService>();
builder.Services.AddSingleton<SMMS.Api.Services.Storage.IFileStorage, SMMS.Api.Services.Storage.LocalFileStorage>();
builder.Services.AddScoped<SMMS.Api.Services.Payments.IPaymentGateway, SMMS.Api.Services.Payments.ManualUpiPaymentGateway>();
builder.Services.AddScoped<SMMS.Api.Services.Payments.PaymentService>();

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
builder.Services.AddAuthorization();

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontEnd", policy =>
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

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
    if (context.User.Identity?.IsAuthenticated == true)
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
