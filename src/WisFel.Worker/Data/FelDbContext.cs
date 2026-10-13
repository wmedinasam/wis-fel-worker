using Microsoft.EntityFrameworkCore;

namespace WisFel.Worker.Data;

public sealed class FelDbContext(DbContextOptions<FelDbContext> options) : DbContext(options)
{
    public DbSet<SaeCompany> SaeCompanies => Set<SaeCompany>();

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
    }
}
