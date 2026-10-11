namespace ETFInvestment.Data.SQLite;
public static class DatabasePaths
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ETFInvestment");
    public static string Market => Path.Combine(DirectoryPath,"market.db");
    public static string Portfolio => Path.Combine(DirectoryPath,"portfolio.db");
    public static void EnsureDirectory() => Directory.CreateDirectory(DirectoryPath);
}
