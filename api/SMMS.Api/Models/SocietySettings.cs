using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SMMS.Api.Models.Auditing;

namespace SMMS.Api.Models;

/// <summary>Single-row table holding society-wide configuration.</summary>
public class SocietySettings : IAuditable
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string SocietyName { get; set; } = "NLC Aadya";

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(100)]
    public string? RegistrationNumber { get; set; }

    [MaxLength(20)]
    public string? Gst { get; set; }

    [MaxLength(20)]
    public string? Pan { get; set; }

    /// <summary>Society logo as a data URI (e.g. "data:image/png;base64,...").</summary>
    public string? LogoBase64 { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal MaintenanceAmt { get; set; } = 2000;

    /// <summary>Default calculation method for the primary "Maintenance Charges" component.</summary>
    [MaxLength(30)]
    public string MaintenanceCalcMethod { get; set; } = nameof(Models.CalculationMethod.FixedAmount);

    /// <summary>Day of the month maintenance is due, 1-31.</summary>
    public int DueDay { get; set; } = 5;

    [Column(TypeName = "decimal(12,2)")]
    public decimal LateFee { get; set; } = 100;

    public int GraceDays { get; set; } = 5;

    /// <summary>Day of month (1-31) the monthly maintenance invoice is generated. Configurable
    /// so societies that bill on the 25th of the prior month vs the 1st vs the 5th all work
    /// without code changes.</summary>
    public int BillingDay { get; set; } = 1;

    /// <summary>When true, the background scheduler auto-generates monthly invoices on BillingDay.
    /// When false, an admin must trigger generation manually.</summary>
    public bool AutoGenerateInvoices { get; set; } = false;

    /// <summary>e.g. "2026-27".</summary>
    [MaxLength(20)]
    public string? FinancialYear { get; set; }

    /// <summary>Comma-separated floor labels, e.g. "1,2,3,4,5".</summary>
    [MaxLength(500)]
    public string Floors { get; set; } = "1,2,3,4,5";

    /// <summary>Comma-separated tower/block labels, e.g. "A,B,C". Sources the Tower dropdowns
    /// and the PerTower component rate keys.</summary>
    [MaxLength(500)]
    public string Towers { get; set; } = string.Empty;

    /// <summary>Comma-separated flat-type labels, e.g. "1 BHK,2 BHK,3 BHK". Sources the Flat Type
    /// dropdown on the member form and the PerFlatType / PerSquareFootByFlatType rate keys.</summary>
    [MaxLength(500)]
    public string FlatTypes { get; set; } = "1 BHK,1.5 BHK,2 BHK,2.5 BHK,3 BHK";

    /// <summary>Comma-separated expense category names.</summary>
    [MaxLength(1000)]
    public string Categories { get; set; } = "Security,Housekeeping,Electricity,Water,Repairs,Lift Maintenance,Gardening,Festival,CCTV,Miscellaneous";

    /// <summary>Comma-separated non-member income category names (ad/hoarding, shop rent, etc.).</summary>
    [MaxLength(1000)]
    public string IncomeCategories { get; set; } = "Advertisement,Shop Rent,Tower Rent,Hall Rental,Interest,Scrap Sale,Donation,Miscellaneous";

    [MaxLength(20)]
    public string Theme { get; set; } = "light";

    [MaxLength(20)]
    public string PrimaryColor { get; set; } = "#6c63ff";

    [MaxLength(20)]
    public string? SecondaryColor { get; set; }

    [MaxLength(150)]
    public string? ApplicationTitle { get; set; }

    // ── UPI / bank collection settings (used to build dynamic payment QR codes) ──

    /// <summary>When false, members only ever see the manual UPI/QR flow — the card gateway is
    /// hidden and its endpoints refuse to open an order. Opt in, so a society never starts
    /// taking gateway payments just because the platform has credentials configured.</summary>
    public bool OnlinePaymentsEnabled { get; set; } = false;

    /// <summary>Society's UPI VPA, e.g. "society@upi". When set, members see a dynamic pay QR.</summary>
    [MaxLength(100)]
    public string? UpiId { get; set; }

    /// <summary>Payee name shown in the UPI app (defaults to SocietyName when empty).</summary>
    [MaxLength(100)]
    public string? UpiPayeeName { get; set; }

    [MaxLength(100)]
    public string? BankName { get; set; }

    [MaxLength(150)]
    public string? BankAccountName { get; set; }

    [MaxLength(30)]
    public string? BankAccountNumber { get; set; }

    [MaxLength(20)]
    public string? BankIfsc { get; set; }

    // ── Audit (IAuditable) ──
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
}
