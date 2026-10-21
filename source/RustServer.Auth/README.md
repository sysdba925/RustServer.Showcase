# RustServer.Auth

Authentication and authorization service for the RustServer platform.

Built with ASP.NET Core 10, ASP.NET Core Identity, Entity Framework Core, and SQLite.

For Linux production deployment, see
[the production deployment guide](docs/production-deployment.md).

## Features

- Local authentication with username and password
- Cookie-based authentication
- Login and logout endpoints
- Current user endpoint
- Nginx `auth_request` verification endpoints
- Google and Steam account linking for administrators
- User roles and permissions
- Account lockout after failed login attempts
- Initial administrator creation from `.env`
- SQLite persistence

---

## Authentication flow diagrams

- [Username and password authentication](docs/authentication-flow.puml)
- [Administrator access and provider linking](docs/admin-access-flow.puml)

The diagrams document the current implementation, including HTTP responses,
middleware callbacks, and routes that are referenced by the UI but are not
implemented in this repository.

## Routes for direct reverse-proxy routing

RustServer.Auth serves its login page directly at `GET /login`. It returns
`wwwroot/index.html` as `text/html`; its existing `/login.css` and `/login.js`
assets remain public. `GET /` intentionally has no login-page mapping, leaving
that route available for the platform portal.

Auth owns `/login`, `/api/auth/*`, `/portal`, `/admin`, and `/health/ready`.
The `/player` and `/dashboard` links on the administrator page are owned by
other platform applications and are not implemented here.

---

## 1. Local Development

### Requirements

- .NET 10 SDK
- EF Core CLI tools

