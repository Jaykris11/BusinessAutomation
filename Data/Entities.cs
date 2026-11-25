namespace PayrollApp.Web.Data;

public enum PunchType { TimeIn = 0, LunchOut = 1, LunchIn = 2, TimeOut = 3 }
public enum PayType { Hourly = 0, Daily = 1 }

// Every table that belongs to a client carries BusinessId (multi-tenant).
public class Business
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool RequireIpForPunch { get; set; } = true;
    public List<AllowedIp> AllowedIps { get; set; } = new();
}

public class AllowedIp
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public string Address { get; set; } = "";   // e.g. "112.200.10.55"
    public string? Label { get; set; }          // e.g. "Main store wifi"
}

public class Worker
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public string? UserId { get; set; }          // link to Identity user
    public string FullName { get; set; } = "";
    public PayType PayType { get; set; } = PayType.Hourly;
    public decimal Rate { get; set; }            // per hour, or per day
    public decimal HoursPerDay { get; set; } = 8m;
    public bool IsActive { get; set; } = true;

    public decimal HourlyRate => PayType == PayType.Hourly ? Rate : Rate / HoursPerDay;
}

// Raw timestamps only. Hours and pay are always computed from these.
public class Punch
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int WorkerId { get; set; }
    public PunchType Type { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? IpAddress { get; set; }
    public string? SelfiePath { get; set; }
}

// Rejected attempts (wrong network) so the owner can see them.
public class RejectedPunch
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int WorkerId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? IpAddress { get; set; }
    public string Reason { get; set; } = "";
}

public class AuditLog
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public string ActorUserId { get; set; } = "";
    public string Action { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

// Inventory & Stock Tracking
public class Product
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "General";
    public decimal Price { get; set; }
    public decimal Cost { get; set; }
    public int CurrentStock { get; set; }
    public int LowStockAlert { get; set; } = 10;
    public bool IsActive { get; set; } = true;

    public bool IsLowStock => CurrentStock <= LowStockAlert;
}

// Sales & Revenue Tracking
public class Sale
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = "Cash"; // Cash, GCash, Maya
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

// Announcements & Memo Board
public class Announcement
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Priority { get; set; } = "Normal"; // Normal, Urgent
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

// End of Day Cash Drawer Reconciliation (Z-Read)
public class CashDrawerClosing
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public decimal CashDrawerActual { get; set; }
    public decimal ExpectedSales { get; set; }
    public decimal Difference { get; set; } // Actual - Expected
    public string Status { get; set; } = "Balanced"; // Balanced, Shortage, Overage
    public string ClosedBy { get; set; } = "Owner";
    public DateTime ClosedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}

// Employee Leave / PTO / VTO Requests with Slot Tracking
public enum LeaveType { PTO = 0, VTO = 1, SickLeave = 2, VacationLeave = 3 }
public enum LeaveStatus { Pending = 0, Approved = 1, Rejected = 2 }

public class LeaveRequest
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int WorkerId { get; set; }
    public LeaveType Type { get; set; } = LeaveType.PTO;
    public DateTime TargetDate { get; set; }
    public string Reason { get; set; } = "";
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public string? OwnerNote { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
}

// Active Store Shift Control (Start / Stop)
public class StoreShiftSession
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public string ShiftName { get; set; } = "Morning Shift";
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public string StartedBy { get; set; } = "Owner";
    public string? EndedBy { get; set; }
}

// Employee Cash Advance / Vale Requests
public enum AdvanceStatus { Pending = 0, Approved = 1, Rejected = 2, PaidOut = 3 }

public class AdvancePaymentRequest
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int WorkerId { get; set; }
    public decimal AmountRequested { get; set; }
    public string Reason { get; set; } = "";
    public AdvanceStatus Status { get; set; } = AdvanceStatus.Pending;
    public string? OwnerNote { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
}

// Payment Slip / Payslip issued by Owner to Employee
public class Payslip
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int WorkerId { get; set; }
    public string PayPeriod { get; set; } = ""; // e.g. "November 1 - 15, 2026"
    public decimal HoursWorked { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal GrossPay { get; set; }
    public decimal CashAdvanceDeductions { get; set; } = 0;
    public decimal OtherDeductions { get; set; } = 0;
    public decimal NetPay { get; set; }
    public string PaymentMethod { get; set; } = "Cash"; // Cash, Bank, GCash
    public string? Remarks { get; set; }
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsAcknowledged { get; set; } = false;
    public DateTime? AcknowledgedAtUtc { get; set; }
    public bool IsArchived { get; set; } = false;
}





