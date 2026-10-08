param(
    [ValidateSet("Auto", "Sqlite", "Postgres")]
    [string]$Provider = "Auto",
    [string]$OutputDirectory = "",
    [string]$SqlitePath = "",
    [string]$PostgresConnectionString = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if ($Provider -eq "Auto") {
    $Provider = if ($env:Database__Provider) { $env:Database__Provider } else { "Sqlite" }
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root "backups\server"
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"

function Parse-ConnectionString {
    param([string]$Value)
    $map = @{}
    foreach ($part in ($Value -split ";")) {
        if (-not $part.Trim()) { continue }
        $pair = $part -split "=", 2
        if ($pair.Count -eq 2) {
            $map[$pair[0].Trim().ToLowerInvariant()] = $pair[1].Trim()
        }
    }
    return $map
}

if ($Provider -match "^(Postgres|PostgreSQL)$") {
    if (-not $PostgresConnectionString) {
        $PostgresConnectionString = $env:ConnectionStrings__Postgres
    }
    if (-not $PostgresConnectionString) {
        throw "PostgreSQL connection string is required."
    }

    $pgDump = Get-Command pg_dump -ErrorAction SilentlyContinue
    if (-not $pgDump) {
        throw "pg_dump was not found. Install PostgreSQL client tools."
    }

    $cs = Parse-ConnectionString $PostgresConnectionString
    $hostName = $cs["host"]
    $port = if ($cs["port"]) { $cs["port"] } else { "5432" }
    $database = $cs["database"]
    $username = if ($cs["username"]) { $cs["username"] } else { $cs["user id"] }
    $password = $cs["password"]

    if (-not $hostName -or -not $database -or -not $username) {
        throw "Connection string must contain Host, Database and Username."
    }

    $destination = Join-Path $OutputDirectory ("erp_accounting_postgres_" + $stamp + ".dump")
    $oldPassword = $env:PGPASSWORD
    try {
        $env:PGPASSWORD = $password
        $arguments = @(
            "--host=$hostName",
            "--port=$port",
            "--username=$username",
            "--format=custom",
            "--file=$destination",
            $database
        )
        & $pgDump.Source @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "pg_dump failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        $env:PGPASSWORD = $oldPassword
    }

    Write-Host "PostgreSQL backup created:" -ForegroundColor Green
    Write-Host $destination
    exit 0
}

if (-not $SqlitePath) {
    $SqlitePath = Join-Path $root "apps\server_api\data\erp_accounting.db"
}
if (-not (Test-Path $SqlitePath)) {
    throw "SQLite database was not found: $SqlitePath"
}

$destination = Join-Path $OutputDirectory ("erp_accounting_sqlite_" + $stamp + ".db")
$sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue

if ($sqlite) {
    $escapedDestination = $destination.Replace("'", "''")
    & $sqlite.Source $SqlitePath ".backup '$escapedDestination'"
    if ($LASTEXITCODE -ne 0) {
        throw "sqlite3 backup failed with exit code $LASTEXITCODE."
    }
} else {
    $walPath = $SqlitePath + "-wal"
    if (Test-Path $walPath) {
        throw "sqlite3 is not installed and a WAL file is active. Stop the API or install sqlite3 before backup."
    }

    Write-Warning "sqlite3 is not installed. Ensure the API is stopped while the database file is copied."
    Copy-Item -LiteralPath $SqlitePath -Destination $destination -Force
}

Write-Host "SQLite backup created:" -ForegroundColor Green
Write-Host $destination
