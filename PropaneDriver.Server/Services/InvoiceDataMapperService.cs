using Azure.AI.DocumentIntelligence;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Services
{
    // Translates the Azure Document Intelligence prebuilt-invoice schema
    // into our own InvoiceData DTO. Azure surfaces extracted fields via
    // AnalyzedDocument.Fields keyed by name (e.g. "VendorName",
    // "InvoiceTotal", "Items"); we pick the ones we care about and coerce
    // each into a typed property on InvoiceData. Anything the model didn't
    // return — or returned with an unusable type — stays null.
    public static class InvoiceDataMapperService
    {
        public static InvoiceData? FromAnalyzeResult(AnalyzeResult analyzeResult)
        {
            var firstAnalyzedDocument = analyzeResult.Documents?.FirstOrDefault();
            var documentFields = firstAnalyzedDocument?.Fields;
            if (documentFields is null || documentFields.Count == 0)
                return null;

            return new InvoiceData
            {
                VendorName = ReadString(documentFields, "VendorName"),
                VendorAddress = ReadAddress(documentFields, "VendorAddress"),
                VendorAddressRecipient = ReadString(documentFields, "VendorAddressRecipient"),

                CustomerName = ReadString(documentFields, "CustomerName"),
                CustomerId = ReadString(documentFields, "CustomerId"),
                CustomerAddress = ReadAddress(documentFields, "CustomerAddress"),
                CustomerAddressRecipient = ReadString(documentFields, "CustomerAddressRecipient"),

                BillingAddress = ReadAddress(documentFields, "BillingAddress"),
                BillingAddressRecipient = ReadString(documentFields, "BillingAddressRecipient"),
                ShippingAddress = ReadAddress(documentFields, "ShippingAddress"),
                ShippingAddressRecipient = ReadString(documentFields, "ShippingAddressRecipient"),
                ServiceAddress = ReadAddress(documentFields, "ServiceAddress"),
                ServiceAddressRecipient = ReadString(documentFields, "ServiceAddressRecipient"),
                RemittanceAddress = ReadAddress(documentFields, "RemittanceAddress"),
                RemittanceAddressRecipient = ReadString(documentFields, "RemittanceAddressRecipient"),

                InvoiceId = ReadString(documentFields, "InvoiceId"),
                InvoiceDate = ReadDate(documentFields, "InvoiceDate"),
                DueDate = ReadDate(documentFields, "DueDate"),
                PurchaseOrder = ReadString(documentFields, "PurchaseOrder"),
                ServiceStartDate = ReadDate(documentFields, "ServiceStartDate"),
                ServiceEndDate = ReadDate(documentFields, "ServiceEndDate"),
                PaymentTerm = ReadString(documentFields, "PaymentTerm"),

                SubTotal = ReadCurrencyAmount(documentFields, "SubTotal"),
                TotalTax = ReadCurrencyAmount(documentFields, "TotalTax"),
                InvoiceTotal = ReadCurrencyAmount(documentFields, "InvoiceTotal"),
                AmountDue = ReadCurrencyAmount(documentFields, "AmountDue"),
                PreviousUnpaidBalance = ReadCurrencyAmount(documentFields, "PreviousUnpaidBalance"),
                // Currency code lives inside the individual currency-valued
                // fields. The header doesn't have a standalone "CurrencyCode"
                // field, so we look at the totals in priority order and lift
                // the first one we find.
                CurrencyCode = ReadCurrencyCode(documentFields, "InvoiceTotal")
                            ?? ReadCurrencyCode(documentFields, "AmountDue")
                            ?? ReadCurrencyCode(documentFields, "SubTotal"),

                LineItems = ReadLineItems(documentFields)
            };
        }

        private static List<InvoiceLineItem> ReadLineItems(IReadOnlyDictionary<string, DocumentField> documentFields)
        {
            var lineItems = new List<InvoiceLineItem>();
            if (!documentFields.TryGetValue("Items", out var itemsField)
                || itemsField.ValueList is null)
                return lineItems;

            foreach (var itemField in itemsField.ValueList)
            {
                var itemSubFields = itemField.ValueDictionary;
                if (itemSubFields is null) continue;

                lineItems.Add(new InvoiceLineItem
                {
                    Description = ReadString(itemSubFields, "Description"),
                    ProductCode = ReadString(itemSubFields, "ProductCode"),
                    Quantity = ReadNumber(itemSubFields, "Quantity"),
                    Unit = ReadString(itemSubFields, "Unit"),
                    UnitPrice = ReadCurrencyAmount(itemSubFields, "UnitPrice"),
                    Tax = ReadCurrencyAmount(itemSubFields, "Tax"),
                    TaxRate = ReadNumber(itemSubFields, "TaxRate"),
                    Amount = ReadCurrencyAmount(itemSubFields, "Amount"),
                    Date = ReadDate(itemSubFields, "Date")
                });
            }

            return lineItems;
        }

        private static string? ReadString(IReadOnlyDictionary<string, DocumentField> documentFields, string fieldKey)
        {
            if (!documentFields.TryGetValue(fieldKey, out var field)) return null;
            if (!string.IsNullOrWhiteSpace(field.ValueString)) return field.ValueString;
            return string.IsNullOrWhiteSpace(field.Content) ? null : field.Content;
        }

        private static DateTime? ReadDate(IReadOnlyDictionary<string, DocumentField> documentFields, string fieldKey) =>
            documentFields.TryGetValue(fieldKey, out var field) && field.ValueDate.HasValue
                ? field.ValueDate.Value.UtcDateTime
                : null;

        private static decimal? ReadCurrencyAmount(IReadOnlyDictionary<string, DocumentField> documentFields, string fieldKey)
        {
            if (!documentFields.TryGetValue(fieldKey, out var field)) return null;
            if (field.ValueCurrency is not null) return (decimal)field.ValueCurrency.Amount;
            if (field.ValueDouble.HasValue) return (decimal)field.ValueDouble.Value;
            if (field.ValueInt64.HasValue) return field.ValueInt64.Value;
            return null;
        }

        private static decimal? ReadNumber(IReadOnlyDictionary<string, DocumentField> documentFields, string fieldKey)
        {
            if (!documentFields.TryGetValue(fieldKey, out var field)) return null;
            if (field.ValueDouble.HasValue) return (decimal)field.ValueDouble.Value;
            if (field.ValueInt64.HasValue) return field.ValueInt64.Value;
            return null;
        }

        private static string? ReadCurrencyCode(IReadOnlyDictionary<string, DocumentField> documentFields, string fieldKey) =>
            documentFields.TryGetValue(fieldKey, out var field) && field.ValueCurrency is not null
                ? field.ValueCurrency.CurrencyCode
                : null;

        // Flattens Azure's structured AddressValue into a single human-readable
        // line. Falls back to the raw OCR Content if the model didn't parse a
        // structured address out of the field (e.g. handwritten or unusual
        // layouts).
        private static string? ReadAddress(IReadOnlyDictionary<string, DocumentField> documentFields, string fieldKey)
        {
            if (!documentFields.TryGetValue(fieldKey, out var field)) return null;
            var addressValue = field.ValueAddress;
            if (addressValue is null)
            {
                if (!string.IsNullOrWhiteSpace(field.ValueString)) return field.ValueString;
                return string.IsNullOrWhiteSpace(field.Content) ? null : field.Content;
            }

            var streetPortion = !string.IsNullOrWhiteSpace(addressValue.StreetAddress)
                ? addressValue.StreetAddress
                : JoinNonEmpty(" ", addressValue.HouseNumber, addressValue.Road, addressValue.Unit);

            var stateAndPostal = JoinNonEmpty(" ", addressValue.State, addressValue.PostalCode);
            var cityStatePortion = JoinNonEmpty(", ", addressValue.City, stateAndPostal);

            var combined = JoinNonEmpty(", ", streetPortion, cityStatePortion, addressValue.CountryRegion);
            return string.IsNullOrWhiteSpace(combined) ? null : combined;
        }

        private static string JoinNonEmpty(string separator, params string?[] parts) =>
            string.Join(separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
