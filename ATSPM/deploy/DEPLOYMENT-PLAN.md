# ATSPM Windows Server Deployment Plan

## Overview

Deploy ATSPM on a Windows Server using IIS in-process hosting for the .NET APIs, IIS URL Rewrite as a reverse proxy for the Next.js WebUI only, NSSM to run the Next.js WebUI as a Windows Service, and Windows Task Scheduler for scheduled jobs. SQL Server is assumed to be pre-existing and accessible from the server.

ATSPM application binaries are published from source by the separate ATSPM-Windows-Installer repository, which owns packaging, server installation, update, rollback, and uninstall behavior. Because multiple ATSPM repositories may exist side by side, the installer build must allow the source repository to be selected explicitly. An operator edits one settings file, provides sensitive values interactively, then runs a single PowerShell script to install or update.

---

## Repository Responsibilities

### ATSPM Repository

- Owns application source code for ConfigApi, DataApi, ReportApi, IdentityApi, WebUI, DatabaseInstaller, WatchDog, and EventLogUtility
- Defines runtime configuration contract required by the installer package
- Does not own server provisioning logic, IIS setup, Windows Service registration, or Task Scheduler registration
- Serves as the selectable source repository for installer packaging

### ATSPM-Windows-Installer Repository

- Owns source-based publish and packaging into the Windows deployment bundle
- Owns Install-Prerequisites, Install, Update, and Uninstall scripts
- Owns IIS, NSSM, Task Scheduler, and service-account setup
- Owns rollback, backup, and operator workflow on the target server

### Release Contract Between Repositories

- ATSPM-Windows-Installer must be pointed at the intended ATSPM source repository during packaging
- The selected ATSPM source repository must contain the expected `Atspm` solution layout
- Any new deployable component, required environment variable, or install-time prerequisite must be reflected in both repositories before release

---

## Architecture

```
Internet
    │
    ▼
IIS (port 443, HTTPS)
    │
    ├── /config/*   ──► ConfigApi   (in-process, IIS app pool ATSPM-ConfigApi)
    ├── /data/*     ──► DataApi     (in-process, IIS app pool ATSPM-DataApi)
    ├── /report/*   ──► ReportApi   (in-process, IIS app pool ATSPM-ReportApi)
    ├── /identity/* ──► IdentityApi (in-process, IIS app pool ATSPM-IdentityApi)
    └── /*          ──► URL Rewrite → WebUI Node (localhost:3000)

Windows Task Scheduler
    ├── ATSPM-WatchDog      (daily 7:00 AM)
    ├── ATSPM-Aggregation   (daily 1:00 AM)
    └── ATSPM-EventLogPull  (every 15 min)
```

- **4 .NET 8 APIs** (ConfigApi, DataApi, ReportApi, IdentityApi) run **in-process** under IIS app pools using the ASP.NET Core Hosting Bundle — no separate Kestrel ports, no ARR module required
- **IIS URL Rewrite** catch-all rule proxies unmatched requests to the Node.js WebUI at localhost:3000 (ARR is not required for the APIs; only URL Rewrite is needed)
- **Next.js WebUI** runs as a standalone Node.js process managed by NSSM as a Windows Service
- **WatchDog** and **EventLogUtility** run as CLI tools via Windows Task Scheduler under a dedicated **ATSPM service account**
- **DatabaseInstaller** is a one-shot CLI that applies EF Core migrations on install and update

---

## Deployment Package Structure

```
ATSPM-v{version}-deploy.zip
├── Build.ps1                    # Installer repo: assembles the final ZIP from released artifacts
├── Install-Prerequisites.ps1   # Operator: installs IIS, .NET, Node, URL Rewrite
├── Install.ps1                  # Operator: first-time install on server
├── Update.ps1                   # Operator: upgrade an existing install
├── Uninstall.ps1                # Operator: fully removes the installation
├── config/
│   ├── settings.ps1             # Non-sensitive configuration — edit before running
│   └── settings-sensitive.ps1  # Sensitive secrets — generated interactively, never committed
├── publish/                     # populated from released ATSPM application artifacts
│   ├── ConfigApi/
│   ├── DataApi/
│   ├── ReportApi/
│   ├── IdentityApi/
│   ├── DatabaseInstaller/
│   ├── WatchDog/
│   ├── EventLogUtility/
│   └── WebUI/                   # Next.js .next/standalone output
└── tools/
    └── nssm.exe                 # NSSM service manager (bundled)
```

