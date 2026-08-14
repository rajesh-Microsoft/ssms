# SMMS.Api

ASP.NET Core (.NET 10) Web API backend for the Society Maintenance Management System.
This is being built incrementally alongside the existing Excel-based front-end
(`SMMS_BankImport_v5_fixed.html`) — the front-end has **not** been switched over to
call this API yet.

## Stack

- ASP.NET Core Web API (controllers) + EF Core
- SQL Server (LocalDB for local dev; Azure SQL Database for deployment, provisioned later)
- JWT bearer authentication, `PasswordHasher<T>` (PBKDF2) for password/security-answer hashing
- Role-based authorization: `Admin` (full CRUD) vs `Member` (read-only)

## First-time setup

1. Requires the .NET 10 SDK and SQL Server LocalDB (`sqllocaldb info` to check).
2. Set the JWT signing key as a user secret (never commit this):
   ```powershell
   cd api/SMMS.Api
   dotnet user-secrets set "Jwt:Key" "<a long random string, 32+ chars>"
   ```
3. Apply EF Core migrations to create/update the local database:
   ```powershell
   dotnet ef database update
   ```
4. Run the API:
   ```powershell
   dotnet run
   ```

On first run, the database is seeded with a default Admin account: username `Admin`,
password `admin123` — change this immediately via `POST /api/users/{id}/reset-password`.

## Razorpay test checkout

SMMS can optionally create Razorpay test orders from the resident payment screen. Manual UPI
QR and proof upload remain available as a fallback. Test mode is disabled by default and does
not move real money.

1. Create or sign in to a Razorpay account, switch the dashboard to **Test Mode**, and generate
   a test Key ID and Key Secret.
2. Store both values with .NET user-secrets so they remain outside Git and OneDrive:
   ```powershell
   cd api/SMMS.Api
   dotnet user-secrets set "Razorpay:Enabled" "true"
   dotnet user-secrets set "Razorpay:KeyId" "rzp_test_your_key_id"
   dotnet user-secrets set "Razorpay:KeySecret" "your_test_key_secret"
   ```
3. Restart the API, sign in as a resident, open **Pay Online**, and choose
   **Pay with Razorpay**.
4. For a simulated UPI result in Razorpay Checkout, use `success@razorpay` or
   `failure@razorpay`. Razorpay's test card and netbanking options can also be used.

When running with Docker Compose, inject the test credentials into the current PowerShell
process instead of creating a `.env` file in the OneDrive workspace:

```powershell
$env:RAZORPAY_ENABLED = "true"
$env:RAZORPAY_KEY_ID = "rzp_test_your_key_id"
$env:RAZORPAY_KEY_SECRET = Read-Host "Razorpay test key secret"
docker compose up -d --build smms-api smms-ui
```

The server creates the order and verifies the checkout HMAC, order ID, amount, currency, and
captured payment status before marking an SMMS invoice paid. The Key Secret is never returned
to the browser.

### Webhook (authoritative settlement)

The browser callback is best-effort: if the resident closes the tab between capture and
verification, the invoice would stay unpaid. `POST /api/webhooks/razorpay` closes that gap and
Razorpay retries it until it gets a 2xx.

1. In the Razorpay dashboard, add a webhook for the `payment.captured` (and optionally
   `order.paid`) event pointing at `https://ssms.<your-domain>/api/webhooks/razorpay`, and set a
   webhook secret.
2. Supply that secret as `Razorpay:WebhookSecret` (user-secrets) or `RAZORPAY_WEBHOOK_SECRET`
   (Docker Compose). Without it the endpoint returns 404 and no webhook is processed.

The endpoint is anonymous but nothing in the payload is trusted until the `X-Razorpay-Signature`
HMAC over the raw body is verified. It arrives with no tenant subdomain, so the society is read
from the `society` note stamped onto the order at creation time; the amount and currency are
re-checked against the SMMS invoice, and an already-approved attempt is a no-op.

This is a sandbox integration for one platform Razorpay account. Before production use, decide
how each society completes merchant onboarding and receives settlement into its own bank account.

## PhonePe checkout

PhonePe Standard Checkout (v2) is available alongside manual UPI and Razorpay. The resident is
redirected to a PhonePe-hosted page, so no card or UPI credentials ever reach SMMS.

Authentication is OAuth client-credentials: SMMS exchanges the client id/secret for a short-lived
`O-Bearer` token, caches it, and refreshes it a minute before expiry. Sandbox and production use
different hosts, selected by `PhonePe:Environment` (`Sandbox` or `Production`).

