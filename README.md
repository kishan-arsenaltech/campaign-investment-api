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
git clone https://github.com/your-org/campaign-investment-api.git
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