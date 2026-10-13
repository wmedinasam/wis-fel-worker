using WisFel.Worker.Data;

namespace WisFel.Worker.Sae;

public interface ISaeDocumentReader
{
    Task<string> BuildRequestJsonAsync(
        SaeCompany company,
        Document document,
        CancellationToken cancellationToken = default);
}
