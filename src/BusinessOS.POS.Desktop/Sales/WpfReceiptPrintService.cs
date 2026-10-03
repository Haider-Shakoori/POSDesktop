using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using BusinessOS.POS.Application.Abstractions.Sales;

namespace BusinessOS.POS.Desktop.Sales;

public sealed class WpfReceiptPrintService : IReceiptPrintService
{
    public string FormatReceipt(SaleReceiptData receipt)
    {
        var sale = receipt.Sale;
        var builder = new StringBuilder();
        builder.AppendLine(receipt.BusinessName);
        builder.AppendLine(receipt.BusinessSubtitle);
        builder.AppendLine(new string('-', 42));
        builder.AppendLine("Receipt: " + sale.Number);
        builder.AppendLine("Date: " + sale.SoldAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        builder.AppendLine("Cashier: " + sale.CashierName);
        builder.AppendLine("Customer: " + sale.CustomerName);
        builder.AppendLine(new string('-', 42));

        foreach (var item in sale.Items)
        {
            builder.AppendLine(item.Product);
            builder.Append("  ")
                .Append(item.Quantity.ToString("0.######", CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(item.Unit)
                .Append(" × ")
                .Append(item.UnitPrice.ToString("N2", CultureInfo.InvariantCulture))
                .Append(" = ")
                .Append(item.LineSubtotal.ToString("N2", CultureInfo.InvariantCulture))
                .AppendLine();

            var discount = item.LineDiscount + item.SaleDiscount;
            if (discount > 0m)
            {
                builder.AppendLine("  Discount: -" + discount.ToString("N2", CultureInfo.InvariantCulture));
            }
        }

        builder.AppendLine(new string('-', 42));
        builder.AppendLine("Subtotal: " + receipt.CurrencyCode + " " + sale.Subtotal.ToString("N2", CultureInfo.InvariantCulture));
        if (sale.LineDiscountTotal > 0m)
            builder.AppendLine("Line discount: -" + receipt.CurrencyCode + " " + sale.LineDiscountTotal.ToString("N2", CultureInfo.InvariantCulture));
        if (sale.SaleDiscountAmount > 0m)
            builder.AppendLine("Sale discount: -" + receipt.CurrencyCode + " " + sale.SaleDiscountAmount.ToString("N2", CultureInfo.InvariantCulture));
        builder.AppendLine("TOTAL: " + receipt.CurrencyCode + " " + sale.NetTotal.ToString("N2", CultureInfo.InvariantCulture));
        builder.AppendLine(new string('-', 42));
        builder.AppendLine("Payments:");
        foreach (var payment in sale.Payments)
        {
            builder.Append("  ").Append(payment.MethodName)
                .Append(": ").Append(receipt.CurrencyCode).Append(' ')
                .Append(payment.AppliedAmount.ToString("N2", CultureInfo.InvariantCulture));
            if (payment.TenderedAmount != payment.AppliedAmount)
            {
                builder.Append(" · tendered ")
                    .Append(payment.TenderedAmount.ToString("N2", CultureInfo.InvariantCulture));
            }
            if (!string.IsNullOrWhiteSpace(payment.Reference))
            {
                builder.Append(" · ref ").Append(payment.Reference);
            }
            builder.AppendLine();
        }
        if (sale.ChangeAmount > 0m)
            builder.AppendLine("Change: " + receipt.CurrencyCode + " " + sale.ChangeAmount.ToString("N2", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(sale.Notes))
            builder.AppendLine("Notes: " + sale.Notes);
        builder.AppendLine(new string('-', 42));
        builder.AppendLine("Thank you.");
        return builder.ToString();
    }

    public bool Print(SaleReceiptData receipt)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        var document = BuildDocument(receipt);
        document.PageWidth = Math.Min(dialog.PrintableAreaWidth, 320);
        document.PagePadding = new Thickness(12);
        document.ColumnWidth = document.PageWidth;
        document.FontFamily = new FontFamily("Segoe UI");
        document.FontSize = 11;

        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "Receipt " + receipt.Sale.Number);
        return true;
    }

    private static FlowDocument BuildDocument(SaleReceiptData receipt)
    {
        var sale = receipt.Sale;
        var document = new FlowDocument();

        document.Blocks.Add(new Paragraph(new Run(receipt.BusinessName))
        {
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
        });
        document.Blocks.Add(new Paragraph(new Run(receipt.BusinessSubtitle))
        {
            FontSize = 9,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
        });

        document.Blocks.Add(new Paragraph(new Run(
            $"Receipt: {sale.Number}\nDate: {sale.SoldAt.LocalDateTime:yyyy-MM-dd HH:mm}\nCashier: {sale.CashierName}\nCustomer: {sale.CustomerName}"))
        {
            Margin = new Thickness(0, 0, 0, 8),
        });

        var table = new Table { CellSpacing = 2 };
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(72) });
        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        foreach (var item in sale.Items)
        {
            var row = new TableRow();
            row.Cells.Add(new TableCell(new Paragraph(new Run(
                item.Product + "\n" + item.Quantity.ToString("0.######", CultureInfo.InvariantCulture) + " " + item.Unit +
                " × " + item.UnitPrice.ToString("N2", CultureInfo.InvariantCulture)))
            { Margin = new Thickness(0, 2, 0, 2) }));
            row.Cells.Add(new TableCell(new Paragraph(new Run(item.NetTotal.ToString("N2", CultureInfo.InvariantCulture)))
            { TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 2, 0, 2) }));
            group.Rows.Add(row);
        }

        document.Blocks.Add(table);

        var total = new Paragraph
        {
            Margin = new Thickness(0, 8, 0, 8),
            TextAlignment = TextAlignment.Right,
        };
        total.Inlines.Add(new Run("TOTAL  " + receipt.CurrencyCode + " " + sale.NetTotal.ToString("N2", CultureInfo.InvariantCulture))
        {
            FontSize = 15,
            FontWeight = FontWeights.Bold,
        });
        document.Blocks.Add(total);

        var payments = new StringBuilder("Payments:\n");
        foreach (var payment in sale.Payments)
        {
            payments.Append(payment.MethodName)
                .Append(": ").Append(receipt.CurrencyCode).Append(' ')
                .Append(payment.AppliedAmount.ToString("N2", CultureInfo.InvariantCulture));
            if (payment.TenderedAmount != payment.AppliedAmount)
                payments.Append(" · tendered ").Append(payment.TenderedAmount.ToString("N2", CultureInfo.InvariantCulture));
            payments.AppendLine();
        }
        if (sale.ChangeAmount > 0m)
            payments.AppendLine("Change: " + receipt.CurrencyCode + " " + sale.ChangeAmount.ToString("N2", CultureInfo.InvariantCulture));

        document.Blocks.Add(new Paragraph(new Run(payments.ToString())) { Margin = new Thickness(0, 0, 0, 8) });
        document.Blocks.Add(new Paragraph(new Run("Thank you."))
        {
            TextAlignment = TextAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
        });

        return document;
    }
}
