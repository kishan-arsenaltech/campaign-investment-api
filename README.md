# Campaign Investment API

Campaign Investment API is built in .NET 9 (ASP.NET Core Web API) with a modular, maintainable architecture (Core → Service → Repo → API), providing a backend for managing campaigns, investments, secure Stripe payments, and marketing/webhook integrations such as Klaviyo.

---

## Table of Contents

- Business Value
- Project Overview
- Tech Stack
- Repository Layout
- Getting Started (Local)
  - Prerequisites
  - Environment Variables
  - Run Locally
- Database & Migrations
- New Features

---

## Business Value

**Streamlined Campaign Funding** – Investors can directly fund campaigns through a secure payment gateway (Stripe), eliminating manual tracking.

**Transparent Investment Tracking** – Every contribution is recorded in the database and linked to a campaign for clear accountability.

**Automated Payment Handling** – Successful or failed transactions are automatically reflected in the system through Stripe webhooks.

**Marketing Automation** – With Klaviyo integration, investor engagement and follow-up campaigns (emails, notifications) can be automated.

**Security & Trust** – JWT authentication, webhook verification, and centralized error handling create a reliable and secure platform.

**Scalable Foundation** – Built with clean architecture and EF Core, making it easy to extend with analytics, reporting, or additional payment providers in the future.


## Project Overview

This API enables:

- Managing Campaigns & Investments
- Handling Stripe payments
- Receiving and verifying webhooks (Stripe, Klaviyo)
- Providing campaign performance analytics


**Architecture follows layered pattern:**

**Core** → Domain Models & DTOs

**Repo** → EF Core DbContext & Repository Layer

**Service** → Business Logic

**API** → Controllers, Middleware, Startup


---

## Tech Stack

- .NET 9 / ASP.NET Core Web API
- C# (Entity Models, DTOs, Services, Controllers)
- Entity Framework Core
- SQL Server (local or Azure SQL)
- Stripe (payments & webhooks)
- Klaviyo (marketing & tracking)
- Azure (App Service, SQL, Blob, Insights)
- Swagger for API documentation

---

## Repository Layout
- /Investment.Core # Domain models & DTOs
- /Investment.Repo # EF Core DbContext & Repositories
- /Investment.Service # Business logic & services
- /Investment # ASP.NET Core API project (Controllers, Startup)
- /migrations # EF Core migrations
- .gitignore
- README.md

---

## Getting Started (Local)

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com)
- SQL Server or LocalDB
- Git
- (Optional) Stripe CLI & ngrok

### Environment Variables

