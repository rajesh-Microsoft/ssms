using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record ComplaintDto(
    int Id,
    string Subject,
    string Description,
    string Category,
    string Priority,
    string Status,
    int RaisedByUserId,
    string RaisedByUsername,
    string? Flat,
    string? Floor,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    string? ResolutionNotes,
    string? AssignedTo);

// Raised by any authenticated user (Member or Admin) — Status/ResolvedAt are
// always server-controlled and never accepted from the client.
public record ComplaintCreateRequest(
    [Required, MaxLength(150)] string Subject,
    [Required, MaxLength(1000)] string Description,
    [Required, MaxLength(60)] string Category,
    [MaxLength(20)] string? Priority);

// Admin-only: manages status/resolution in addition to correcting content.
public record ComplaintUpdateRequest(
    [Required, MaxLength(150)] string Subject,
    [Required, MaxLength(1000)] string Description,
    [Required, MaxLength(60)] string Category,
    [Required, MaxLength(20)] string Priority,
    [Required, MaxLength(20)] string Status,
    [MaxLength(1000)] string? ResolutionNotes,
    [MaxLength(150)] string? AssignedTo);
