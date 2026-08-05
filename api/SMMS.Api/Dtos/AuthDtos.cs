using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record LoginRequest(
    [Required] string Username,
    [Required] string Password);

public record LoginResponse(
    string Token,
    int UserId,
    string Username,
    string Role,
    Dictionary<string, string> Permissions,
    bool MustChangePassword);

public record SignupRequest(
    [Required, MaxLength(150)] string Name,
    [Required, MaxLength(20)] string Flat,
    string? Floor,
    string? Mobile,
    string? Email,
    [Required, MaxLength(50)] string Username,
    [Required, MinLength(4)] string Password,
    [Required] string SecurityQuestion,
    [Required] string SecurityAnswer);

public record SecurityQuestionResponse(string SecurityQuestion);

/// <summary>A flat from the society roster, offered at sign-up. <paramref name="Taken"/> flats are
/// shown but not selectable, so a resident can see their flat is already claimed.</summary>
public record FlatOptionDto(string Flat, string? Floor, bool Taken);

public record ResetPasswordRequest(
    [Required] string Username,
    [Required] string SecurityAnswer,
    [Required, MinLength(4)] string NewPassword);
