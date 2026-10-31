using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// CI-only test executor of the exact SSMS/sqlcmd installer.
// Never commit credentials. Connection is supplied by an environment variable.
if(args.Length!=1||!File.Exists(args[0]))
    throw new ArgumentException("Expected one path to the generated SSMS installer.");
var connection=Environment.GetEnvironmentVariable("IPPCS_INSTALLER_CONNECTION");
if(string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("IPPCS_INSTALLER_CONNECTION is required.");
var builder=new SqlConnectionStringBuilder(connection);
if(!string.Equals(builder.InitialCatalog,"master",StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Installer runner connection must start in database 'master'.");
var sql=await File.ReadAllTextAsync(args[0]);
if(!sql.Contains("CREATE DATABASE [IPPCS_",StringComparison.Ordinal)||
   !sql.Contains("__EFMigrationsHistory",StringComparison.Ordinal))
    throw new InvalidOperationException("Installer does not contain required SQL preflight and migration history.");
// EF-generated SQL follows SSMS GO semantics; split only full standalone GO lines.
var batches=Regex.Split(sql,@"(?im)^[ \t]*GO[ \t]*(?:--[^\r\n]*)?\r?\n");
using var db=new SqlConnection(connection);
await db.OpenAsync();
var done=0;
foreach(var batch in batches)
{
    if(string.IsNullOrWhiteSpace(batch))continue;
    using var cmd=new SqlCommand(batch,db){CommandTimeout=180};
    await cmd.ExecuteNonQueryAsync();
    done++;
}
var expected=Environment.GetEnvironmentVariable("IPPCS_INSTALLER_DATABASE");
if(string.IsNullOrWhiteSpace(expected)||!expected.StartsWith("IPPCS_",StringComparison.Ordinal))
    throw new InvalidOperationException("IPPCS_INSTALLER_DATABASE must name the isolated target database.");
using(var check=new SqlCommand("""
SELECT DB_NAME() AS CurrentDatabase,
    (SELECT COUNT(*) FROM sys.tables WHERE name IN
        (N'AspNetUsers', N'ManufacturingOrders', N'FactoryMasterOptions',
         N'ManufacturingInspectionResults', N'FactoryApsRuns')) AS CriticalTables,
    (SELECT COUNT(*) FROM [__EFMigrationsHistory]) AS Migrations
""",db))
using(var reader=await check.ExecuteReaderAsync())
{
    if(!await reader.ReadAsync() ||
       !string.Equals(reader.GetString(0),expected,StringComparison.OrdinalIgnoreCase) ||
       reader.GetInt32(1)!=5 || reader.GetInt32(2)<10)
        throw new InvalidOperationException("Installer did not provision the expected complete IPPCS schema.");
}
Console.WriteLine($"PASS: executed {done} SSMS SQL batches; verified 5 critical IPPCS tables and 10+ applied migrations in {expected}.");
