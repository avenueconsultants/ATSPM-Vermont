# ATSPM Docker Compose Setup

This project uses Docker Compose to orchestrate multiple services required for running the ATSPM (Automated Traffic Signal Performance Measures) application stack, including APIs, database, and frontend.

## Prerequisites

- Docker
- Docker Compose
- OpenSSL

## Certificate Setup

Each developer must generate SSL certificate files using OpenSSL. These files are required for HTTPS support and must be placed in the `nginx/certs` folder.

### Steps to Generate Certificates (Windows PowerShell)

1. Open PowerShell as Administrator.
2. Navigate to the `ATSPM` folder:
   ```powershell
   cd path\to\ATSPM
   ```
3. Run the following commands to create the certs folder and generate the certificates:
   ```powershell
   mkdir nginx\certs
   openssl req -x509 -nodes -days 365 -newkey rsa:2048 -keyout nginx\certs\aspnetapp.key -out nginx\certs\aspnetapp.crt -subj "/CN=localhost"
   openssl pkcs12 -export -out nginx\certs\aspnetapp.pfx -inkey nginx\certs\aspnetapp.key -in nginx\certs\aspnetapp.crt -passout pass:password
   ```
4. These files are now used by the Docker services via a volume mount defined in the `.env` file:
   ```env
   CERT_LOCATION=./nginx/certs:/root/.aspnet/https:ro
   ```

## Environment Variables

Copy `.env.example` to `.env` in the root of the project (next to `docker-compose.yml`) and replace the placeholder values with local secrets. The local `.env` file must not be checked in.

### Create the Local `.env`

```powershell
Copy-Item .env.example .env
```

### Sample `.env` File

```env
# Database Credentials
POSTGRES_USER=admin
POSTGRES_PASSWORD=change-me

# Connection Strings
CONFIG_CONNECTION=Host=postgres;Port=5432;Username=admin;Password=change-me;Database=ATSPM-Config;Pooling=true; Timeout=30;CommandTimeout=60;
AGGREGATION_CONNECTION=Host=postgres;Port=5432;Username=admin;Password=change-me;Database=ATSPM-Aggregation;Pooling=true; Timeout=30;CommandTimeout=60;
EVENTLOG_CONNECTION=Host=postgres;Port=5432;Username=admin;Password=change-me;Database=ATSPM-EventLogs;Pooling=true; Timeout=30;CommandTimeout=60;
IDENTITY_CONNECTION=Host=postgres;Port=5432;Username=admin;Password=change-me;Database=ATSPM-Identity;Pooling=true; Timeout=30;CommandTimeout=60;

# Database Providers
ConfigContext_Provider=PostgreSQL
AggregationContext_Provider=PostgreSQL
EventLogContext_Provider=PostgreSQL
IdentityContext_Provider=PostgreSQL

# Admin Configuration
ADMIN_EMAIL=admin@example.com
ADMIN_ROLE=Admin
ADMIN_PASSWORD="ChangeThisPassword1!"
SEED_ADMIN=true

# Allowed Hosts
ALLOWED_HOSTS=*

# Email Configuration
EmailConfiguration__Host=smtp.freesmtpservers.com
EmailConfiguration__Port=25
EmailConfiguration__UserName=
EmailConfiguration__Password=
EmailConfiguration__EnableSsl=false

# JWT Configuration
JWT_EXPIRE_DAYS=1
JWT_KEY=ReplaceWithLongRandomJwtSigningKey
JWT_ISSUER=ATSPM-Local

# Public site URL used after external login succeeds
ATSPM_SITE=https://localhost:3443

# Existing OIDC Provider
OIDC_PROVIDERS__UTAHID__DISPLAYNAME=Utah Id
OIDC_PROVIDERS__UTAHID__AUTHORITY=https://example-utahid-authority/
OIDC_PROVIDERS__UTAHID__CLIENTID=replace-me
OIDC_PROVIDERS__UTAHID__CLIENTSECRET=replace-me
OIDC_PROVIDERS__UTAHID__CALLBACKPATH=/identity/signin-utahid
OIDC_PROVIDERS__UTAHID__SCOPES__0=openid
OIDC_PROVIDERS__UTAHID__SCOPES__1=email
OIDC_PROVIDERS__UTAHID__SCOPES__2=profile
OIDC_PROVIDERS__UTAHID__SCOPES__3=app:Atspm

# Microsoft Entra Provider
OIDC_PROVIDERS__ENTRA__DISPLAYNAME=Microsoft Entra
OIDC_PROVIDERS__ENTRA__AUTHORITY=https://login.microsoftonline.com/your-tenant-id/v2.0
OIDC_PROVIDERS__ENTRA__CLIENTID=replace-me
OIDC_PROVIDERS__ENTRA__CLIENTSECRET=replace-me
OIDC_PROVIDERS__ENTRA__CALLBACKPATH=/identity/signin-entra
OIDC_PROVIDERS__ENTRA__SCOPES__0=openid
OIDC_PROVIDERS__ENTRA__SCOPES__1=email
OIDC_PROVIDERS__ENTRA__SCOPES__2=profile

# Certificate Volume Mapping
CERT_LOCATION=./nginx/certs:/root/.aspnet/https:ro

# Watchdog Configuration
WatchdogConfiguration__ScanDate=2025-01-15
WatchdogConfiguration__ConsecutiveCount=3
WatchdogConfiguration__LowHitThreshold=50
WatchdogConfiguration__MaximumPedestrianEvents=200
WatchdogConfiguration__MinimumRecords=500
WatchdogConfiguration__MinPhaseTerminations=50
WatchdogConfiguration__PercentThreshold=0.9
WatchdogConfiguration__PreviousDayPMPeakEnd=18
WatchdogConfiguration__PreviousDayPMPeakStart=17
WatchdogConfiguration__ScanDayEndHour=5
WatchdogConfiguration__ScanDayStartHour=1
WatchdogConfiguration__RampMainlineStartHour=15
WatchdogConfiguration__RampMainlineEndHour=19
WatchdogConfiguration__RampStuckQueueStartHour=1
WatchdogConfiguration__RampStuckQueueEndHour=4
WatchdogConfiguration__WeekdayOnly=false
WatchdogConfiguration__DefaultEmailAddress=alerts@example.com
WatchdogConfiguration__EmailAllErrors=false
WatchdogConfiguration__Sort=Location
```

## SSO Docker Testing

The Docker stack reads OIDC provider settings from `.env` and passes them into `identityapi` as nested ASP.NET Core configuration.

For local Docker testing, register redirect URIs in each provider that match the public HTTPS site and callback path:

- `https://localhost:3443/identity/signin-utahid`
- `https://localhost:3443/identity/signin-entra`

The nginx container proxies the public paths expected by the WebUI:

- `/config` -> `ConfigApi`
- `/data` -> `DataApi`
- `/report` -> `ReportApi`
- `/identity` -> `IdentityApi`

Also make sure the frontend displays the providers you want to test in `frontend-env.txt`, for example:

```env
SSO_VISIBLE_PROVIDERS=utahid,entra
```

Do not commit real tenant IDs, client IDs, client secrets, SMTP credentials, or local `.env` values.

## Running the Project

To spin up the stack:
```bash
docker-compose up --build
```

Make sure the `.env` file is present and the certificates are generated before running the above command.

---

For issues or help, reach out to your DevOps or development lead.

