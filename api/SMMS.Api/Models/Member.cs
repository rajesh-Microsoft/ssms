using System.ComponentModel.DataAnnotations;

namespace SMMS.Api.Models;

public class Member
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Flat { get; set; } = string.Empty;

    [MaxLength(10)]
    public string? Floor { get; set; }

    [MaxLength(20)]
    public string? Mobile { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    /// <summary>"Active" or "Inactive".</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "Active";

    public ICollection<Collection> Collections { get; set; } = new List<Collection>();
}
