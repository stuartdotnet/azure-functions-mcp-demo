using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace FunctionsMcpDemo;

// ─── Greeting ─────────────────────────────────────────────────────────────────
// No storage required. Good for verifying the MCP server is up before testing
// anything that needs Azurite.

public class GreetingTools(ILogger<GreetingTools> logger)
{
    [Function(nameof(Hello))]
    public string Hello(
        [McpToolTrigger("hello", "Greets a user by name. Use this to verify the MCP server is responding.")]
            ToolInvocationContext context,
        [McpToolProperty("name", "The name to greet. Omit for a generic greeting.")]
            string? name)
    {
        logger.LogInformation("Hello tool invoked for: {Name}", name ?? "(no name)");
        return $"Hello {name ?? "there"}! I am an MCP tool running on Azure Functions.";
    }
}

// ─── Product catalogue ────────────────────────────────────────────────────────
// In-memory data. No storage required.
// Shows: McpToolProperty with isRequired, multiple tools in one class.

public class ProductTools(ILogger<ProductTools> logger)
{
    private static readonly Dictionary<string, Product> Catalogue = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WIDGET-001"]  = new("Blue Widget",            12.99m, "In stock"),
        ["GADGET-PRO"]  = new("Professional Gadget",    89.99m, "Limited stock"),
        ["DOOHICKEY-X"] = new("Deluxe Doohickey",       34.50m, "Out of stock"),
        ["THINGAMAJIG"] = new("Standard Thingamajig",    7.99m, "In stock"),
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

// ─── Notes ────────────────────────────────────────────────────────────────────
// Reads and writes to Azure Blob Storage.
// Requires Azurite locally (or a real storage account).
// Shows: MCP trigger + Azure binding composability.

public class NoteTools(ILogger<NoteTools> logger)
{
    private const string BlobPath = "notes/{mcptoolargs.title}.txt";

    [Function(nameof(SaveNote))]
    [BlobOutput(BlobPath)]
    public string SaveNote(
        [McpToolTrigger("save_note",
            "Saves a text note to storage with a given title. " +
            "The title becomes the filename. Overwrites any existing note with the same title.")]
            ToolInvocationContext context,
        [McpToolProperty("title", "The note title. Use lowercase letters, numbers, and hyphens only.", isRequired: true)]
            string title,
        [McpToolProperty("content", "The text content to save.", isRequired: true)]
            string content)
    {
        logger.LogInformation("Saving note: {Title} ({Length} chars)", title, content.Length);
        return content;
    }

    [Function(nameof(GetNote))]
    public string GetNote(
        [McpToolTrigger("get_note", "Retrieves a previously saved note by its title.")]
            ToolInvocationContext context,
        [McpToolProperty("title", "The title of the note to retrieve.", isRequired: true)]
            string title,
        [BlobInput(BlobPath)] string? noteContent)
    {
        logger.LogInformation("Getting note: {Title}", title);
        return noteContent ?? $"Note '{title}' not found. Use save_note to create it.";
    }
}
