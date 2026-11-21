#!/usr/bin/env python3
"""Compose one SQL Server/SSMS installer from the EF Core generated idempotent SQL.

Zero database connections. NEVER synthesizes application tables by hand.
All source DDL, indexes, foreign keys, identity and seed records remain
exactly the SQL emitted by the committed EF Core migration assembly.
"""
import argparse
import hashlib
import pathlib
import re
import sys

NAME = re.compile(r"^IPPCS_[A-Za-z0-9_]{1,54}$", flags=re.ASCII)
BASELINE = "20261026184038_CiSchemaSmoke"
CURRENT_CATALOG = "20261027221500_StageThirtySevenApsReasonCatalog"


def generate(source: pathlib.Path, output: pathlib.Path, database: str) -> str:
    if not NAME.fullmatch(database):
        raise ValueError("Database must be named IPPCS_... (ASCII letters, digits or underscore).")
    if source.resolve() == output.resolve():
        raise ValueError("Source and output must be distinct files.")
    raw = source.read_text(encoding="utf-8-sig")
    if not raw.strip() or BASELINE not in raw or CURRENT_CATALOG not in raw:
        raise ValueError("EF migration SQL is missing required baseline or current migration.")
    if "__EFMigrationsHistory" not in raw or "GO" not in raw.splitlines():
        raise ValueError("Expected complete SQL Server idempotent migration output.")
    if "IF NOT EXISTS" not in raw.upper():
        raise ValueError("Expected EF Core idempotent migration guards.")
    # No GO directives inside a guard; each preliminary statement runs in its
    # own SSMS/sqlcmd batch, followed by unmodified EF's SQL.
    prefix = f"""-- IPPCS initial / versioned SQL Server installation.
-- GENERATED FROM REVIEWED EF CORE MIGRATIONS; DO NOT EDIT TABLES BY HAND.
-- Database: {database}
-- Source migration SQL SHA256: {hashlib.sha256(raw.encode('utf-8')).hexdigest()}
-- Run in SSMS Query Editor with SQLCMD batch delimiter GO, or via sqlcmd.
-- Intended ONLY for an isolated IPPCS_ database, not any existing ERP DB.
-- No account password, shared ERP records, warehouse, HR or sales data.
-- Before applying to an existing IPPCS_ database, take a verified backup.
USE [master];
GO
IF DB_ID(N'{database}') IS NULL
BEGIN
    EXEC(N'CREATE DATABASE [{database}]');
END;
GO
USE [{database}];
GO
IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NULL
   AND EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0)
BEGIN
    THROW 51100, 'This database has user tables but no EF history. Refusing to overwrite an unrecognized schema.', 1;
END;
GO
-- All subsequent batches are generated, additive/idempotent EF migration SQL.
"""
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(prefix + raw.lstrip("\ufeff"), encoding="utf-8-sig")
    return hashlib.sha256(output.read_bytes()).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description="Build a standalone IPPCS EF SQL installer")
    parser.add_argument("--source", required=True, type=pathlib.Path)
    parser.add_argument("--output", required=True, type=pathlib.Path)
    parser.add_argument("--database", default="IPPCS_Dev")
    args = parser.parse_args()
    try:
        sha = generate(args.source, args.output, args.database)
    except (ValueError, OSError) as ex:
        print(f"ERROR: {ex}", file=sys.stderr)
        return 2
    print(f"PASS: {args.output} (SHA256 {sha})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
