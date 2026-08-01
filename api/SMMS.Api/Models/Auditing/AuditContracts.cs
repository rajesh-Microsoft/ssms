namespace SMMS.Api.Models.Auditing;

/// <summary>Standard who/when audit stamps, filled automatically by the SaveChanges interceptor.</summary>
public interface IAuditable
{
    DateTime CreatedOn { get; set; }
    string? CreatedBy { get; set; }
    DateTime? ModifiedOn { get; set; }
    string? ModifiedBy { get; set; }
}

/// <summary>Marks a row as removable without a physical delete.</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
}
