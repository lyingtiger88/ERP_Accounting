param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,
    [ValidateSet("Auto", "Sqlite", "Postgres")]
    [string]$Provider = "Auto",
    [string]$SqlitePath = "",
    [string]$PostgresConnectionString = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path $BackupPath)) {
    throw "Backup file was not found: $BackupPath"
}

if ($Provider -eq "Auto") {
    $Provider = if ($BackupPath.ToLowerInvariant().EndsWith(".dump")) { "Postgres" } else { "Sqlite" }
}

if (-not $Force) {
    $answer = Read-Host "Restore will replace server data. Type RESTORE to continue"
    if ($answer -ne "RESTORE") {
        Write-Host "Restore cancelled."
        exit 1
    }
}

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

    $pgRestore = Get-Command pg_restore -ErrorAction SilentlyContinue
    if (-not $pgRestore) {
        throw "pg_restore was not found. Install PostgreSQL client tools."
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

    $oldPassword = $env:PGPASSWORD
    try {
        $env:PGPASSWORD = $password
        $arguments = @(
            "--host=$hostName",
            "--port=$port",
            "--username=$username",
            "--dbname=$database",
            "--clean",
            "--if-exists",
            "--no-owner",
            $BackupPath
        )
        & $pgRestore.Source @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "pg_restore failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        $env:PGPASSWORD = $oldPassword
    }

    Write-Host "PostgreSQL restore completed." -ForegroundColor Green
    exit 0
}

if (-not $SqlitePath) {
    $SqlitePath = Join-Path $root "apps\server_api\data\erp_accounting.db"
}

$sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue
if ($sqlite) {
    $integrity = (& $sqlite.Source $BackupPath "PRAGMA integrity_check;") -join ""
    if ($integrity.Trim().ToLowerInvariant() -ne "ok") {
        throw "SQLite backup failed integrity_check: $integrity"
    }
} else {
    Write-Warning "sqlite3 is not installed; integrity_check cannot be performed by this script."
}

$directory = Split-Path -Parent $SqlitePath
New-Item -ItemType Directory -Force -Path $directory | Out-Null

$safety = $SqlitePath + ".pre_restore_" + (Get-Date -Format "yyyyMMdd_HHmmss")
if (Test-Path $SqlitePath) {
    Copy-Item -LiteralPath $SqlitePath -Destination $safety -Force
}

try {
    Copy-Item -LiteralPath $BackupPath -Destination $SqlitePath -Force
}
catch {
    if (Test-Path $safety) {
        Copy-Item -LiteralPath $safety -Destination $SqlitePath -Force
    }
    throw
}

Write-Host "SQLite restore completed." -ForegroundColor Green
if (Test-Path $safety) {
    Write-Host "Safety copy retained at:"
    Write-Host $safety
}
Write-Warning "Restart the API after restore."
