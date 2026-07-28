namespace SMMS.Api.Dtos.Control;

/// <summary>Payload for the society onboarding wizard.</summary>
public record OnboardSocietyRequest(
    string Key,
    string DisplayName,
    string? Plan,
    string? AdminUsername,
    string? AdminPassword,
    int FlatCount,
    DateTime? ExpiryDate);

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
    DateTime CreatedAt);

public record PlatformDashboardDto(
    int TotalSocieties,
    int ActiveSocieties,
    int SuspendedSocieties,
    int TrialSocieties,
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
