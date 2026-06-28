using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace FunctionsMcpDemo;

// ─── Product catalogue ────────────────────────────────────────────────────────
// In-memory data. No storage required.
// Shows: McpToolProperty with isRequired, multiple tools in one class.

public class ProductTools(ILogger<ProductTools> logger)
{
    private static readonly Dictionary<string, Product> Catalogue = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CTL-PRO-X"]   = new("Pro Gaming Controller",  59.99m, "In stock"),
        ["HEADSET-7X"]  = new("7.1 Surround Headset",   89.99m, "Limited stock"),
        ["MECH-KB-TKL"] = new("Mechanical TKL Keyboard", 129.99m, "In stock"),
        ["MOUSE-DPI-4K"] = new("4K DPI Gaming Mouse",   49.99m, "Out of stock"),
        ["CHAIR-ERGO"]  = new("Ergonomic Gaming Chair", 349.99m, "Limited stock"),
        ["MON-165HZ"]   = new("165Hz 27\" Gaming Monitor", 299.99m, "In stock"),
    };

    [Function(nameof(GetProductInfo))]
    public string GetProductInfo(
        [McpToolTrigger("get_product_info",
            "Returns price and stock status for a product SKU. " +
            "Use list_products first if you don't know the SKU.")]
            ToolInvocationContext context,
        [McpToolProperty("sku", "The product SKU (e.g. WIDGET-001, GADGET-PRO).", isRequired: true)]
            string sku)
    {
        logger.LogInformation("Product info requested for SKU: {Sku}", sku);

        if (!Catalogue.TryGetValue(sku, out var product))
            return $"Product '{sku}' not found. Call list_products to see available SKUs.";

        return $"{product.Name} — £{product.Price:F2} — {product.StockStatus}";
    }

    [Function(nameof(ListProducts))]
    public string ListProducts(
        [McpToolTrigger("list_products", "Returns all available products and their SKUs.")]
            ToolInvocationContext context)
    {
        logger.LogInformation("Product list requested");

        var lines = Catalogue.Select(kvp =>
            $"  {kvp.Key}: {kvp.Value.Name} (£{kvp.Value.Price:F2}) — {kvp.Value.StockStatus}");

        return "Available products:\n" + string.Join("\n", lines);
    }

    private record Product(string Name, decimal Price, string StockStatus);
}

// ─── Product details ──────────────────────────────────────────────────────────
// Reads and writes extended product descriptions to Azure Blob Storage.
// Requires Azurite locally (or a real storage account).
// Shows: MCP trigger + Azure binding composability.

public class ProductDetailsTools(ILogger<ProductDetailsTools> logger)
{
    private const string BlobPath = "products/{mcptoolargs.sku}.txt";

    [Function(nameof(SaveProductDetails))]
    [BlobOutput(BlobPath)]
    public string SaveProductDetails(
        [McpToolTrigger("save_product_details",
            "Saves an extended description for a product SKU to storage. " +
            "Use list_products to find valid SKUs. Overwrites any existing description.")]
            ToolInvocationContext context,
        [McpToolProperty("sku", "The product SKU (e.g. WIDGET-001). Use list_products to find valid SKUs.", isRequired: true)]
            string sku,
        [McpToolProperty("details", "The extended product description to save.", isRequired: true)]
            string details)
    {
        logger.LogInformation("Saving product details for SKU: {Sku} ({Length} chars)", sku, details.Length);
        return details;
    }

    [Function(nameof(GetProductDetails))]
    public string GetProductDetails(
        [McpToolTrigger("get_product_details",
            "Retrieves the extended description for a product SKU. " +
            "Use get_product_info for price and stock status instead.")]
            ToolInvocationContext context,
        [McpToolProperty("sku", "The product SKU to look up.", isRequired: true)]
            string sku,
        [BlobInput(BlobPath)] string? details)
    {
        logger.LogInformation("Getting product details for SKU: {Sku}", sku);
        return details ?? $"No extended details saved for '{sku}'. Use save_product_details to add them.";
    }
}
