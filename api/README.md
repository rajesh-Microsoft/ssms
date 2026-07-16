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
