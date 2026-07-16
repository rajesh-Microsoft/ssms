using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record UserDto(int Id, string Username, string Role, string? Email, string Status);

public record UserCreateRequest(
    [Required, MaxLength(50)] string Username,
    [Required, MinLength(4)] string Password,
    [Required] string Role,
    string? Email,
    string Status = "Active");

public record ResetUserPasswordRequest([Required, MinLength(4)] string NewPassword);

public record AuditLogDto(int Id, DateTime Timestamp, string User, string Module, string Action, string? Details);
