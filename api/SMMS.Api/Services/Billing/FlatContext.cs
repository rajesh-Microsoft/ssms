namespace SMMS.Api.Services.Billing;

/// <summary>Immutable flat attributes fed to a calculation strategy.</summary>
public record FlatContext(int MemberId, string FlatNumber, decimal AreaSqFt,
    string? FlatType, string? Tower, string? Floor);
