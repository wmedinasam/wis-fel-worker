namespace WisFel.Worker.Data;

public enum DocumentSendDenialReason
{
    CompanyNotConfigured,
    CompanyNotLicensed
}

public sealed record DocumentSendDecision(
    bool IsAllowed,
    DocumentSendDenialReason? DenialReason)
{
    public static DocumentSendDecision Allow() => new(true, null);

    public static DocumentSendDecision Deny(DocumentSendDenialReason reason) => new(false, reason);
}

public interface IDocumentSendPolicy
{
    DocumentSendDecision Evaluate(SaeCompany? company);
}

public sealed class DocumentSendPolicy : IDocumentSendPolicy
{
    public DocumentSendDecision Evaluate(SaeCompany? company)
    {
        if (company is null)
        {
            return DocumentSendDecision.Deny(DocumentSendDenialReason.CompanyNotConfigured);
        }

        return company.IsLicensed
            ? DocumentSendDecision.Allow()
            : DocumentSendDecision.Deny(DocumentSendDenialReason.CompanyNotLicensed);
    }
}
