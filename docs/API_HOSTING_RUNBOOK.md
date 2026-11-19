# Zarr API hosting runbook (future phase)

**Current state:** no accounting API server is provisioned. This runbook prepares a reproducible container; it does not deploy anything or select a hosting provider.

## Local container smoke test

Prerequisites: Docker Engine/Desktop.

From the repository root:

```bash
docker build -f apps/server_api/Dockerfile -t zarr-api:local .
docker volume create zarr-api-data
docker run --rm --name zarr-api-local \
  -p 8080:8080 \
  -v zarr-api-data:/app/data \
  zarr-api:local
```

In another terminal:

```bash
curl --fail http://localhost:8080/health
```

Expected response reports `status: healthy` and `database: true`. This local smoke test uses a persistent SQLite volume and does not require Supabase or a production license key. Stop with Ctrl+C; the named Docker volume remains.

## Production deployment checklist (do not execute until hosting is approved)

1. Select a Linux VPS/container platform, region, budget, domain and administrator access.
2. Prefer managed PostgreSQL for production. Configure `Database__Provider=Postgres` and `ConnectionStrings__Postgres` as host secrets; do not rely on container-local SQLite for a multi-user deployment.
3. Deploy the image behind a reverse proxy/load balancer with HTTPS. Expose only 443 publicly; keep container port 8080 private to the proxy.
4. Configure trusted forwarded headers only for the actual proxy network before relying on client IPs, HTTPS redirects or secure cookies.
5. Store Supabase URL, publishable key and service-role key in host-side secrets. The service-role key must never reach the Flutter client.
6. Generate a fresh production RSA-3072 signing key on the host/secret manager. Configure `Kaliia__EntitlementPrivateKeyPem` and a new `Kaliia__EntitlementKeyId`; update the Flutter public-key allowlist and ship a client version before enabling signed offline entitlements.
7. Add uptime/health monitoring against `/health`, structured logs, alerts, database backups and a tested restore procedure.
8. Configure rate limits, firewall rules, OS/container patching, least-privilege runtime, secret rotation and an incident/key-revocation runbook.
9. Validate using a test company and test account. Keep `Kaliia:UseCompanyEntitlements` and `Kaliia:EnforceZarrLicense` off until all end-to-end acceptance checks pass.
10. Obtain explicit release approval before enabling enforcement for real companies or distributing production binaries.

## Security notes

- The Docker image runs as the non-root application user and listens on port 8080.
- SQLite is only the default for local container smoke testing; production should use managed PostgreSQL.
- Do not add secrets to Docker build arguments, image layers, source control or logs.
- The `.dockerignore` excludes local data and secret-like files from the build context.
