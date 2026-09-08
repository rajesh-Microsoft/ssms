using System.ComponentModel.DataAnnotations;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

/// <summary>
/// A named bundle of module permissions — Treasurer, Chairman, Festival Committee. Permissions are
/// configured once here and inherited by every member, so a committee handover is a membership
/// change rather than an edit to each person's account.
/// </summary>
public class UserGroup : IAuditable
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; set; }

    /// <summary>Same JSON shape as <see cref="User.Permissions"/>, so both go through
    /// PermissionHelper and there is only ever one permission format to reason about.</summary>
    public string? Permissions { get; set; }

    /// <summary>Shipped with the product. Cannot be deleted or renamed, because societies rely on
    /// these names and the seeder would recreate them anyway.</summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;

    public List<UserGroupMember> Members { get; set; } = [];

    // ── Audit (IAuditable) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}

/// <summary>Membership of a user in a group. One person keeps one login however many positions
/// they hold, so the audit trail always names the same identity.</summary>
public class UserGroupMember
{
    public int UserGroupId { get; set; }
    public UserGroup? UserGroup { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public DateTime AddedOn { get; set; }
    public string? AddedBy { get; set; }
}
