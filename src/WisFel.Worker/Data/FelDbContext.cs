using Microsoft.EntityFrameworkCore;

namespace WisFel.Worker.Data;

public sealed class FelDbContext(DbContextOptions<FelDbContext> options) : DbContext(options)
{
    public DbSet<SaeCompany> SaeCompanies => Set<SaeCompany>();

    public DbSet<SaeDocumentQuery> SaeDocumentQueries => Set<SaeDocumentQuery>();

    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SaeCompany>(entity =>
        {
            entity.ToTable("SaeCompanies", table =>
            {
                table.HasCheckConstraint(
                    "CK_SaeCompanies_SaeVersion",
                    "[SaeVersion] >= 4.0 AND [SaeVersion] * 10 = FLOOR([SaeVersion] * 10)");
                table.HasCheckConstraint(
                    "CK_SaeCompanies_CompanyNumber",
                    "[CompanyNumber] BETWEEN 1 AND 99");
                table.HasCheckConstraint(
                    "CK_SaeCompanies_LicenseTimestamp",
                    "[IsLicensed] = 0 OR [LicenseUpdatedAt] IS NOT NULL");
            });

            entity.HasKey(company => company.Id);
            entity.Property(company => company.SaeVersion).HasPrecision(18, 1);
            entity.Property(company => company.IsLicensed).HasDefaultValue(false);
            entity.HasIndex(company => new { company.SaeVersion, company.CompanyNumber }).IsUnique();
        });

        modelBuilder.Entity<SaeDocumentQuery>(entity =>
        {
            entity.ToTable("SaeDocumentQueries", table =>
            {
                table.HasCheckConstraint(
                    "CK_SaeDocumentQueries_SaeVersion",
                    "[SaeVersion] >= 4.0 AND [SaeVersion] * 10 = FLOOR([SaeVersion] * 10)");
                table.HasCheckConstraint(
                    "CK_SaeDocumentQueries_DocumentType",
                    "LEN([DocumentType]) > 0 AND [DocumentType] = UPPER([DocumentType])");
            });

            entity.HasKey(query => query.Id);
            entity.Property(query => query.SaeVersion).HasPrecision(18, 1);
            entity.Property(query => query.DocumentType).HasMaxLength(10).IsRequired();
            entity.Property(query => query.DocumentSql).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(query => query.IssuerSql).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(query => query.ReceiverSql).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(query => query.ItemsSql).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(query => query.FreeFieldsSql).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(query => query.IsEnabled).HasDefaultValue(true);
            entity.HasOne<SaeCompany>()
                .WithMany()
                .HasForeignKey(query => query.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(query => new { query.CompanyId, query.SaeVersion, query.DocumentType })
                .IsUnique();
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("Documents", table =>
            {
                table.HasCheckConstraint(
                    "CK_Documents_Status",
                    "[Status] IN (N'Pending', N'Processing', N'Signed', N'Delivered', N'Rejected', N'Failed')");
            });

            entity.HasKey(document => document.Id);
            entity.Property(document => document.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(DocumentStatus.Pending);
            entity.Property(document => document.InternalNumber).HasMaxLength(50).IsRequired();
            entity.Property(document => document.InternalSerial).HasMaxLength(20).IsRequired();
            entity.Property(document => document.DocumentType).HasMaxLength(10).IsRequired();
            entity.Property(document => document.Uuid).HasColumnName("UUID").HasMaxLength(36);
            entity.Property(document => document.Serial).HasMaxLength(20);
            entity.Property(document => document.Number).HasMaxLength(20);
            entity.Property(document => document.Observations).HasColumnType("nvarchar(max)");
            entity.Property(document => document.Request).HasColumnType("nvarchar(max)");
            entity.Property(document => document.Response).HasColumnType("nvarchar(max)");
            entity.Property(document => document.Pdf).HasColumnType("varbinary(max)");

            entity.HasOne(document => document.Company)
                .WithMany()
                .HasForeignKey(document => document.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(document => new
            {
                document.CompanyId,
                document.LocationId,
                document.DocumentType,
                document.InternalId
            }).IsUnique();

            entity.HasIndex(document => new { document.Status, document.Id });
        });
    }
}
