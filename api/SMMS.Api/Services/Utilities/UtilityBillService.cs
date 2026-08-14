using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Models;

namespace SMMS.Api.Services.Utilities;

public record UtilityFetchOutcome(bool BillAvailable, bool Created, UtilityBill? Bill, string Message);

public class UtilityBillService(
    SmmsDbContext db,
    IUtilityProviderResolver resolver,
    ILogger<UtilityBillService> logger)
{
    public async Task<UtilityFetchOutcome> FetchAsync(int connectionId, CancellationToken cancellationToken)
    {
        var connection = await db.UtilityConnections.Include(c => c.Provider)
            .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken)
            ?? throw new KeyNotFoundException("Utility connection was not found.");

        if (connection.Status != "Active")
            return new(false, false, null, "Utility connection is inactive.");

        try
        {
            var result = await resolver.Resolve(connection.Provider!.Code).FetchBillAsync(connection, cancellationToken);
            connection.LastFetchedOn = DateTime.UtcNow;
            connection.LastFetchError = null;
            if (result is null)
            {
                await db.SaveChangesAsync(cancellationToken);
                return new(false, false, null, "No bill available.");
            }

            var bill = await db.UtilityBills.FirstOrDefaultAsync(
                b => b.UtilityConnectionId == connection.Id && b.BillingMonth == result.BillingMonth,
                cancellationToken);
            var created = bill is null;
            if (created)
            {
                bill = new UtilityBill { UtilityConnectionId = connection.Id, BillingMonth = result.BillingMonth };
                db.UtilityBills.Add(bill);
            }

            bill!.BillNumber = result.BillNumber;
            bill.BillDate = result.BillDate;
            bill.DueDate = result.DueDate;
            bill.BillAmount = result.BillAmount;
            bill.UnitsConsumed = result.UnitsConsumed;
            bill.Arrears = result.Arrears;
            bill.ConsumerName = result.ConsumerName;
            bill.Status = result.Status;
            bill.RawHtml = result.RawHtml;
            bill.FetchedOn = DateTime.UtcNow;
            connection.ServiceNumber ??= result.ServiceNumber;

            await db.SaveChangesAsync(cancellationToken);
            if (created)
            {
                db.UtilityNotifications.Add(new UtilityNotification
                {
                    UtilityBillId = bill.Id,
                    Title = $"{connection.Provider.Category} Bill Generated",
                    Message = $"{connection.Provider.ProviderName} bill of {bill.BillAmount:0.00} is due {bill.DueDate:dd MMM yyyy} for consumer {connection.ConsumerNumber}."
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            return new(true, created, bill, created ? "New utility bill fetched." : "Utility bill refreshed.");
        }
        catch (Exception ex)
        {
            connection.LastFetchedOn = DateTime.UtcNow;
            connection.LastFetchError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError(ex, "Utility fetch failed ConnectionId={ConnectionId} Provider={Provider} Consumer={ConsumerNumber}",
                connection.Id, connection.Provider?.Code, connection.ConsumerNumber);
            throw;
        }
    }
}