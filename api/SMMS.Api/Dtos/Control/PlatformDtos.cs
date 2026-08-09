namespace SMMS.Api.Dtos.Control;

using SMMS.Api.Models.Control;

/// <summary>Payload for the society onboarding wizard.</summary>
public record OnboardSocietyRequest(
    string Key,
    string DisplayName,
    string? Plan,
    string? AdminUsername,
    string? AdminPassword,
    int FlatCount,
    DateTime? ExpiryDate);

/// <summary>Public self-service registration — creates a Pending request; no database yet.</summary>
public record RegisterSocietyRequest(
    string Key,
    string DisplayName,
    string? AdminName,
    string? AdminEmail,
    string? Phone,
    string? Address,
    string? Plan,
    int FlatCount);

/// <summary>Super-admin approval. Blank credentials => defaults (username "admin" + generated password).</summary>
public record ApproveSocietyRequest(
    string? AdminUsername,
    string? AdminPassword,
    DateTime? ExpiryDate);

/// <summary>Approval result — includes the one-time admin credentials + the society's portal URL.</summary>
public record ApproveSocietyResponse(
    SocietyDto Society,
    string AdminUsername,
    string AdminPassword,
    string PortalUrl);

/// <summary>Payload to renew/extend a society's subscription (sets a new expiry and optional plan).</summary>
public record RenewSocietyRequest(
    DateTime ExpiryDate,
    string? Plan);

/// <summary>Confirmation for permanent deletion. The key must be retyped, so the destructive
/// call cannot be made by a client that only knows the URL.</summary>
public record DeleteSocietyRequest(string ConfirmKey);

/// <summary>A society as seen by the super-admin, including a live member count.</summary>
public record SocietyDto(
    string Key,
    string DisplayName,
    string DbName,
    string Status,
    string? Plan,
    DateTime? ExpiryDate,
    int FlatCount,
    int MemberCount,
    DateTime CreatedAt,
    bool IsExpired,
    int? DaysUntilExpiry,
    string? AdminName = null,
    string? AdminEmail = null,
    string? Phone = null,
    string? Address = null,
    bool IsDemo = false);

public record PlatformDashboardDto(
    int TotalSocieties,
    int ActiveSocieties,
    int SuspendedSocieties,
    int TrialSocieties,
    int ExpiredSocieties,
    int ExpiringSoonSocieties,
    int TotalMembers,
    IReadOnlyList<SocietyDto> Societies);

public record PlatformAuditDto(
    int Id,
    string ActorUsername,
    string Action,
    string? TargetType,
    string? TargetKey,
    string? Details,
    string? ImpersonatedTenant,
    DateTime Timestamp);

public record ImpersonateResponse(string Token, string TenantKey, string AdminUsername);

/// <summary>Builds <see cref="SocietyDto"/> instances with the derived subscription fields
/// (expired flag + days-until-expiry) computed consistently across every endpoint.</summary>
public static class SocietyMapping
{
    /// <summary>A society within this many days of expiry is flagged as "expiring soon" in the console.</summary>
    public const int ExpiringSoonDays = 14;

    public static SocietyDto ToDto(Society s, int memberCount)
    {
        int? daysUntilExpiry = s.ExpiryDate is { } exp
            ? (int)Math.Ceiling((exp.Date - DateTime.UtcNow.Date).TotalDays)
            : null;

        var isExpired = string.Equals(s.Status, SocietyStatus.Expired, StringComparison.OrdinalIgnoreCase)
            || (daysUntilExpiry is < 0);

        return new SocietyDto(
            s.Key, s.DisplayName, s.DbName, s.Status, s.Plan, s.ExpiryDate,
            s.FlatCount, memberCount, s.CreatedAt, isExpired, daysUntilExpiry,
            s.AdminName, s.AdminEmail, s.Phone, s.Address, s.IsDemo);
    }
}