Install EF Core CLI if necessary:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.12
```

### Database

The application uses SQLite.

The development connection string is inherited from `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "AuthDatabase": "Data Source=auth.db"
  }
}
```

Create or update the database:

```powershell
dotnet ef database update
```

The `auth.db` file is created in the working directory. Application startup
automatically applies pending EF Core migrations; `dotnet ef database update`
remains useful for manual maintenance.

### Initial Administrator

The initial administrator is created automatically when the user database is empty.

Create `.env` in the project directory:

```dotenv
ADMIN_USERNAME=admin
ADMIN_PASSWORD=<your-secure-password>
```

Password requirements:

- Minimum 12 characters
- Must satisfy the configured ASP.NET Core Identity password policy

Start the application:

```powershell
dotnet run
```

The application creates the initial administrator using ASP.NET Core Identity.

After successful creation, remove `.env`.

**Important:**

- Never commit `.env` or actual credentials.
- `.env.example` must contain placeholders only.
- Passwords are stored as hashes in SQLite.
- Changing `.env` does not change an existing user's password.
- The current bootstrap implementation checks whether any user exists, not whether an Admin role is assigned.

---

## 2. Authentication API

Base route:

`/api/auth`

### GET /api/auth/csrf

Returns a no-store antiforgery request token and sets the corresponding
antiforgery cookie. Send the token in the `X-CSRF-TOKEN` header for login and
logout requests.

### POST /api/auth/login

Authenticates a user and creates an authentication cookie.

Request:

```json
{
  "userName": "admin",
  "password": "<your-password>"
}
```

Responses:

| Status | Description |
|---|---|
| 200 OK | Authentication successful |
| 401 Unauthorized | Invalid credentials or login unsuccessful |
| 400 Bad Request | Missing or invalid CSRF token |

Successful authentication returns an HTTP-only cookie named `RustServer.Auth`.
First request `GET /api/auth/csrf` and send its `token` in the `X-CSRF-TOKEN`
header with this request.

### GET /api/auth/me

Returns information about the authenticated user.

Requires authentication.

Response:

```json
{
  "id": "...",
  "userName": "admin",
  "roles": ["Admin", "Player"],
  "googleLinked": true,
  "steamLinked": false
}
```

Possible responses:

| Status | Description |
|---|---|
| 200 OK | User authenticated |
| 401 Unauthorized | Missing or invalid session |

The external-login flags are read from the user's ASP.NET Core Identity external logins.

### GET /api/auth/verify

Fast, bodyless authentication check for Nginx `auth_request`.
It validates the existing `RustServer.Auth` Identity cookie through the standard
ASP.NET Core authentication middleware and does not make an explicit user or role lookup.

| Status | Description |
|---|---|
| 200 OK | Authenticated |
| 401 Unauthorized | Missing or invalid session |

### GET /api/auth/authorize/admin

Fast, bodyless administrator authorization check for Nginx `auth_request`.
Authorization uses the `Admin` role claim in the existing Identity cookie.

| Status | Description |
|---|---|
| 200 OK | Authenticated administrator |
| 401 Unauthorized | Missing or invalid session |
| 403 Forbidden | Authenticated user without the `Admin` role |

### POST /api/auth/logout

Signs out the current user.

Requires authentication.

| Status | Description |
|---|---|
| 204 No Content | Logout successful |
| 401 Unauthorized | User not authenticated |
| 400 Bad Request | Missing or invalid CSRF token |

`POST /api/auth/logout` also requires a token from `GET /api/auth/csrf` in the
`X-CSRF-TOKEN` header.

### External provider routes

`GET /api/auth/google/login` and `GET /api/auth/steam/login` start their
respective sign-in flows. Their ASP.NET Core middleware callback paths remain
`/signin-google` and `/signin-steam`; the application callbacks then redirect
successful sign-ins to `/portal`, and failed sign-ins to `/login` with the
provider-specific error query parameter.

Administrator-only `GET /api/auth/google/link` and
`GET /api/auth/steam/link` start account linking. Successful application
callbacks redirect to `/admin?googleLinked=1` and `/admin?steamLinked=1`.

---

## 3. Manual API Testing

Run the application using HTTPS.

Use an HTTP client that preserves cookies between requests.

### Test 1: Unauthorized Access

Request:

`GET /api/auth/me`

Expected:

`401 Unauthorized`

### Test 2: Login

Request:

`POST /api/auth/login`

Body:

```json
{
  "userName": "admin",
  "password": "<your-password>"
}
```

Expected:

- `200 OK`
- Response contains a `Set-Cookie` header

### Test 3: Authenticated Access

Using the cookie received during login:

`GET /api/auth/me`

Expected:

```json
{
  "name": "admin"
}
```

### Test 4: Logout

Using the same session:

`POST /api/auth/logout`

Expected:

`204 No Content`

### Test 5: Verify Logout

Request:

`GET /api/auth/me`

Expected:

`401 Unauthorized`

**Note:** Swagger UI may not preserve authentication cookies across requests in every configuration. Use a browser or HTTP client with cookie support if the authenticated test fails unexpectedly.

---

## 4. Database Inspection

Open `auth.db` using a SQLite database viewer.

Important tables:

| Table | Purpose |
|---|---|
| AspNetUsers | Users and password hashes |
| AspNetRoles | Roles |
| AspNetUserRoles | User-role assignments |
| AspNetUserLogins | External authentication providers |
| AspNetUserTokens | Identity user tokens |
| __EFMigrationsHistory | Applied EF Core migrations |

The initial administrator should appear in `AspNetUsers`.

The bootstrap administrator receives the `Admin` and `Player` roles.

---

## 5. Security Notes

Current configuration:

- HTTP-only authentication cookie
- Secure cookie (HTTPS only)
- SameSite=Lax
- Eight-hour cookie expiration setting
- Account lockout after five failed login attempts for 60 minutes
- Credentials stored using ASP.NET Core Identity password hashing
- Nginx verification endpoints use standard cookie authentication and role claims;
  the Identity security-stamp validator can still periodically revalidate a session.

Production deployment binds Auth only to loopback and keeps credentials and
SQLite data outside release directories. Install a TLS-terminating reverse proxy
for public traffic. Auth accepts the standard `X-Forwarded-For`,
`X-Forwarded-Proto`, and `X-Forwarded-Host` header set only from the IP
addresses in `ReverseProxy:TrustedProxies`; the Production configuration
explicitly trusts the loopback IPv4 and IPv6 addresses. This ensures OAuth
callback URLs use the public HTTPS scheme and host while ignoring forwarded
headers from untrusted clients. See the
[production deployment guide](docs/production-deployment.md) for the required
permissions, systemd service, secrets, migrations, and readiness checks.

---

## 6. Architecture

The service follows:

**Feature-Based Architecture + Command Handlers + Orchestration + Ports & Adapters**

Responsibilities:

- **Controllers:** HTTP request and response handling
- **Handlers:** Application operations
- **Infrastructure:** ASP.NET Core Identity, EF Core, persistence
- **Configuration:** Dependency injection and service configuration
- **Bootstrap:** Initial application setup

`Program.cs` is responsible only for application composition and startup.

---

## 7. Useful Commands

Build:

```powershell
dotnet build
```

Run:

```powershell
dotnet run
```

Create migration:

```powershell
dotnet ef migrations add MigrationName
```

Apply migrations:

```powershell
dotnet ef database update
```

List migrations:

```powershell
dotnet ef migrations list
```

Run in Development:

```powershell
dotnet run --environment Development
```
