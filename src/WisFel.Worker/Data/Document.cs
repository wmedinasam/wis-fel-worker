namespace WisFel.Worker.Data;

public sealed class Document
{
    private Document()
    {
    }

    public long Id { get; private set; }

    public int CompanyId { get; private set; }

    public int LocationId { get; private set; }

    public DocumentStatus Status { get; private set; }

    public string InternalNumber { get; private set; } = string.Empty;

    public string InternalSerial { get; private set; } = string.Empty;

    public int InternalId { get; private set; }

    public string DocumentType { get; private set; } = string.Empty;

    public string? Uuid { get; private set; }

    public string? Serial { get; private set; }

    public string? Number { get; private set; }

    public string? Observations { get; private set; }

    public string? Request { get; private set; }

    public string? Response { get; private set; }

    public byte[]? Pdf { get; private set; }

    public SaeCompany Company { get; private set; } = null!;
}
