using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text.Json.Nodes;
using WisFel.Worker.Data;
using WisFel.Worker.Payloads;

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

    [Fact]
    public void RequestBuilderEmitsRootDocumentWithRequiredNodes()
    {
        var json = DocumentRequestJsonBuilder.Serialize(new DocumentRequest());
        var root = JsonNode.Parse(json)!.AsObject();
        var document = root["Document"]!.AsObject();

        Assert.Single(root);
        Assert.NotNull(document["Issuer"]!.AsObject());
        Assert.NotNull(document["Receiver"]!.AsObject());
        Assert.Empty(document["Items"]!.AsArray());
        Assert.Empty(document["FreeFields"]!.AsObject());
    }

    [Fact]
    public void RequestBuilderPreservesItemsAndSerializesFreeFieldsAsObject()
    {
        var request = new DocumentRequest
        {
            Document = new DocumentPayload
            {
                Issuer = new JsonObject { ["TaxId"] = "ISSUER-1" },
                Receiver = new JsonObject { ["TaxId"] = "RECEIVER-1" },
                Items =
                [
                    new JsonObject
                    {
                        ["Description"] = "First item",
                        ["Quantity"] = 2
                    },
                    new JsonObject
                    {
                        ["Description"] = "Second item",
                        ["Quantity"] = 1
                    }
                ],
                FreeFields = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal)
                {
                    ["Z-Reference"] = JsonValue.Create("last"),
                    ["A-Reference"] = JsonValue.Create("first")
                }
            }
        };

        var json = DocumentRequestJsonBuilder.Serialize(request);
        var document = JsonNode.Parse(json)!["Document"]!.AsObject();

        Assert.Equal(2, document["Items"]!.AsArray().Count);
        Assert.Equal("ISSUER-1", document["Issuer"]!["TaxId"]!.GetValue<string>());
        Assert.Equal("RECEIVER-1", document["Receiver"]!["TaxId"]!.GetValue<string>());
        Assert.Equal("first", document["FreeFields"]!["A-Reference"]!.GetValue<string>());
        Assert.Equal("last", document["FreeFields"]!["Z-Reference"]!.GetValue<string>());
        Assert.Equal(json, DocumentRequestJsonBuilder.Serialize(request));
    }

    [Fact]
    public void RequestBuilderRejectsNullItems()
    {
        var request = new DocumentRequest
        {
            Document = new DocumentPayload
            {
                Items = [null!]
            }
        };

        Assert.Throws<ArgumentException>(() => DocumentRequestJsonBuilder.Serialize(request));
    }

    private static FelDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FelDbContext>()
            .UseSqlServer("Server=localhost;Database=FEL;Integrated Security=True;TrustServerCertificate=True")
            .Options;

        return new FelDbContext(options);
    }
}
