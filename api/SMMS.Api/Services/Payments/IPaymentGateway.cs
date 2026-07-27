using SMMS.Api.Models;

namespace SMMS.Api.Services.Payments;

/// <summary>
/// Strategy abstraction over a payment collection method. The current system uses
/// <see cref="ManualUpiPaymentGateway"/> (dynamic UPI QR + resident-uploaded proof + admin
/// approval). Automated gateways (Razorpay, Cashfree, PhonePe Business, ICICI Smart Collect)
/// can be added later as new implementations that create a payment intent and reconcile via
/// webhook — writing to the same PaymentProof table (GatewayName/GatewayReference) with no
/// database schema change.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Stable key persisted on records created by this gateway, e.g. "ManualUPI".</summary>
    string Name { get; }

    /// <summary>Produces the payer-facing instruction for settling a charge — for the manual UPI
    /// gateway this is a UPI deep-link URI to be rendered as a QR code.</summary>
    PaymentInstruction CreateInstruction(SocietySettings settings, Collection charge, Member member);
}

/// <summary>What the resident must act on to pay (a UPI URI today; a hosted checkout URL later).</summary>
public record PaymentInstruction(string Kind, string Payload, string InvoiceNumber, string Note, decimal Amount);