The ZIP above is produced by ATSPM-Windows-Installer from the selected ATSPM source repository.

---

## Configuration

### `config/settings.ps1` — Non-Sensitive Settings

The operator edits this file before running any script. All scripts dot-source it.

| Setting | Description |
|---|---|
| `$InstallRoot` | Root install path on server (default `C:\inetpub\atspm`) |
| `$BackupRoot` | Where Update.ps1 stores backups (default `C:\inetpub\atspm\backups`) |
| `$NodePath` | Full path to `node.exe` on the server |
| `$IisSiteName` | IIS website name |
| `$IisHostname` | Public hostname (e.g. `atspm.yourdomain.com`) |
| `$IisPort` | HTTPS port (443) |
| `$SslCertThumb` | Thumbprint of SSL cert already in `LocalMachine\My` |
| `$PortWebUI` | Node.js listen port (3000) |
| `$JwtIssuer` / `$JwtExpireDays` | JWT issuer string and token lifetime |
| `$AdminEmail` / `$AdminRole` | Seed admin user email and role (first install only) |
| `$SmtpHost` / `$SmtpPort` / `$SmtpEnableSsl` | SMTP server settings (non-credential) |
| `$WatchdogEmailTo` / `$WatchdogWeekdayOnly` | WatchDog report email target |
| `$MapLat` / `$MapLon` / `$MapTileLayer` | Default map center and tile source for WebUI |
| `$MapAttribution` / `$PoweredByImage` | Map attribution and optional logo URL |
| `$WatchdogScheduleTime` | Daily run time for WatchDog (default `07:00`) |
| `$AggregationScheduleTime` | Daily run time for Aggregation (default `01:00`) |
| `$EventLogIntervalMins` | Polling interval for event log pull (default `15`) |
| `$ServiceAccountName` | Local account for Task Scheduler jobs (default `ATSPM-Svc`) |

### `config/settings-sensitive.ps1` — Secrets (Never Committed)

This file is **not included in the ZIP** and **must not be committed to source control**. It is generated on the server by running `Install.ps1` or explicitly by calling `New-SensitiveConfig.ps1`.

All secrets are collected via `Read-Host -AsSecureString` and written to this file using DPAPI encryption (`ConvertFrom-SecureString` with no key — machine/user-scoped). The file is readable only by the account that created it.

| Setting | Description |
|---|---|
| `$ConnConfig` … `$ConnIdentity` | SQL Server connection strings for all 4 EF contexts |
| `$JwtKey` | JWT signing secret (32+ chars) |
| `$AdminPassword` | Seed admin password (first install only) |
| `$SmtpUserName` / `$SmtpPassword` | SMTP credentials |
| `$ServiceAccountPassword` | Password for the ATSPM service account |

> If `settings-sensitive.ps1` does not exist when a script runs, the script prompts for all values interactively and generates the file automatically.

---

## Scripts

### ATSPM Release Artifacts — Developer/CI

Run from the ATSPM-Windows-Installer repository or CI pipeline. Produces the application publish output from the selected ATSPM source repository.

**Steps:**
1. Resolve the intended source repository via `-SourceRoot`; if multiple sibling ATSPM repositories exist, require explicit selection
2. Read version from `ConfigApi.csproj`
3. `dotnet publish` each of the 7 .NET projects (`--runtime win-x64`, framework-dependent by default)
4. `npm run build` in `WebUI/`; copy `.next/standalone/` + static assets to `publish/WebUI/`
5. Stage the published output for packaging by ATSPM-Windows-Installer

