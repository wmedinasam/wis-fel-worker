namespace WisFel.Worker.Sae;

public static class SaeQueryResultContract
{
    public const string JsonColumn = "Json";
    public const string FreeFieldNameColumn = "Name";
    public const string FreeFieldValueColumn = "Value";
    public const string ProductCodeField = "ProductCode";
}

public sealed record SaeFreeFieldValue(string Name, string? Value);
