using ClosedXML.Excel;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Services
{
    // Writes the contents of a HooverInvoiceData into a user-supplied .xlsx
    // workbook. The chosen layout is one row per equipment piece, with the
    // invoice header fields (vendor, customer, totals, etc.) repeated on
    // each row — so downstream pivots / filters in Excel can slice across
    // both axes without further reshaping. A header row is added to the
    // first sheet if it's currently empty; otherwise new rows are appended
    // below the last used row.
    public static class HooverInvoiceExcelExporterService
    {
        private static readonly string[] HeaderColumnTitles =
        {
            "Scan Timestamp UTC",
            "Invoice ID",
            "Vendor Name",
            "Customer Name",
            "Invoice Date",
            "Due Date",
            "Subtotal",
            "Total Tax",
            "Invoice Total",
            "Amount Due",
            "Currency Code",
            "Total Fuel Pumped",
            "Equipment ID",
            "Equipment Fuel Quantity"
        };

        // Reads the supplied workbook, appends the Hoover scan to the first
        // worksheet, and returns the modified workbook as a byte array
        // (ready to ship back to the client as a download).
        public static byte[] AppendScan(
            Stream existingWorkbookStream,
            HooverInvoiceData hooverInvoiceData,
            DateTime scanTimestampUtc)
        {
            using var workbook = new XLWorkbook(existingWorkbookStream);
            var firstWorksheet = workbook.Worksheets.FirstOrDefault()
                ?? workbook.Worksheets.Add("Hoover Invoices");

            var lastUsedRow = firstWorksheet.LastRowUsed();
            var nextRowNumber = (lastUsedRow?.RowNumber() ?? 0) + 1;

            // Sheet was empty — drop in the header row first so the columns
            // are labeled.
            if (lastUsedRow is null)
            {
                for (var columnIndex = 0; columnIndex < HeaderColumnTitles.Length; columnIndex++)
                {
                    var headerCell = firstWorksheet.Cell(nextRowNumber, columnIndex + 1);
                    headerCell.Value = HeaderColumnTitles[columnIndex];
                    headerCell.Style.Font.Bold = true;
                }
                nextRowNumber++;
            }

            // One row per equipment piece. If the parser surfaced no pieces
            // at all, we still write a single row so the invoice header
            // and total fuel pumped don't get silently dropped.
            if (hooverInvoiceData.EquipmentPieces.Count == 0)
            {
                WriteScanRow(firstWorksheet, nextRowNumber, scanTimestampUtc, hooverInvoiceData, equipmentPiece: null);
            }
            else
            {
                foreach (var equipmentPiece in hooverInvoiceData.EquipmentPieces)
                {
                    WriteScanRow(firstWorksheet, nextRowNumber, scanTimestampUtc, hooverInvoiceData, equipmentPiece);
                    nextRowNumber++;
                }
            }

            using var outputStream = new MemoryStream();
            workbook.SaveAs(outputStream);
            return outputStream.ToArray();
        }

        private static void WriteScanRow(
            IXLWorksheet worksheet,
            int rowNumber,
            DateTime scanTimestampUtc,
            HooverInvoiceData hooverInvoiceData,
            EquipmentPiece? equipmentPiece)
        {
            worksheet.Cell(rowNumber, 1).Value = scanTimestampUtc;
            worksheet.Cell(rowNumber, 2).Value = hooverInvoiceData.InvoiceId ?? string.Empty;
            worksheet.Cell(rowNumber, 3).Value = hooverInvoiceData.VendorName ?? string.Empty;
            worksheet.Cell(rowNumber, 4).Value = hooverInvoiceData.CustomerName ?? string.Empty;

            if (hooverInvoiceData.InvoiceDate.HasValue)
                worksheet.Cell(rowNumber, 5).Value = hooverInvoiceData.InvoiceDate.Value;
            if (hooverInvoiceData.DueDate.HasValue)
                worksheet.Cell(rowNumber, 6).Value = hooverInvoiceData.DueDate.Value;

            if (hooverInvoiceData.SubTotal.HasValue)
                worksheet.Cell(rowNumber, 7).Value = hooverInvoiceData.SubTotal.Value;
            if (hooverInvoiceData.TotalTax.HasValue)
                worksheet.Cell(rowNumber, 8).Value = hooverInvoiceData.TotalTax.Value;
            if (hooverInvoiceData.InvoiceTotal.HasValue)
                worksheet.Cell(rowNumber, 9).Value = hooverInvoiceData.InvoiceTotal.Value;
            if (hooverInvoiceData.AmountDue.HasValue)
                worksheet.Cell(rowNumber, 10).Value = hooverInvoiceData.AmountDue.Value;

            worksheet.Cell(rowNumber, 11).Value = hooverInvoiceData.CurrencyCode ?? string.Empty;

            if (hooverInvoiceData.TotalFuelPumped.HasValue)
                worksheet.Cell(rowNumber, 12).Value = hooverInvoiceData.TotalFuelPumped.Value;

            if (equipmentPiece is not null)
            {
                worksheet.Cell(rowNumber, 13).Value = equipmentPiece.Id;
                worksheet.Cell(rowNumber, 14).Value = equipmentPiece.FuelQuantity;
            }
        }
    }
}
