using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record UserDto(int Id, string Username, string? Name, string Role, string? Email, string? Mobile, string? Flat,
    string? Floor, string Status, IEnumerable<int> GroupIds, IEnumerable<string> GroupNames,
    string? OccupancyType);

public record UserCreateRequest(
    [Required, MaxLength(50)] string Username,
    [Required, MinLength(4)] string Password,
    [Required] string Role,
    string? Email,
    string? Mobile,
    string? Flat,
    string? Floor,
    string Status = "Active",
    bool MustChangePassword = false,
    List<int>? GroupIds = null,
    /// <summary>"Owner" or "Tenant". Decides the permission baseline, so it is admin-set only.</summary>
    string? OccupancyType = null);

public record UserUpdateRequest(
    [Required, MaxLength(50)] string Username,
    [Required] string Role,
    string? Email,
    string? Mobile,
    string? Flat,
    string? Floor,
    [Required] string Status,
    List<int>? GroupIds = null,
    string? OccupancyType = null);

public record ResetUserPasswordRequest([Required, MinLength(4)] string NewPassword);

public record AuditLogDto(int Id, DateTime Timestamp, string User, string Module, string Action, string? Details);
