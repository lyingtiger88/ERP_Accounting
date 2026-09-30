# Database Migration Strategy

ERP Accounting supports two server-side EF Core database providers:

- SQLite for zero-setup local development
- PostgreSQL for server/production-style deployment

EF Core tooling generates migrations for the active provider. Because provider-specific migration details can differ, the project will maintain separate migration sets for SQLite and PostgreSQL.

## Current state

The first runnable milestone still uses EF Core `EnsureCreated` so existing development databases continue to work without destructive reset.

Fiscal-year and floating-detail tables are currently added through `AccountingSchemaBootstrapper` using provider-specific `CREATE TABLE/INDEX IF NOT EXISTS` statements. This is a temporary non-destructive compatibility bridge for existing SQLite/PostgreSQL development databases; it does **not** replace the planned reviewed EF Core migration history.

## Planned migration layout

```text
apps/server_api/
  Migrations/
    Sqlite/
    Postgres/
```

Each model change will receive equivalent migrations for both providers.

## Transition plan

1. Freeze the Phase 1 base schema, including fiscal years and floating detail dimensions.
2. Generate the initial SQLite migration set.
3. Generate the initial PostgreSQL migration set.
4. Validate both against fresh databases.
5. Replace `EnsureCreated` with controlled migration application.
6. Add migration checks to CI.
7. Use reviewed SQL scripts or migration bundles for production deployment.

Existing test databases created with `EnsureCreated` will be migrated or reset only after an explicit backup/reset step.