**Parameters:**
- `-SourceRoot` — ATSPM repository root or `Atspm` solution root to package from
- `-SelfContained` — publish self-contained (no .NET Hosting Bundle required on server)
- `-OutputDir` — where to write the deployment ZIP

---

### `Install-Prerequisites.ps1` — Prerequisite Installation

Run as Administrator before `Install.ps1` if the server has not been prepared. This script is idempotent — safe to run multiple times.

**Steps:**

1. **IIS and Windows features**
   ```powershell
   Enable-WindowsOptionalFeature -Online -FeatureName `
     IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures,
     IIS-HttpRedirect, IIS-ApplicationDevelopment, IIS-NetFxExtensibility45,
     IIS-ASPNET45, IIS-ISAPIExtensions, IIS-ISAPIFilter,
     IIS-Security, IIS-RequestFiltering, IIS-StaticContent,
     IIS-ManagementConsole -All
   ```

2. **IIS URL Rewrite module** — download from Microsoft and install silently via `msiexec`

3. **.NET 8 ASP.NET Core Hosting Bundle** — download from `dotnet.microsoft.com` and install silently

4. **Node.js 20 LTS** — download from `nodejs.org` and install silently via `.msi`; update `$NodePath` in `settings.ps1` to match

5. **Post-install verification** — confirms each component is present and prints a summary

> ARR (Application Request Routing) is **not required**. The .NET APIs use in-process IIS hosting; only URL Rewrite is needed for the Node.js proxy.

---

### `Install.ps1` — First-Time Installation

Run as Administrator from the extracted ZIP folder on the target server.

On any failure after Step 2, the script runs `Invoke-Rollback` to undo all changes made in the current run before exiting.

**Steps:**

**Step 1 — Load configuration**
- Dot-source `config/settings.ps1`
- If `config/settings-sensitive.ps1` does not exist, prompt for all sensitive values interactively (`Read-Host -AsSecureString`) and write the encrypted file

**Step 2 — Prerequisites check**
- IIS (W3SVC) is installed
- IIS URL Rewrite module present (`rewrite.dll`)
- .NET 8 ASP.NET Core runtime present (`dotnet --list-runtimes`)
- Node.js present at `$NodePath`
- SQL Server reachable (test connection to the Config DB server)
- If any check fails: print guidance to run `Install-Prerequisites.ps1` and exit (no rollback needed — nothing was changed)

**Step 3 — Create service account**
- Create local Windows user `$ServiceAccountName` with the password from `settings-sensitive.ps1`
- Grant **Log on as a service** right via `secedit`
- Grant **read/execute** on `$InstallRoot` to the service account

**Step 4 — Create install directories**
- Creates `$InstallRoot\{ConfigApi,DataApi,ReportApi,IdentityApi,DatabaseInstaller,WatchDog,EventLogUtility,WebUI}`
- *Rollback: remove `$InstallRoot`*

**Step 5 — Copy application files**
- Copies `publish\{name}\*` into each install directory
- Writes `frontend-env.txt` into `WebUI\` with routing URLs and map settings from `settings.ps1`

**Step 6 — Database migrations**
```
DatabaseInstaller.exe update
    --provider SqlServer
    --config-connection      <ConnConfig>
    --aggregation-connection <ConnAggregation>
    --eventlog-connection    <ConnEventLog>
    --identity-connection    <ConnIdentity>
    --seed-admin
    --admin-email    <AdminEmail>
    --admin-password <AdminPassword>
    --admin-role     <AdminRole>
