using BusinessOS.POS.Application.Abstractions.Sales;

namespace BusinessOS.POS.Desktop.Sales;

public interface IReceiptPrintService
{
    string FormatReceipt(SaleReceiptData receipt);
    bool Print(SaleReceiptData receipt);
}
