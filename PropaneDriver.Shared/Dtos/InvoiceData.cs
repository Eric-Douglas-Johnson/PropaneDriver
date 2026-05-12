using System.Text.Json.Serialization;

namespace PropaneDriver.Shared.Dtos
{
    // Captures the named fields that the Azure Document Intelligence
    // prebuilt-invoice model returns when analyzing a scanned invoice.
    // Properties map 1:1 to fields in Azure's invoice schema; anything the
    // model couldn't extract stays null so callers can tell "missing" apart
    // from "explicitly empty."
    //
    // Polymorphism: container types (e.g. OcrDocumentDto) hold this via the
    // base reference, but the server may return derived subclasses such as
    // HooverInvoiceData. The "$invoiceKind" discriminator lets the wire
    // payload preserve the concrete type so the client can deserialize back
    // into the right shape and cast to access subclass-only fields.
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$invoiceKind")]
    [JsonDerivedType(typeof(InvoiceData), "generic")]
    [JsonDerivedType(typeof(HooverInvoiceData), "hoover")]
    public class InvoiceData
    {
        public string? VendorName { get; set; }
        public string? VendorAddress { get; set; }
        public string? VendorAddressRecipient { get; set; }

        public string? CustomerName { get; set; }
        public string? CustomerId { get; set; }
        public string? CustomerAddress { get; set; }
        public string? CustomerAddressRecipient { get; set; }

        public string? BillingAddress { get; set; }
        public string? BillingAddressRecipient { get; set; }
        public string? ShippingAddress { get; set; }
        public string? ShippingAddressRecipient { get; set; }
        public string? ServiceAddress { get; set; }
        public string? ServiceAddressRecipient { get; set; }
        public string? RemittanceAddress { get; set; }
        public string? RemittanceAddressRecipient { get; set; }

        public string? InvoiceId { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string? PurchaseOrder { get; set; }
        public DateTime? ServiceStartDate { get; set; }
        public DateTime? ServiceEndDate { get; set; }
        public string? PaymentTerm { get; set; }

        public decimal? SubTotal { get; set; }
        public decimal? TotalTax { get; set; }
        public decimal? InvoiceTotal { get; set; }
        public decimal? AmountDue { get; set; }
        public decimal? PreviousUnpaidBalance { get; set; }
        public string? CurrencyCode { get; set; }

        public List<InvoiceLineItem> LineItems { get; set; } = new();
    }

    public class InvoiceLineItem
    {
        public string? Description { get; set; }
        public string? ProductCode { get; set; }
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? Tax { get; set; }
        public decimal? TaxRate { get; set; }
        public decimal? Amount { get; set; }
        public DateTime? Date { get; set; }
    }
}
