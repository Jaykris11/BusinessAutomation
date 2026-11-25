# Laufey Business Automation & Workforce Payroll System

A full-stack business operations, point-of-sale (POS), inventory control, and real-time workforce payroll automation platform built with **ASP.NET Core 10 (Razor Pages)**, **Entity Framework Core**, and **PostgreSQL (Supabase)**.

Designed with a unified responsive interface across desktop and mobile viewports, the system separates administrative financial operations from employee self-service workflows while enforcing strict role-based access control, IP-verified kiosk attendance, and automated payroll computation.

---

## Core Architecture & Features

### 1. Owner Executive Portal
- **Real-Time Operations Dashboard (`/Index`)**:
  - Live polling counters tracking currently clocked-in personnel, active labor cost burn rate, and daily sales without manual page refreshes.
  - Interactive financial performance analytics tracking Gross Sales, Inventory Capital, and Net Profit across Daily, Weekly, Monthly, and Yearly intervals.
  - Store shift management (Start / End Store Shift) and company-wide memo broadcasting.
- **Point of Sale & Revenue Tracking (`/Sales`)**:
  - Automated sales calculation linked directly to catalog selling prices (SRP) and product stock deduction.
  - Storage-optimized querying with Year (`2026`, `2025`) and Timeframe (`Daily`, `Weekly`, `Monthly`, `Yearly`) filters to minimize database payload and memory overhead.
  - End-of-Day Cash Drawer Reconciliation (Z-Read) with denomination counters and variance auditing.
- **Stock & Inventory Management (`/Stocks`)**:
  - Dedicated catalog management tracking cost price, selling price (SRP), current stock levels, and configurable low-stock alert thresholds.
- **Real-Time Employee Performance Leaderboard (`/StaffPerformance`)**:
  - Auto-syncing leaderboard ranking staff by verified hours worked and accumulated wages with Daily, Weekly, Monthly, and Yearly filtering.
- **Requests & Approvals Queue (`/Approvals`)**:
  - Centralized review workflow for employee Leave/PTO/VTO requests and Cash Advance (Vale) applications with approval/rejection notes.
- **Payslip Issuance & Archival (`/Payslips`)**:
  - Targeted individual payslip generation that automatically pre-fills logged monthly hours, hourly rates, and pending approved cash advance deductions.
  - Printable/PDF Official Compensation Voucher modal for every issued payslip.
  - Active/Archived storage segregation and year-based filtering to keep historical records organized without degrading query speed.

### 2. Employee Self-Service & Kiosk Portal
- **Public Kiosk Time Clock (`/Login`)**:
  - Dedicated quick kiosk modal on the sign-in screen allowing staff to record `Time In`, `Lunch Out`, `Lunch In`, and `Time Out` using their Employee ID (`EMP-001`, etc.) or full name.
  - Strict sequential punch validation and store Wi-Fi IP verification.
- **Staff Workspace (`/Clock`)**:
  - Real-time live-updating counters for today's hours worked, gross earnings today, and month-to-date accumulated wages.
  - Verified punch history log and live store announcements/memos from management.
- **Leave & Vale Requests (`/MyRequests`)**:
  - Paid Time Off (PTO), Voluntary Time Off (VTO), and Sick Leave submission with real-time daily slot capacity indicators (blocking submissions when daily leave slots are full).
  - Cash Advance (Vale) request submission and live approval status tracking.
- **Official Payment Slips (`/MyPayslips`)**:
  - View released payslips, inspect itemized earnings and deductions inside an interactive Official Compensation Voucher receipt modal, print/export to PDF, and digitally acknowledge receipt.

---

## Security & Hardening Measures

- **Role-Based Access Control (ASP.NET Core Identity)**: Strict separation between `Owner` and `Employee` roles with cookie-based session management and account lockout protection after failed attempts.
- **Rate Limiting**: Global fixed-window IP rate limiting (`120 requests/minute`) configured via `Microsoft.AspNetCore.RateLimiting` to mitigate brute-force and denial-of-service abuse.
- **HTTP Security Headers**: Enforced `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`, `Referrer-Policy: strict-origin-when-cross-origin`, and `X-XSS-Protection` headers across all responses.
- **Credential Segregation**: Database connection strings and secrets are excluded from version control via `.gitignore` and managed through environment variables (`ConnectionStrings__Default`) in production.
- **UTC Timestamp Integrity**: All PostgreSQL `timestamp with time zone` queries enforce explicit UTC `DateTimeKind` normalization for accurate Philippine Standard Time (`UTC+8`) payroll calculations.

---

## Technology Stack

- **Backend**: .NET 10, ASP.NET Core Razor Pages, C#
- **Database & ORM**: PostgreSQL (Supabase), Entity Framework Core, Npgsql
- **Authentication**: ASP.NET Core Identity
- **Frontend**: HTML5, CSS3 (Responsive Flexbox/Grid & Print Stylesheets), Vanilla JavaScript (Fetch API real-time polling)
- **Containerization**: Docker multi-stage build

---

## Local Setup & Configuration

### Prerequisites
- .NET 10 SDK
- PostgreSQL database instance (local or Supabase)

### Installation Steps
1. Clone the repository:
   ```bash
   git clone https://github.com/Jaykris11/BusinessAutomation.git
   cd BusinessAutomation
   ```

2. Create your local configuration file from the example template:
   ```bash
   cp appsettings.example.json appsettings.json
   ```

3. Update `appsettings.json` with your PostgreSQL / Supabase connection string:
   ```json
   {
     "ConnectionStrings": {
       "Default": "Host=YOUR_HOST;Port=5432;Database=postgres;Username=postgres;Password=YOUR_PASSWORD;SSL Mode=Require;Trust Server Certificate=true"
     }
   }
   ```

4. Initialize the database schema (optional, as the application automatically creates missing tables on startup) using `supabase_init.sql`.

5. Run the application:
   ```bash
   dotnet run
   ```

---

## Deployment Notes

This application is an ASP.NET Core 10 web server packaged with a production `Dockerfile`. Because standard serverless static hosts (such as Vercel) do not natively host persistent .NET Kestrel runtimes without custom container runtimes, recommended production deployment targets include **Render**, **Railway**, **Fly.io**, or **Azure App Service** using the included `Dockerfile`.

Set the following environment variable in your cloud provider's dashboard before deploying:
- `ConnectionStrings__Default`: Your PostgreSQL / Supabase connection string.
