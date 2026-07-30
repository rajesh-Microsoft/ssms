namespace SMMS.Api.Models;

/// <summary>Classifies a collection category. Recurring categories are auto-billed each period;
/// OneTime and Income are handled by dedicated flows (generated once / external income).</summary>
public enum CollectionCategoryType
{
    Recurring = 0,
    OneTime = 1,
    Income = 2
}
