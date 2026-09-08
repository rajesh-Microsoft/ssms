using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Dtos;

public record UserGroupDto(
    int Id,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    int MemberCount,
    Dictionary<string, string> Permissions);

public record UserGroupDetailDto(
    UserGroupDto Group,
    IEnumerable<UserGroupMemberDto> Members);

public record UserGroupMemberDto(int UserId, string Username, string? Name, string? Flat, string Role);

public record UserGroupUpsertRequest(
    [Required, MaxLength(60)] string Name,
    [MaxLength(300)] string? Description,
    bool IsActive,
    Dictionary<string, string>? Permissions);

public record UserGroupMembersRequest(List<int> UserIds);

/// <summary>What a user may actually do, and where it came from — so an admin can see whether an
/// account is still on an individual override rather than inheriting from its groups.</summary>
public record EffectiveAccessDto(
    int UserId,
    string Role,
    bool UsesIndividualOverride,
    IEnumerable<string> Groups,
    Dictionary<string, string> Permissions);
