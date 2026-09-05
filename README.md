# SMMS

## Windows local development

The setup script installs Git, Visual Studio Code, Docker Desktop, and the `age`
encryption CLI with `winget`, creates or switches to a developer branch, creates
the ignored local configuration files, starts the SQL Server/API/nginx containers,
verifies API health, installs the recommended VS Code extensions, and opens the
repository.

### First-time setup

1. Clone this repository and open PowerShell in the clone.
2. Run:

   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\setup-local.ps1 -BranchName dev/<developer-name>
   ```

Docker Desktop may require accepting its first-run terms or restarting Windows.
If so, complete that step and run the same command again; the script is
idempotent and preserves existing `.env` and `docker-compose.override.yml`
files.

Use `-SkipInstall` when the required tools are already managed by your IT team.
Use `-SkipBuild` to start previously built images without rebuilding.

### Local URLs

- Admin portal: http://admin.localtest.me:8080
- Aadya tenant: http://aadya.localtest.me:8080
- Public portal: http://ssms.localtest.me:8080
- API health: http://admin.localtest.me:8080/api/health

`*.localtest.me` resolves to `127.0.0.1`; plain `localhost` does not provide the
tenant hostname required by the API. Local SQL data and uploads persist in
Docker volumes. Payment gateways are disabled unless a developer explicitly
adds credentials to the git-ignored `.env` file.

Common commands:

```powershell
docker compose ps
docker compose logs -f smms-api
docker compose down
docker compose down -v # Deletes all local databases and uploads.
```

To copy authorized society data between developer machines, use the encrypted,
validated workflow in [docs/developer-data-transfer.md](docs/developer-data-transfer.md).
