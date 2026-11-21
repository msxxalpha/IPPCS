"""Source-only regression for the one-file SQL installer composer."""
import pathlib
import tempfile
import unittest
import importlib.util

SCRIPT = pathlib.Path(__file__).resolve().parents[1] / "scripts" / "build_ippcs_install_sql.py"
SPEC = importlib.util.spec_from_file_location("ippcs_sql_composer", SCRIPT)
mod = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(mod)

FAKE_EF = """IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] ([MigrationId] nvarchar(150) NOT NULL);
END;
GO
IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId]=N'20261026184038_CiSchemaSmoke')
BEGIN
 SELECT 1;
END;
GO
IF NOT EXISTS(SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId]=N'20261027221500_StageThirtySevenApsReasonCatalog')
BEGIN
 SELECT 2;
END;
GO
"""


class SqlInstallerTests(unittest.TestCase):
    def test_generation_is_stable_and_guards_database_name_and_legacy_tables(self):
        with tempfile.TemporaryDirectory() as folder:
            root = pathlib.Path(folder)
            source, target = root / "migrations.sql", root / "IPPCS_Dev.sql"
            source.write_text(FAKE_EF, encoding="utf-8")
            first = mod.generate(source, target, "IPPCS_Dev")
            second = mod.generate(source, target, "IPPCS_Dev")
            self.assertEqual(first, second)
            sql = target.read_text(encoding="utf-8-sig")
            self.assertIn("CREATE DATABASE [IPPCS_Dev]", sql)
            self.assertIn("USE [IPPCS_Dev];", sql)
            self.assertIn("THROW 51100", sql)
            self.assertEqual(sql.count("20261026184038_CiSchemaSmoke"), 1)
            self.assertIn(FAKE_EF, sql)
            self.assertEqual(target.read_bytes()[:3], b"\xef\xbb\xbf")

    def test_sql_injection_and_unrelated_databases_are_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = pathlib.Path(folder)
            source, target = root / "source.sql", root / "target.sql"
            source.write_text(FAKE_EF, encoding="utf-8")
            for invalid in ["master", "CompanyERP", "IPPCS_;DROP DATABASE X",
                            "IPPCS_Test] ; DROP TABLE Users", "IPPCS_نام"]:
                with self.subTest(invalid=invalid):
                    with self.assertRaises(ValueError):
                        mod.generate(source, target, invalid)
            self.assertFalse(target.exists())

    def test_uncommitted_incomplete_migration_sequence_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = pathlib.Path(folder)
            source, target = root / "source.sql", root / "target.sql"
            source.write_text("IF NOT EXISTS...\nGO\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                mod.generate(source, target, "IPPCS_Dev")
            self.assertFalse(target.exists())


if __name__ == "__main__":
    unittest.main()
