using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record MemberDto(int Id, string Name, string Flat, string? Floor, string? Mobile, string? Email, string Status);

public record MemberUpsertRequest(
    [Required, MaxLength(150)] string Name,
    [Required, MaxLength(20)] string Flat,
    string? Floor,
    string? Mobile,
    string? Email,
    string Status = "Active");
