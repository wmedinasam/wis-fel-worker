namespace WisFel.Worker.Data;

public sealed class SaeCommonQuery
{
    private SaeCommonQuery()
    {
    }

    public SaeCommonQuery(SaeCompany company, string customerSql, string productSql)
    {
        ArgumentNullException.ThrowIfNull(company);

        CompanyId = company.Id;
        SaeVersion = company.SaeVersion;
        CustomerSql = RequireQuery(customerSql, nameof(customerSql));
        ProductSql = RequireQuery(productSql, nameof(productSql));
        IsEnabled = true;
    }

    public int Id { get; private set; }

    public int CompanyId { get; private set; }

    public decimal SaeVersion { get; private set; }

    public string CustomerSql { get; private set; } = string.Empty;

    public string ProductSql { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    private static string RequireQuery(string query, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return query;
    }
}
