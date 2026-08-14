using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record UserDto(int Id, string Username, string? Name, string Role, string? Email, string? Mobile, string? Flat,
    string? Floor, string Status, Dictionary<string, string> Permissions);

public record UserCreateRequest(
    [Required, MaxLength(50)] string Username,
    [Required, MinLength(4)] string Password,
    [Required] string Role,
    string? Email,
    string? Mobile,
    string? Flat,
    string? Floor,
    string Status = "Active",
    Dictionary<string, string>? Permissions = null,
    bool MustChangePassword = false);

public record UserUpdateRequest(
    [Required, MaxLength(50)] string Username,
    [Required] string Role,
    string? Email,
    string? Mobile,
    string? Flat,
    string? Floor,
    [Required] string Status,
    Dictionary<string, string>? Permissions = null);

public record ResetUserPasswordRequest([Required, MinLength(4)] string NewPassword);

public record AuditLogDto(int Id, DateTime Timestamp, string User, string Module, string Action, string? Details);