```
- *Rollback: no DB rollback (EF migrations are not auto-reversed); operator is warned that the DB may need manual cleanup*

**Step 7 — Configure IIS (in-process hosting)**
- Create 4 app pools (No Managed Code): `ATSPM-ConfigApi`, `ATSPM-DataApi`, `ATSPM-ReportApi`, `ATSPM-IdentityApi`
- Create IIS website `$IisSiteName` bound to `$IisHostname:443` with SSL cert from `$SslCertThumb`
- Register each API as an IIS Application under the site using the **ASP.NET Core Module** (in-process):
  - `/config` → `$InstallRoot\ConfigApi`
  - `/data` → `$InstallRoot\DataApi`
  - `/report` → `$InstallRoot\ReportApi`
  - `/identity` → `$InstallRoot\IdentityApi`
- Set per-app-pool environment variables for each API (connection strings, JWT settings, `ASPNETCORE_ENVIRONMENT=Production`)
- Add URL Rewrite catch-all rule on the root site forwarding `/*` to `http://localhost:$PortWebUI` (for Node.js WebUI only)
- *Rollback: remove IIS site and all 4 app pools*

**Step 8 — WebUI Windows Service (NSSM)**
```
nssm install ATSPM-WebUI <NodePath> server.js
nssm set     ATSPM-WebUI AppDirectory  <InstallRoot>\WebUI
nssm set     ATSPM-WebUI AppEnvironmentExtra "PORT=3000" "NODE_ENV=production"
nssm set     ATSPM-WebUI Start SERVICE_AUTO_START
```
- *Rollback: `nssm remove ATSPM-WebUI confirm`*

**Step 9 — Task Scheduler jobs (service account)**

All three tasks run under `$ServiceAccountName` (not SYSTEM). Connection strings and SMTP credentials are passed as **user-level environment variables scoped to the service account**, not machine-level.

| Task | Executable | Arguments | Schedule |
|---|---|---|---|
| `ATSPM-WatchDog` | `WatchDog.exe` | `generate --defaultEmailAddress <email> --weekdayOnly <bool>` | Daily at `$WatchdogScheduleTime` |
| `ATSPM-Aggregation` | `EventLogUtility.exe` | `aggregate` | Daily at `$AggregationScheduleTime` |
| `ATSPM-EventLogPull` | `EventLogUtility.exe` | `log` | Every `$EventLogIntervalMins` minutes |

- *Rollback: unregister all 3 tasks*

**Step 10 — Health checks**
- Start all app pools
- HTTP probe each API at `https://$IisHostname/config/health`, `/data/health`, `/report/health`, `/identity/health`
- HTTP probe WebUI at `http://localhost:3000`

---

### `Update.ps1` — Upgrade Existing Installation

Run as Administrator from the extracted new-version ZIP folder.

**Steps:**

1. **Load configuration** — same as Install.ps1 Step 1; prompt for secrets if `settings-sensitive.ps1` is missing
2. **Stop everything** — stop app pools, `ATSPM-WebUI` NSSM service, disable all 3 Task Scheduler tasks
3. **Backup** — copy current `$InstallRoot\{app}\` to `$BackupRoot\{timestamp}\`
4. **Copy new files** — overwrite each install directory from `publish\`
5. **Run migrations** — `DatabaseInstaller.exe update` (same as install but without `--seed-admin`)
   - On migration failure: automatically restore from backup and restart services; abort with error
6. **Restart** — start app pools, NSSM service, re-enable scheduled tasks
7. **Health checks** — same HTTP probes as install; warn (do not abort) if any fail

---

### `Uninstall.ps1` — Full Removal

Run as Administrator to completely remove the ATSPM installation.

**Steps:**

1. **Stop and remove NSSM service** — `nssm stop ATSPM-WebUI`, `nssm remove ATSPM-WebUI confirm`
2. **Remove Task Scheduler tasks** — unregister `ATSPM-WatchDog`, `ATSPM-Aggregation`, `ATSPM-EventLogPull`
3. **Remove IIS site and app pools** — remove website `$IisSiteName`; remove app pools `ATSPM-ConfigApi`, `ATSPM-DataApi`, `ATSPM-ReportApi`, `ATSPM-IdentityApi`
4. **Remove service account** — remove local user `$ServiceAccountName`
5. **Remove install directory** — prompt for confirmation before deleting `$InstallRoot`
6. **Remove environment variables** — remove any ATSPM-prefixed user-level env vars set for the service account
7. **Report** — list what was removed and note that the SQL Server databases were **not** dropped (must be removed manually if desired)

---

## Included Components

| Component | Type | Notes |
|---|---|---|
| ConfigApi | IIS in-process | |
| DataApi | IIS in-process | |
| ReportApi | IIS in-process | |
| IdentityApi | IIS in-process | Includes SMTP config |
| WebUI | NSSM Windows Service (port 3000) | Next.js standalone |
| DatabaseInstaller | CLI (install + update) | EF Core migrations for all 4 DBs |
| WatchDog | Task Scheduler (daily) | Generates alert reports |
| EventLogUtility | Task Scheduler (log + aggregate) | Event pull + aggregation |

**Excluded:** DeviceEmulator (lab/testing only), all test projects.

---

## Prerequisites on Target Server

- Windows Server 2016 or later
- IIS with **URL Rewrite** module installed (ARR is not required)
- **.NET 8 ASP.NET Core Hosting Bundle** (or self-contained publish via `-SelfContained`)
- **Node.js 20 LTS**
- **SQL Server** instance accessible from the server (any edition; can be remote)
- An SSL certificate in `LocalMachine\My` with its thumbprint recorded in `settings.ps1`

All of the above can be installed by running `Install-Prerequisites.ps1`.

---

## Environment Variables Reference

### Per-App-Pool (APIs via IIS — set on the app pool, not machine-level)

| Variable | Value |
|---|---|
| `ConnectionStrings__ConfigContext__ConnectionString` | SQL connection string |
| `ConnectionStrings__ConfigContext__Provider` | `SqlServer` |
| `ConnectionStrings__AggregationContext__ConnectionString` | SQL connection string |
| `ConnectionStrings__AggregationContext__Provider` | `SqlServer` |
| `ConnectionStrings__EventLogContext__ConnectionString` | SQL connection string |
| `ConnectionStrings__EventLogContext__Provider` | `SqlServer` |
| `ConnectionStrings__IdentityContext__ConnectionString` | SQL connection string |
| `ConnectionStrings__IdentityContext__Provider` | `SqlServer` |
| `Jwt__Key` | Secret key (32+ chars) |
| `Jwt__Issuer` | Token issuer string |
| `Jwt__ExpireDays` | Token lifetime in days |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

**IdentityApi app pool only:**

| Variable | Value |
|---|---|
| `EmailConfiguration__SmtpEmailService__Host` | SMTP server hostname |
| `EmailConfiguration__SmtpEmailService__Port` | SMTP port |
| `EmailConfiguration__SmtpEmailService__UserName` | SMTP username |
| `EmailConfiguration__SmtpEmailService__Password` | SMTP password |
| `EmailConfiguration__SmtpEmailService__EnableSsl` | `true` / `false` |

### Service Account User-Level (CLI tools via Task Scheduler)

The same connection strings and SMTP vars are set as **user-level** environment variables on `$ServiceAccountName` via:

```powershell
[System.Environment]::SetEnvironmentVariable($name, $value, "User")
# run in the context of $ServiceAccountName using Start-Process / runas
```

This scopes the secrets to the service account only — they are not readable by arbitrary processes running as other accounts.

---

## Post-Install Verification

1. `https://{hostname}/config/swagger` — ConfigApi Swagger UI
2. `https://{hostname}/data/swagger` — DataApi Swagger UI
3. `https://{hostname}/report/swagger` — ReportApi Swagger UI
4. `https://{hostname}/identity/swagger` — IdentityApi Swagger UI
5. `https://{hostname}` — WebUI loads, login with seeded admin account works
6. Run `Update.ps1` after a version bump — confirm migrations ran, backup created, services restarted
7. Check Task Scheduler — confirm all 3 ATSPM tasks are registered, enabled, and running under `$ServiceAccountName`
