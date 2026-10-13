using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Data.SqlClient;
using WisFel.Worker.Data;
using WisFel.Worker.Sae;

namespace WisFel.Worker.Tests;

public class SaeQueryTests
{
    [Fact]
    public void QueryTemplateIsScopedByCompanyVersionAndDocumentType()
    {
        var company = new SaeCompany(4.6m, 1);
        var template = new SaeDocumentQuery(
            company,
            "FCAM",
            "SELECT 1 AS [Json]",
            "SELECT 1 AS [Json]",
            "SELECT 1 AS [Json]",
            "SELECT 1 AS [Json]",
            "SELECT N'Name' AS [Name], N'Value' AS [Value]");

        Assert.Equal(company.Id, template.CompanyId);
        Assert.Equal(4.6m, template.SaeVersion);
        Assert.Equal("FCAM", template.DocumentType);
        Assert.True(template.IsEnabled);
    }

    [Fact]
    public void QueryTemplateRequiresUppercaseDocumentTypeAndEverySectionQuery()
    {
        var company = new SaeCompany(4.6m, 1);

        Assert.Throws<ArgumentException>(() => new SaeDocumentQuery(
            company,
            "fcam",
            "SELECT 1",
            "SELECT 1",
            "SELECT 1",
            "SELECT 1",
            "SELECT 1"));

        Assert.Throws<ArgumentException>(() => new SaeDocumentQuery(
            company,
            "FCAM",
            "",
            "SELECT 1",
            "SELECT 1",
            "SELECT 1",
            "SELECT 1"));
    }

    [Fact]
    public void QueryTemplateSchemaHasUniqueCompanyVersionTypeKeyAndLargeSqlColumns()
    {
        using var context = CreateContext();
        var entityType = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(SaeDocumentQuery))!;
        var uniqueIndex = Assert.Single(entityType.GetIndexes(), index => index.IsUnique);

        Assert.Equal(
            new[]
            {
                nameof(SaeDocumentQuery.CompanyId),
                nameof(SaeDocumentQuery.SaeVersion),
                nameof(SaeDocumentQuery.DocumentType)
            },
            uniqueIndex.Properties.Select(property => property.Name));
        Assert.Equal("nvarchar(max)", entityType.FindProperty(nameof(SaeDocumentQuery.DocumentSql))!.GetColumnType());
        Assert.Equal("nvarchar(max)", entityType.FindProperty(nameof(SaeDocumentQuery.IssuerSql))!.GetColumnType());
        Assert.Equal("nvarchar(max)", entityType.FindProperty(nameof(SaeDocumentQuery.ReceiverSql))!.GetColumnType());
        Assert.Equal("nvarchar(max)", entityType.FindProperty(nameof(SaeDocumentQuery.ItemsSql))!.GetColumnType());
        Assert.Equal("nvarchar(max)", entityType.FindProperty(nameof(SaeDocumentQuery.FreeFieldsSql))!.GetColumnType());
    }

    [Fact]
    public void ValuesAreSqlParametersAndNeverInterpolatedIntoQueryText()
    {
        const string internalNumber = "INV-1'; DELETE FROM Documents;--";

        var parameters = SqlSaeQueryExecutor.CreateParameters(
            42,
            internalNumber,
            "A",
            3,
            "fcam");
        var parameterCollection = new SqlCommand().Parameters;
        parameterCollection.AddRange(parameters);

        Assert.Equal(5, parameterCollection.Count);
        Assert.Equal(internalNumber, parameterCollection["@InternalNumber"].Value);
        Assert.Equal("FCAM", parameterCollection["@DocumentType"].Value);
        Assert.Equal(System.Data.SqlDbType.NVarChar, parameterCollection["@InternalNumber"].SqlDbType);
        Assert.Equal(50, parameterCollection["@InternalNumber"].Size);
    }

    private static FelDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FelDbContext>()
            .UseSqlServer("Server=localhost;Database=FEL;Integrated Security=True;TrustServerCertificate=True")
            .Options;

        return new FelDbContext(options);
    }
}
