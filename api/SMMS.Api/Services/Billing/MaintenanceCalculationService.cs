using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Models;

namespace SMMS.Api.Services.Billing;

public record InvoiceLine(int? ComponentId, string ComponentName, string Method, decimal Amount);
public record FlatInvoice(int MemberId, string FlatNumber, IReadOnlyList<InvoiceLine> Lines, decimal Total);

/// <summary>The maintenance rule engine: for every active flat, evaluates every active component
/// (in SortOrder) via its strategy and returns a per-component line breakdown plus the total.</summary>
public class MaintenanceCalculationService(SmmsDbContext db, ChargeStrategyResolver resolver)
{
    public async Task<List<MaintenanceComponent>> LoadComponentsAsync() =>
        await db.MaintenanceComponents
            .Where(c => c.IsActive && c.CategoryType == CollectionCategoryType.Recurring)
            .Include(c => c.Rates)
            .Include(c => c.FlatOverrides)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToListAsync();

    public async Task<IReadOnlyList<FlatInvoice>> CalculateAllAsync()
    {
        var components = await LoadComponentsAsync();
        var members = await db.Members.Where(m => m.Status == "Active").ToListAsync();
        return members.Select(m => CalculateForFlat(components, ToContext(m))).ToList();
    }

    public FlatInvoice CalculateForFlat(IReadOnlyList<MaintenanceComponent> components, FlatContext flat)
    {
        var prior = new Dictionary<int, decimal>();
        var lines = new List<InvoiceLine>();

        foreach (var c in components)
        {
            // Optional component: skip flats that have not explicitly opted in.
            if (!c.ApplyToAllFlats &&
                !c.FlatOverrides.Any(o => o.MemberId == flat.MemberId && o.IsApplicable))
                continue;

            var amount = decimal.Round(
                resolver.For(c.Method).Calculate(c, flat, prior), 2, MidpointRounding.AwayFromZero);

            prior[c.Id] = amount;
            if (amount != 0m)
                lines.Add(new InvoiceLine(c.Id, c.Name, c.Method.ToString(), amount));
        }

        return new FlatInvoice(flat.MemberId, flat.FlatNumber, lines, lines.Sum(l => l.Amount));
    }

    public static FlatContext ToContext(Member m) =>
        new(m.Id, m.Flat, m.AreaSqFt, m.FlatType, m.Tower, m.Floor);
}
