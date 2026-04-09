namespace LiteDbX;

public static class LiteDatabaseMigrationExtensions
{
    public static LiteDbX.Migrations.MigrationRunner Migrations(this ILiteDatabase database)
    {
        return new LiteDbX.Migrations.MigrationRunner(database);
    }
}

