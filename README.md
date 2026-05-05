# Azure Functions MCP Demo

A minimal C# / .NET 10 demo showing how to expose [Model Context Protocol (MCP)](https://modelcontextprotocol.io) tools via Azure Functions using the `Microsoft.Azure.Functions.Worker.Extensions.Mcp` extension.

## What it does

Three tool groups are registered as MCP tools that any MCP client (Claude Desktop, VS Code with GitHub Copilot, etc.) can discover and invoke:

| Tool | Description |
|---|---|
| `hello` | Greets a user by name. Good for verifying the server is up. |
| `list_products` | Returns all products in the in-memory catalogue. |
| `get_product_info` | Returns price and stock status for a given SKU. |
| `save_note` | Saves a text note to Azure Blob Storage (keyed by title). |
| `get_note` | Retrieves a previously saved note by title. |

`hello`, `list_products`, and `get_product_info` use only in-memory data. `save_note` and `get_note` require Blob Storage — Azurite works fine locally.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) (for the notes tools locally)
- An MCP client: Claude Desktop, VS Code + GitHub Copilot, or similar

## Running locally

**1. Start Azurite**

```bash
azurite --location ./AzuriteConfig --debug ./azurite-debug.log
```

**2. Start the Functions host**

```bash
func start
```

The MCP endpoint will be available at:

```
http://localhost:7071/runtime/webhooks/mcp
```

**3. Connect your MCP client**

The repo includes a `.vscode/mcp.json` with ready-made server configurations for both local and remote targets. In VS Code with GitHub Copilot, this is picked up automatically.

For Claude Desktop or other clients, add the local server URL to your MCP config:

```json
{
  "mcpServers": {
    "functions-mcp-demo": {
      "type": "http",
      "url": "http://localhost:7071/runtime/webhooks/mcp"
    }
  }
}
```

## Deploying to Azure

This project is configured for [Azure Developer CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/overview) (`azd`).

```bash
# Provision resources and deploy in one step
azd up
```

Once deployed, get the Function App hostname from the portal or `azd show`, then update your MCP client config to point at the remote URL. The `.vscode/mcp.json` includes a `remote-mcp-demo` server entry that prompts for the hostname and system key.

The system key for the MCP extension webhook is in the Azure Portal under **Function App > App keys**.

## Project structure

```
├── McpTools.cs          # All MCP tool definitions (GreetingTools, ProductTools, NoteTools)
├── Program.cs           # Host builder
├── host.json            # Functions host config, including MCP server metadata
├── FunctionsMcpDemo.csproj
├── azure.yaml           # azd service definition
├── .vscode/
│   └── mcp.json         # MCP client config for local and remote targets
└── AzuriteConfig/       # Azurite local storage data (gitignored)
```

## Key packages

| Package | Purpose |
|---|---|
| `Microsoft.Azure.Functions.Worker.Extensions.Mcp` | MCP trigger and tool property attributes |
| `Microsoft.Azure.Functions.Worker.Extensions.Storage.Blobs` | Blob input/output bindings for notes |

## How it works

Each MCP tool is a regular Azure Function with an `[McpToolTrigger]` attribute replacing the usual HTTP or timer trigger. The extension handles MCP protocol negotiation — tool discovery, argument parsing, and response serialisation — so the function body just returns a string.

Composing MCP triggers with other bindings (like `[BlobOutput]`) works as normal, which is how `save_note` writes to Blob Storage without any SDK calls in the function body.
