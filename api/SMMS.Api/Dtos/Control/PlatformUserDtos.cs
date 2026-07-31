namespace SMMS.Api.Dtos.Control;

/// <summary>A platform operator (super-admin) as shown in the console. Never includes the password hash.</summary>
public record PlatformUserDto(
    int Id,
    string Username,
    string? DisplayName,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt);

/// <summary>Payload to create a new super-admin.</summary>
public record CreatePlatformUserRequest(string Username, string? DisplayName, string Password);

/// <summary>Payload to reset a super-admin's password.</summary>
public record ResetPlatformPasswordRequest(string NewPassword);
