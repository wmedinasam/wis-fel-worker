using Microsoft.Data.SqlClient;

namespace WisFel.Worker.Data;

public static class SaeConnectionStringFactory
{
    public static string Create(string sharedConnectionString, SaeCompany company)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedConnectionString);
        ArgumentNullException.ThrowIfNull(company);

        var connectionString = new SqlConnectionStringBuilder(sharedConnectionString)
        {
            InitialCatalog = company.DatabaseName
        };

        return connectionString.ConnectionString;
    }
}