1. Register at the [PhonePe Business dashboard](https://business.phonepe.com/) and complete
   merchant KYC. From **Developer Settings**, copy the Client ID, Client Secret and Client Version.
2. Store them outside Git:
   ```powershell
   cd api/SMMS.Api
   dotnet user-secrets set "PhonePe:Enabled" "true"
   dotnet user-secrets set "PhonePe:Environment" "Sandbox"
   dotnet user-secrets set "PhonePe:ClientId" "<client id>"
   dotnet user-secrets set "PhonePe:ClientSecret" "<client secret>"
   dotnet user-secrets set "PhonePe:AllowedSocieties" "demo"
   ```
3. Under Docker Compose, use `PHONEPE_ENABLED`, `PHONEPE_ENVIRONMENT`, `PHONEPE_CLIENT_ID`,
   `PHONEPE_CLIENT_SECRET`, `PHONEPE_CLIENT_VERSION`, `PHONEPE_WEBHOOK_USERNAME`,
   `PHONEPE_WEBHOOK_PASSWORD` and `PHONEPE_ALLOWED_SOCIETIES`.

As with Razorpay, two independent gates must both be open before a resident sees the button:
`PhonePe:AllowedSocieties` must include the society (empty means nobody), **and** that society's
`Settings.OnlinePaymentsEnabled` must be true.

### Webhook (authoritative settlement)

The redirect back from PhonePe is best-effort — a resident who closes the tab would otherwise
leave the invoice unpaid. Configure a webhook in the PhonePe dashboard under \*\*Developer Settings

> Webhook\*\*:

- URL: `https://ssms.<your-domain>/api/webhooks/phonepe`
- Authentication type: **SHA**, with a username and password you also supply as
  `PhonePe:WebhookUsername` / `PhonePe:WebhookPassword`. Without them the endpoint returns 404.
- Events: `checkout.order.completed` (and `checkout.order.failed` if you want failure logging).

PhonePe authenticates with `SHA256(username:password)` in the `Authorization` header, which only
proves the caller knows a shared secret — it does not sign the payload. So a valid header alone
never settles anything: the webhook always re-confirms the order against the Order Status API and
checks the amount against the SMMS invoice before marking it paid. An already-approved attempt is
a no-op, so duplicate deliveries are safe. If confirmation fails the endpoint returns 503 so
PhonePe retries.

The webhook arrives with no tenant subdomain, so the society is read from `metaInfo.udf1`, falling
back to the society encoded in the merchant order id (`SMMS_<society>_<collectionId>_<random>`).

**Not yet decided:** this uses a single platform merchant account, so settlement lands in one bank
account rather than each society's. Collecting maintenance for many societies into one account has
regulatory implications in India — resolve merchant onboarding per society before going live.

## Utility bill integrations

Utility connections are tenant-local and configured by society administrators from
**Settings > Utility Connections**. TGSPDCL is the first provider; additional providers register
an `IUtilityProvider` implementation without changing connection, bill, budget, or UI code.

The TGSPDCL provider uses the configured direct bill URL first and parses server-rendered HTML
with HtmlAgilityPack. If a provider later requires client-side rendering, Playwright Chromium is
available as an automatic fallback. Provider URLs and browser selectors live under
`Utilities:Providers` in `appsettings.json`; none are hardcoded in the provider implementation.

Active, auto-fetch connections are checked daily at 08:00 Asia/Kolkata. Fetches retry with
exponential backoff, update the connection's last error without crashing the sweep, upsert one
bill per connection/month, and create one notification for each new bill. A bill for the selected
budget month overrides that category's estimate in projections, but does not create an expense;
the admin still books payment through the existing Budget Planner workflow.

The EF migration is `20260814171119_AddUtilityBillIntegration`. A reviewable standalone script is
also available at `deploy/sql/20260814171119_AddUtilityBillIntegration.sql`. Docker images install
Playwright Chromium during build, so image builds need outbound access to NuGet and Playwright's
browser package servers.

Utility endpoints:

- `GET /api/utilities/providers`
- `GET/POST /api/utilities/connections`
- `PUT/DELETE /api/utilities/connections/{id}`
- `POST /api/utilities/connections/{id}/fetch`
- `GET /api/utilities/bills`, `GET /api/utilities/bills/{id}`
- `GET /api/utilities/bills/{id}/download`
- `GET /api/utilities/notifications`

All utility endpoints require a tenant JWT. Only `Admin` can create, edit, delete, or fetch a
connection; authenticated members can read bills and notifications.

## Key endpoints

- `POST /api/auth/login`, `POST /api/auth/signup`, `GET /api/auth/security-question`, `POST /api/auth/reset-password`
- `GET/POST/PUT/DELETE /api/members`
- `GET/POST/PUT/DELETE /api/collections`
- `GET/POST/PUT/DELETE /api/expenses`
- `GET/PUT /api/settings`
- `GET/POST /api/users`, `POST /api/users/{id}/approve`, `POST /api/users/{id}/reset-password`, `DELETE /api/users/{id}`
- `GET /api/auditlog` (Admin only)

All endpoints except `auth/*` require a `Bearer` JWT. Mutating endpoints require the `Admin` role.

## Not done yet

- Front-end (`app.js`) still talks to the in-memory `DB` object / Excel file, not this API.
- No Azure resources have been provisioned — deployment (Azure SQL, App Service/Container Apps)
  will be planned via the `azure-prepare` workflow when the API is ready to cut over.