Configure `appsettings.Development.json` or `.env` with:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=CampaignDB;User Id=sa;Password=YourPassword;"
  },
  "Stripe": {
    "SecretKey": "sk_test_...",
    "WebhookSecret": "whsec_..."
  },
  "Klaviyo": {
    "ApiKey": "pk_...",
    "WebhookSecret": "kwsec_..."
  },
  "Jwt": {
    "Issuer": "campaign-api",
    "Audience": "campaign-client",
    "Key": "super_secret_key_123"
  }
}
```

### Run Locally

#### clone
git clone https://github.com/kishan-arsenaltech/campaign-investment-api.git
cd campaign-investment-api

#### restore dependencies
dotnet restore

## Database & Migrations

#### run migrations
dotnet ef database update --project Investment.Repo

##### add new migration
dotnet ef migrations add Init --project Investment.Repo

##### apply to DB
dotnet ef database update --project Investment.Repo

---

## New Features

### Modular Email Template System

Email templates are now stored and managed in the database via `EmailTemplate` and `EmailTemplateVariable` entities. A new `EmailTemplateController` exposes full CRUD. Templates support dynamic variable extraction at runtime and are categorized using the `EmailTemplateCategory` enum (35 categories covering welcome flows, password reset, ACH/DAF/Foundation payment notifications, grant updates, 2FA login codes, and reminder sequences).

Emails are dispatched asynchronously through a background queue:

- `IEmailQueue` / `EmailQueue` — thread-safe in-memory queue
- `EmailQueueWorker` — hosted service that dequeues and sends via Azure Communication Services or Gmail SMTP
- `EmailJobService` — enqueues outgoing emails from business logic

Template statuses: `Draft`, `Active`, `Inactive`.

---

### Site Configuration Management

A new `SiteConfigurationController` provides CRUD for platform-wide settings, with Azure Blob Storage support for image uploads per entry.

Supported configuration types (`SiteConfigurationType`):

| Type | Purpose |
|------|---------|
| `StaticValue` | Fixed platform values |
| `TransactionType` | Supported transaction method definitions |
| `Statistics` | Dashboard metric configurations |
| `MetaInformation` | SEO / page meta content |
| `Configuration` | General feature flags and settings |

---

### Module-Based Authorization

Fine-grained access control is enforced via the `[ModuleAuthorize]` attribute, which validates the current user's permissions against a module and permission type before the controller action executes.

```csharp
[ModuleAuthorize(Modules.Campaigns, PermissionType.Write)]
[HttpPost]
public async Task<IActionResult> Create([FromBody] CampaignDto dto) { ... }
```

Permissions are resolved by `PermissionHelper` against `ModuleAccessPermission` records stored per role. All module names are defined as constants in `Modules.cs` and permission levels in the `PermissionType` enum.

---

### Soft Delete & Restore

`BaseEntity` now includes `DeletedAt` and `DeletedBy` fields. Extension methods on `IQueryable` filter out soft-deleted records by default.

To restore a soft-deleted entity:

```csharp
entity.Restore(); // clears DeletedAt and DeletedBy
await context.SaveChangesAsync();
```

Pagination and filter DTOs (`PaginationDto`, `CompletedInvestmentsPaginationDto`) now accept an `IsDeleted` flag to let admins explicitly query deleted records.

---

### Scheduled Jobs (Quartz.NET)

Three background jobs are registered via `QuartzSchedulerExtensions`:

| Job | Description |
|-----|-------------|
| `DeleteArchivedUsersJob` | Permanently removes users soft-deleted beyond the retention window |
| `DeleteTestUsersJob` | Cleans up test/demo accounts on schedule |
| `SendReminderEmail` | Fires reminder emails at configured intervals |

Every job writes its execution result (start time, end time, status, error details) to a `SchedulerLogs` database table.

---

### Enhanced Error Logging Middleware

`ErrorHandlingMiddleware` now persists structured `ApiErrorLog` records to the database for every unhandled exception, capturing:

- Exception message and stack trace
- HTTP method, route, and raw request body
- User agent and device metadata
- IP address / geo-location
- Timestamp

---

### Centralized Secrets via Azure Key Vault

All application secrets are defined as constants in `SecretKeys.cs` and loaded at startup through `KeyVaultConfigService`. In production the values are pulled from Azure Key Vault; locally they fall back to `appsettings.json`.

New secrets introduced by this release:

| Key | Purpose |
|-----|---------|
| `blob-configuration` | Azure Blob Storage connection |
| `storage-secret-key` | Azure Storage secret |
| `communication-service-connection-string` | Azure Communication Services |
| `gmail-smtp-user` / `gmail-smtp-password` | Gmail SMTP fallback |
| `sender-address` | Default email sender |
| `admin-email` | Admin notification address |
| `ach-admin-email-list-for-new-payment-request` | ACH admin recipients |
| `email-list-for-scheduler` | Scheduler notification recipients |
| `captcha-secret-key` | reCAPTCHA secret |
| `api-access-token` | Internal API token |
| `public-api-token` | Public API token |
| `master-password` | Admin master password |

---

### Investment Stage Updates

The `InvestmentStage` enum has been updated:

| Value | Stage |
|-------|-------|
| 7 | `CompletedOngoing` *(renamed from `OngoingCompleted`)* |
| 9 | `CompletedOngoingPrivate` *(new)* |

> **Note:** Any code or seed data referencing `OngoingCompleted` should be updated to `CompletedOngoing`. The integer value `7` is unchanged.
