using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DevPortal.Api.Services;

public record Operator(string Username, string Name, string Role, string PasswordHash);

public record AuthOptions
{
    /// <summary>HMAC key for session tokens. Empty means authentication is not configured,
    /// and every write endpoint refuses. That default is deliberate: a deployment button
    /// guarded by nothing is worse than no button.</summary>
    public string SigningKey { get; set; } = "";
    public int TokenMinutes { get; set; } = 60;
    public List<Operator> Operators { get; set; } = new();

    /// <summary>Which tiers each role may deploy to. Enforced on the server; the UI only mirrors it.</summary>
    public Dictionary<string, string[]> Deploy { get; set; } = new();
    public string[] RollbackRoles { get; set; } = Array.Empty<string>();
}

public record Principal(string Username, string Name, string Role);

/// <summary>
/// Password hashing and signed session tokens. Entra ID replaces the login step later;
/// everything downstream only needs a Principal, so that swap stays local to this file.
/// </summary>
public class AuthService(IConfiguration config, ILogger<AuthService> log)
{
    private readonly AuthOptions _options = config.GetSection("Auth").Get<AuthOptions>() ?? new();

    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.SigningKey) &&
        _options.Operators.Any(o => !string.IsNullOrWhiteSpace(o.PasswordHash));

    public string NotConfiguredReason =>
        "Authentication is not configured, so this portal cannot deploy. Set Auth:SigningKey and at " +
        "least one operator with a password hash (dotnet run -- hash <password>).";

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    private static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        if (!int.TryParse(parts[1], out var iterations)) return false;

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public Principal? Authenticate(string username, string password)
    {
        var op = _options.Operators.FirstOrDefault(o =>
            string.Equals(o.Username, username, StringComparison.OrdinalIgnoreCase));

        // Hash even when the user does not exist, so a missing account is not faster to probe.
        var stored = op?.PasswordHash ?? "pbkdf2$210000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
        var ok = Verify(password, stored);
        if (op is null || !ok)
        {
            log.LogWarning("failed sign-in for {Username}", username);
            return null;
        }
        return new Principal(op.Username, op.Name, op.Role);
    }

    public string IssueToken(Principal principal)
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(_options.TokenMinutes).ToUnixTimeSeconds();
        var payload = JsonSerializer.Serialize(new { u = principal.Username, n = principal.Name, r = principal.Role, e = expires });
        var body = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        return $"{body}.{Sign(body)}";
    }

    public Principal? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length != 2) return null;

        var expected = Sign(parts[0]);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[1])))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])));
            var root = doc.RootElement;
            if (root.GetProperty("e").GetInt64() < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            return new Principal(root.GetProperty("u").GetString()!, root.GetProperty("n").GetString()!, root.GetProperty("r").GetString()!);
        }
        catch
        {
            return null;
        }
    }

    public bool CanDeploy(Principal principal, string tier)
        => _options.Deploy.TryGetValue(principal.Role, out var tiers) && tiers.Contains(tier);

    public bool CanRollback(Principal principal)
        => _options.RollbackRoles.Contains(principal.Role);

    public string[] AllowedTiers(Principal principal)
        => _options.Deploy.TryGetValue(principal.Role, out var tiers) ? tiers : Array.Empty<string>();

    private string Sign(string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SigningKey));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }
}
