using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using WisFel.Worker.Data;

namespace WisFel.Worker.Tests;

public class DocumentModelTests
{
    [Fact]
    public void StatusesMapToTriggerCompatibleStrings()
    {
        using var context = CreateContext();
        var statusProperty = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(Document))!
            .FindProperty(nameof(Document.Status))!;
        var converter = statusProperty.GetTypeMapping().Converter;

        Assert.Equal("Pending", converter!.ConvertToProvider(DocumentStatus.Pending));
        Assert.Equal("Processing", converter.ConvertToProvider(DocumentStatus.Processing));
        Assert.Equal("Signed", converter.ConvertToProvider(DocumentStatus.Signed));
        Assert.Equal("Delivered", converter.ConvertToProvider(DocumentStatus.Delivered));
        Assert.Equal("Rejected", converter.ConvertToProvider(DocumentStatus.Rejected));
        Assert.Equal("Failed", converter.ConvertToProvider(DocumentStatus.Failed));
    }

    [Fact]
    public void CertificationResultsAreOptionalUntilSigned()
    {
        using var context = CreateContext();
        var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Document))!;

        Assert.True(entityType.FindProperty(nameof(Document.Uuid))!.IsNullable);
        Assert.True(entityType.FindProperty(nameof(Document.Serial))!.IsNullable);
        Assert.True(entityType.FindProperty(nameof(Document.Number))!.IsNullable);
        Assert.True(entityType.FindProperty(nameof(Document.Observations))!.IsNullable);
        Assert.True(entityType.FindProperty(nameof(Document.Request))!.IsNullable);
        Assert.True(entityType.FindProperty(nameof(Document.Response))!.IsNullable);
        Assert.True(entityType.FindProperty(nameof(Document.Pdf))!.IsNullable);
    }

    [Fact]
    public void PayloadsAndPdfMapToExpectedSqlTypes()
    {
        using var context = CreateContext();
        var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Document))!;

        Assert.Equal("nvarchar(max)", entityType.FindProperty(nameof(Document.Request))!.GetColumnType());
        Assert.Equal("nvarchar(max)", entityType.FindProperty(nameof(Document.Response))!.GetColumnType());
        Assert.Equal("varbinary(max)", entityType.FindProperty(nameof(Document.Pdf))!.GetColumnType());
    }

    [Fact]
    public void DocumentHasCompanyLocationTypeInternalIdUniqueIndex()
    {
        using var context = CreateContext();
        var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Document))!;
        var index = Assert.Single(entityType.GetIndexes(), index => index.IsUnique);

        Assert.Equal(
            new[]
            {
                nameof(Document.CompanyId),
                nameof(Document.LocationId),
                nameof(Document.DocumentType),
                nameof(Document.InternalId)
            },
            index.Properties.Select(property => property.Name));
    }

    [Fact]
    public void StatusConstraintContainsAllSupportedValues()
    {
        using var context = CreateContext();
        var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Document))!;
        var statusConstraint = Assert.Single(
            entityType.GetCheckConstraints(),
            constraint => constraint.Name == "CK_Documents_Status");

        Assert.Contains("Pending", statusConstraint.Sql);
        Assert.Contains("Processing", statusConstraint.Sql);
        Assert.Contains("Signed", statusConstraint.Sql);
        Assert.Contains("Delivered", statusConstraint.Sql);
        Assert.Contains("Rejected", statusConstraint.Sql);
        Assert.Contains("Failed", statusConstraint.Sql);
    }

    private static FelDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FelDbContext>()
            .UseSqlServer("Server=localhost;Database=FEL;Integrated Security=True;TrustServerCertificate=True")
            .Options;

        return new FelDbContext(options);
    }
}
