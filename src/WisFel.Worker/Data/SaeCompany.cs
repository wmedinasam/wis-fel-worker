using System.Globalization;

namespace WisFel.Worker.Data;

public sealed class SaeCompany
{
    private SaeCompany()
    {
    }

    public SaeCompany(decimal saeVersion, int companyNumber)
    {
        if (saeVersion < 4.0m || saeVersion * 10m != decimal.Truncate(saeVersion * 10m))
        {
            throw new ArgumentOutOfRangeException(
                nameof(saeVersion),
                "SAE version must be 4.0 or later and have no more than one decimal place.");
        }

        if (companyNumber is < 1 or > 99)
        {
            throw new ArgumentOutOfRangeException(
                nameof(companyNumber),
                "SAE company number must be between 1 and 99.");
        }

        SaeVersion = saeVersion;
        CompanyNumber = companyNumber;
    }

    public int Id { get; private set; }

    public decimal SaeVersion { get; private set; }

    public int CompanyNumber { get; private set; }

    public bool IsLicensed { get; private set; }

    public DateTimeOffset? LicenseUpdatedAt { get; private set; }

    public string DatabaseName =>
        $"SAE{(SaeVersion * 10m).ToString("0", CultureInfo.InvariantCulture)}Empre{CompanyNumber.ToString("00", CultureInfo.InvariantCulture)}";

    public void ApplyLicenseStatusFromCloud(bool isLicensed, DateTimeOffset updatedAt)
    {
        IsLicensed = isLicensed;
        LicenseUpdatedAt = updatedAt;
    }
}
