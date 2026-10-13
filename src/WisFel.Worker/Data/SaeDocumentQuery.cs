namespace WisFel.Worker.Data;

public sealed class SaeDocumentQuery
{
    private SaeDocumentQuery()
    {
    }

    public SaeDocumentQuery(
        SaeCompany company,
        string documentType,
        string documentSql,
        string issuerSql,
        string itemsSql,
        string freeFieldsSql)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (string.IsNullOrWhiteSpace(documentType) ||
            documentType.Length > 10 ||
            documentType != documentType.Trim().ToUpperInvariant())
        {
            throw new ArgumentException(
                "Document type must be a non-empty uppercase code of at most 10 characters.",
                nameof(documentType));
        }

        CompanyId = company.Id;
        SaeVersion = company.SaeVersion;
        DocumentType = documentType;
        DocumentSql = RequireQuery(documentSql, nameof(documentSql));
        IssuerSql = RequireQuery(issuerSql, nameof(issuerSql));
        ItemsSql = RequireQuery(itemsSql, nameof(itemsSql));
        FreeFieldsSql = RequireQuery(freeFieldsSql, nameof(freeFieldsSql));
        IsEnabled = true;
    }

    public int Id { get; private set; }

    public int CompanyId { get; private set; }

    public decimal SaeVersion { get; private set; }

    public string DocumentType { get; private set; } = string.Empty;

    public string DocumentSql { get; private set; } = string.Empty;

    public string IssuerSql { get; private set; } = string.Empty;

    public string ItemsSql { get; private set; } = string.Empty;

    public string FreeFieldsSql { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    private static string RequireQuery(string query, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return query;
    }
}
