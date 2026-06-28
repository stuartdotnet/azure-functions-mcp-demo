# Azure Functions MCP Demo

A minimal C# / .NET 10 demo showing how to expose [Model Context Protocol (MCP)](https://modelcontextprotocol.io) tools via Azure Functions using the `Microsoft.Azure.Functions.Worker.Extensions.Mcp` extension.

## What it does

Two tool groups are registered as MCP tools that any MCP client (Claude Desktop, VS Code with GitHub Copilot, etc.) can discover and invoke:

| Tool | Description |
|---|---|
| `list_products` | Returns all products in the in-memory catalogue with SKUs. |
| `get_product_info` | Returns price and stock status for a given SKU. |
| `save_product_details` | Saves an extended product description to Azure Blob Storage. |
| `get_product_details` | Retrieves a previously saved product description by SKU. |

`list_products` and `get_product_info` use only in-memory data. `save_product_details` and `get_product_details` require Blob Storage — Azurite works fine locally.

## Prerequisites

| Tool | Purpose |
|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Runtime and build toolchain |
| [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local) | Local `func` host |
| [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) | Local Azure Storage emulator (for blob tools) |
| VS Code + Azure Functions extension | IDE integration and MCP client |

## Running locally

**1. Start Azurite**

```bash
azurite
```

Or use the Azurite extension in VS Code.

**2. Start the Functions host**

Press `F5` in VS Code, or:

```bash
dotnet build
func start --script-root bin/Debug/net10.0
```

The MCP SSE endpoint will be available at:

```
http://localhost:7071/runtime/webhooks/mcp/sse
```

**3. Connect your MCP client**

The repo includes `.vscode/mcp.json` with ready-made server configs for both local and Azure targets. VS Code picks this up automatically — no manual configuration needed.

## Project structure

```
├── ProductTools.cs      # MCP tool definitions (ProductTools, ProductDetailsTools)
├── Program.cs           # Host builder
├── host.json            # Functions host config, including MCP server metadata
├── FunctionsMcpDemo.csproj
├── local.settings.json  # Local config (Azurite storage connection)
├── azure.yaml           # azd service definition
├── infra/
│   ├── main.bicep       # Main infrastructure (Function App, Storage, App Insights)
│   └── app/
│       ├── api.bicep    # Function App resource definition
│       ├── entra.bicep  # Entra ID app registration (see Security section)
│       └── rbac.bicep   # Role assignments
└── .vscode/
    └── mcp.json         # MCP client config for local and Azure targets
```

## Key packages

| Package | Purpose |
|---|---|
| `Microsoft.Azure.Functions.Worker.Extensions.Mcp` | `[McpToolTrigger]` and `[McpToolProperty]` attributes |
| `Microsoft.Azure.Functions.Worker.Extensions.Storage.Blobs` | Blob input/output bindings |

## How it works

Each MCP tool is a regular Azure Function with `[McpToolTrigger]` replacing the usual HTTP or timer trigger. The extension handles MCP protocol negotiation — tool discovery, argument parsing, and response serialisation — so the function body just returns a string.

Composing MCP triggers with other Azure bindings (like `[BlobOutput]`) works as normal, which is how `save_product_details` writes to Blob Storage without any SDK calls in the function body.

`ConfigureFunctionsWebApplication()` (not `ConfigureFunctionsWorkerDefaults()`) is required in `Program.cs` because the MCP extension uses HTTP SSE transport, which needs the ASP.NET Core pipeline.

---

## Connecting to the MCP server

### Local (this project only)

`.vscode/mcp.json` is already configured. When you open this project in VS Code, the local server appears automatically in Copilot agent mode — no setup needed.

### Global (all VS Code projects)

To make an MCP server available across every workspace, add it to your VS Code user-level config:

1. Open the Command Palette (`Ctrl+Shift+P`)
2. Run **MCP: Open User Configuration**
3. Add the server entry — same format as `.vscode/mcp.json`

Alternatively, create `%USERPROFILE%\.mcp.json` — both VS Code and Visual Studio read from this path.

Use the Azure-deployed URL for a global config (the local URL only works when the function app is actively running in a specific project).

### How input prompts work

The `.vscode/mcp.json` uses VS Code's input variable mechanism to avoid hardcoding secrets:

```json
{
  "inputs": [
    {
      "type": "promptString",
      "id": "functions-mcp-extension-system-key",
      "description": "Azure Functions MCP Extension System Key",
      "password": true
    },
    {
      "type": "promptString",
      "id": "functionapp-host",
      "description": "Your function app hostname (e.g. my-app.azurewebsites.net)"
    }
  ],
  "servers": {
    "FunctionsMcpDemo-azure": {
      "type": "http",
      "url": "https://${input:functionapp-host}/runtime/webhooks/mcp/sse?code=${input:functions-mcp-extension-system-key}"
    }
  }
}
```

VS Code scans each server's config for `${input:id}` references and only prompts for inputs that are actually used by that server. The local server has no `${input:...}` tokens, so it never prompts. The Azure server references two, so VS Code asks for both when first connecting. Values are cached for the session.

`password: true` masks the system key in the prompt and prevents it being stored in any settings file.

---

## Deploying to Azure

This project is configured for [Azure Developer CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/overview) (`azd`).

```bash
azd up
```

Once deployed, get the Function App hostname from the portal or `azd` output. The MCP system key is under **Function App > App keys** in the Azure Portal.

---

## Security

### System key (default)

The deployed MCP endpoint is secured with an Azure Functions system key passed as a `code=` query parameter. This is the default configuration — no extra setup needed.

### Entra ID (recommended for team use)

For user-level identity, audit logs, and token-based revocation, secure the endpoint with Microsoft Entra ID using Azure App Service Easy Auth.

**How it works:**

```
MCP client ──Bearer token──▶ Easy Auth (validates with Entra) ──▶ Function App
```

Easy Auth is a platform-level feature — no code changes to your functions are needed.

**What's already in this repo:**

`infra/app/entra.bicep` creates:
- An Entra app registration for the Function App
- A service principal
- A `user_impersonation` OAuth2 scope
- The Easy Auth redirect URI

**What's needed to activate it:**

1. Wire `entra.bicep` into `main.bicep` as a module
2. Configure Easy Auth on the Function App resource in `api.bicep` using the app registration's client ID
3. Update `.vscode/mcp.json` to send a Bearer token:

```json
{
  "servers": {
    "FunctionsMcpDemo-azure": {
      "type": "http",
      "url": "https://${input:functionapp-host}/runtime/webhooks/mcp/sse",
      "headers": {
        "Authorization": "Bearer ${input:entra-bearer-token}"
      }
    }
  }
}
```

Obtain a token via the Azure CLI:
```bash
az account get-access-token --resource YOUR-APP-CLIENT-ID --query accessToken -o tsv
```

**Comparison:**

| | System key | Entra ID |
|---|---|---|
| Setup | Works out of the box | Requires Bicep wiring + Easy Auth config |
| Expiry | Never (until rotated) | 1 hour (refresh handled by clients) |
| Identity | Anonymous | Per-user, audit logged |
| Revocation | Rotate key in portal | Disable user or app in Entra |

System key and Entra can be used together for defence-in-depth.
