namespace SMMS.Api.Models;

/// <summary>Billing period for a recurring collection category. Only Monthly is honoured by the
/// scheduler today; the other values are reserved so adding them later needs no schema change.</summary>
public enum BillingFrequency
{
    Monthly = 0,
    Quarterly = 1,
    HalfYearly = 2,
    Yearly = 3
}
