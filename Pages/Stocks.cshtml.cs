using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayrollApp.Web.Data;

namespace PayrollApp.Web.Pages;

[Authorize]
public class StocksModel : PageModel
{
    private readonly AppDbContext _db;

    public StocksModel(AppDbContext db) => _db = db;

    public List<Product> Products { get; set; } = new();
    public int TotalUnitsInStock { get; set; } = 0;
    public int LowStockCount { get; set; } = 0;
    public decimal TotalInventoryCostValuation { get; set; } = 0;
    public decimal TotalInventoryRetailValuation { get; set; } = 0;

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        Products = await _db.Products
            .Where(p => p.BusinessId == bizId && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();

        TotalUnitsInStock = Products.Sum(p => p.CurrentStock);
        LowStockCount = Products.Count(p => p.IsLowStock);
        TotalInventoryCostValuation = Products.Sum(p => p.Cost * p.CurrentStock);
        TotalInventoryRetailValuation = Products.Sum(p => p.Price * p.CurrentStock);

        return Page();
    }

    public async Task<IActionResult> OnPostAddProductAsync(
        [FromForm] string name,
        [FromForm] string category,
        [FromForm] decimal price,
        [FromForm] decimal cost,
        [FromForm] int initialStock,
        [FromForm] int alertThreshold)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorMessage = "Product name is required.";
            return RedirectToPage();
        }

        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        var product = new Product
        {
            BusinessId = bizId,
            Name = name.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(),
            Price = price > 0 ? price : 0m,
            Cost = cost > 0 ? cost : 0m,
            CurrentStock = initialStock >= 0 ? initialStock : 0,
            LowStockAlert = alertThreshold > 0 ? alertThreshold : 5,
            IsActive = true
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        SuccessMessage = $"✓ New product '{product.Name}' added to inventory!";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestockAsync([FromForm] int productId, [FromForm] int addUnits)
    {
        if (addUnits <= 0)
        {
            ErrorMessage = "Restock quantity must be positive.";
            return RedirectToPage();
        }

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product != null)
        {
            product.CurrentStock += addUnits;
            await _db.SaveChangesAsync();
            SuccessMessage = $"✓ Added +{addUnits} units to {product.Name}. New stock: {product.CurrentStock}.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteProductAsync([FromForm] int productId)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product != null)
        {
            product.IsActive = false;
            await _db.SaveChangesAsync();
            SuccessMessage = $"Product '{product.Name}' removed from inventory.";
        }
        return RedirectToPage();
    }
}
