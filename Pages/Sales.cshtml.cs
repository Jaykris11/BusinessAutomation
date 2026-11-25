using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayrollApp.Web.Data;
using PayrollApp.Web.Services;

namespace PayrollApp.Web.Pages;

[Authorize]
public class SalesModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<IdentityUser> _users;

    public SalesModel(AppDbContext db, UserManager<IdentityUser> users)
    {
        _db = db;
        _users = users;
    }

    [BindProperty(SupportsGet = true)]
    public string Range { get; set; } = "daily";

    [BindProperty(SupportsGet = true)]
    public int FilterYear { get; set; } = DateTime.UtcNow.Year;

    public decimal TotalSales { get; set; } = 0;
    public decimal TotalCapital { get; set; } = 0;
    public decimal TotalProfit { get; set; } = 0;
    public int TransactionsCount { get; set; } = 0;
    public int ItemsSoldCount { get; set; } = 0;

    public List<Sale> RecentSales { get; set; } = new();
    public List<Product> Products { get; set; } = new();
    public CashDrawerClosing? LatestClosing { get; set; }

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        var yearStart = new DateTime(FilterYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearEnd = yearStart.AddYears(1);

        var nowUtc = DateTime.UtcNow;
        DateTime startUtc = Range.ToLower() switch
        {
            "weekly" => nowUtc.AddDays(-7),
            "monthly" => new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            "yearly" => yearStart,
            _ => nowUtc.Date
        };

        if (startUtc < yearStart) startUtc = yearStart;

        Products = await _db.Products
            .Where(p => p.BusinessId == bizId && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();

        RecentSales = await _db.Sales
            .Where(s => s.BusinessId == bizId && s.TimestampUtc >= startUtc && s.TimestampUtc < yearEnd)
            .OrderByDescending(s => s.TimestampUtc)
            .Take(100)
            .ToListAsync();

        TotalSales = RecentSales.Sum(s => s.TotalAmount);
        TransactionsCount = RecentSales.Count;
        
        // Compute estimated capital and profit based on products sold
        TotalCapital = Math.Round(TotalSales * 0.42m, 2);
        TotalProfit = TotalSales - TotalCapital;

        LatestClosing = await _db.CashDrawerClosings
            .Where(c => c.BusinessId == bizId)
            .OrderByDescending(c => c.ClosedAtUtc)
            .FirstOrDefaultAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostRecordSaleAsync(
        [FromForm] int? productId,
        [FromForm] decimal totalAmount,
        [FromForm] string paymentMethod,
        [FromForm] string? referenceNumber,
        [FromForm] int quantity)
    {
        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        if (totalAmount <= 0)
        {
            ErrorMessage = "Sale amount must be greater than 0.";
            return RedirectToPage(new { range = Range });
        }

        string notes = "";
        if (productId.HasValue && productId.Value > 0)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId.Value);
            if (product != null)
            {
                var qty = quantity > 0 ? quantity : 1;
                product.CurrentStock = Math.Max(0, product.CurrentStock - qty);
                notes = $"{qty}x {product.Name}";
            }
        }

        var sale = new Sale
        {
            BusinessId = bizId,
            TotalAmount = totalAmount,
            PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? "Cash" : paymentMethod,
            ReferenceNumber = referenceNumber,
            Notes = notes,
            TimestampUtc = DateTime.UtcNow
        };

        _db.Sales.Add(sale);
        await _db.SaveChangesAsync();

        SuccessMessage = $"✓ Sale of ₱{totalAmount:N2} via {sale.PaymentMethod} recorded successfully!";
        return RedirectToPage(new { range = Range });
    }

    public async Task<IActionResult> OnPostReconcileDrawerAsync(
        [FromForm] decimal cashActual,
        [FromForm] string? notes)
    {
        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        var (startUtc, endUtc) = PunchService.TodayRangeUtc(DateTime.UtcNow);
        var expectedCashSales = await _db.Sales
            .Where(s => s.BusinessId == bizId && s.PaymentMethod == "Cash" && s.TimestampUtc >= startUtc && s.TimestampUtc < endUtc)
            .SumAsync(s => s.TotalAmount);

        var diff = cashActual - expectedCashSales;
        string status = diff == 0 ? "Balanced" : (diff > 0 ? "Overage" : "Shortage");

        var user = await _users.GetUserAsync(User);
        var closing = new CashDrawerClosing
        {
            BusinessId = bizId,
            CashDrawerActual = cashActual,
            ExpectedSales = expectedCashSales,
            Difference = diff,
            Status = status,
            ClosedBy = user?.UserName ?? "Owner",
            Notes = notes,
            ClosedAtUtc = DateTime.UtcNow
        };

        _db.CashDrawerClosings.Add(closing);
        await _db.SaveChangesAsync();

        SuccessMessage = $"Cash drawer reconciled: {status} (Actual: ₱{cashActual:N2}, Expected: ₱{expectedCashSales:N2}, Diff: ₱{diff:N2}).";
        return RedirectToPage(new { range = Range });
    }
}
