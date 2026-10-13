using System.Text.Json.Nodes;

namespace WisFel.Worker.Sae;

public static class SaeItemProductMapping
{
    public static string GetProductCode(JsonObject item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!item.TryGetPropertyValue(SaeQueryResultContract.ProductCodeField, out var productCodeNode) ||
            productCodeNode is not JsonValue productCodeValue ||
            !productCodeValue.TryGetValue<string>(out var productCode) ||
            string.IsNullOrWhiteSpace(productCode))
        {
            throw new InvalidOperationException(
                $"Every SAE item JSON object must include a non-empty string " +
                $"'{SaeQueryResultContract.ProductCodeField}' field.");
        }

        return productCode;
    }

    public static void SetProduct(JsonObject item, JsonObject product)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(product);

        if (item.Any(property => string.Equals(property.Key, "Product", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "SAE item JSON cannot define 'Product'; the shared INVE query supplies that node.");
        }

        item["Product"] = product;
    }
}
