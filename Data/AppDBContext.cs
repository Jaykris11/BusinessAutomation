using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PayrollApp.Web.Data;

public class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<AllowedIp> AllowedIps => Set<AllowedIp>();
    public DbSet<Worker> Workers => Set<Worker>();
    public DbSet<Punch> Punches => Set<Punch>();
    public DbSet<RejectedPunch> RejectedPunches => Set<RejectedPunch>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<CashDrawerClosing> CashDrawerClosings => Set<CashDrawerClosing>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<AdvancePaymentRequest> AdvancePaymentRequests => Set<AdvancePaymentRequest>();
    public DbSet<StoreShiftSession> StoreShiftSessions => Set<StoreShiftSession>();
    public DbSet<Payslip> Payslips => Set<Payslip>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Worker>().Property(x => x.Rate).HasPrecision(12, 2);
        b.Entity<Worker>().Property(x => x.HoursPerDay).HasPrecision(5, 2);
        b.Entity<Worker>().Ignore(x => x.HourlyRate);
        b.Entity<Punch>().HasIndex(x => new { x.BusinessId, x.WorkerId, x.TimestampUtc });

        b.Entity<Product>().Property(x => x.Price).HasPrecision(12, 2);
        b.Entity<Product>().Property(x => x.Cost).HasPrecision(12, 2);
        b.Entity<Product>().Ignore(x => x.IsLowStock);
        b.Entity<Sale>().Property(x => x.TotalAmount).HasPrecision(12, 2);

        b.Entity<CashDrawerClosing>().Property(x => x.CashDrawerActual).HasPrecision(12, 2);
        b.Entity<CashDrawerClosing>().Property(x => x.ExpectedSales).HasPrecision(12, 2);
        b.Entity<CashDrawerClosing>().Property(x => x.Difference).HasPrecision(12, 2);

        b.Entity<AdvancePaymentRequest>().Property(x => x.AmountRequested).HasPrecision(12, 2);

        b.Entity<Payslip>().Property(x => x.HoursWorked).HasPrecision(8, 2);
        b.Entity<Payslip>().Property(x => x.HourlyRate).HasPrecision(12, 2);
        b.Entity<Payslip>().Property(x => x.GrossPay).HasPrecision(12, 2);
        b.Entity<Payslip>().Property(x => x.CashAdvanceDeductions).HasPrecision(12, 2);
        b.Entity<Payslip>().Property(x => x.OtherDeductions).HasPrecision(12, 2);
        b.Entity<Payslip>().Property(x => x.NetPay).HasPrecision(12, 2);
    }
}
