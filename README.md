# MailDevMcp

English | [日本語](README.ja.md)

[![CI](https://github.com/pierre3/MailDevMcp/actions/workflows/ci.yml/badge.svg)](https://github.com/pierre3/MailDevMcp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/MailDevMcp.svg)](https://www.nuget.org/packages/MailDevMcp)

mcp-name: io.github.pierre3/maildev-mcp

An [MCP (Model Context Protocol)](https://modelcontextprotocol.io/) server for [MailDev](https://github.com/maildev/maildev).  
Manage MailDev Docker containers and inspect received emails directly from your AI-powered editor or MCP client.

This server is intended for local development and test automation scenarios where an MCP client needs to:

- start or stop a MailDev container
- inspect inbox contents
- wait for emails after triggering application behavior
- inspect HTML bodies and attachments
- verify attachment integrity byte-for-byte

## Installation

Install as a global .NET tool:

```sh
dotnet tool install -g MailDevMcp
```

After installation, the `maildev-mcp` command becomes available.

## MCP Client Configuration

Add the following to your MCP client settings (e.g. Claude Desktop, VS Code, etc.):

```json
{
  "mcpServers": {
    "maildev-mcp": {
      "command": "maildev-mcp",
      "env": {
        "MAILDEV_API_PORT": "1080"
      }
    }
  }
}
```

### Environment Variables

| Variable | Default | Description |
|---|---|---|
| `MAILDEV_API_PORT` | `1080` | Port number for the MailDev REST API |

## Typical Workflow

1. Call `StartMaildev` to launch a local MailDev container
2. Trigger your application to send an email
3. Call `WaitForEmail` if delivery is asynchronous
4. Use `ListEmails`, `SearchEmails`, or `GetEmail` to inspect the result
5. Use `GetEmailHtml`, `GetAttachmentContent`, or `VerifyAttachment` when deeper validation is needed
6. Clean up with `DeleteEmail`, `DeleteAllEmails`, or `StopMaildev`

## Available Tools

| Tool | Description |
|---|---|
| `StartMaildev` | Start the MailDev Docker container with configurable SMTP port, API port, authentication, and TLS |
| `StopMaildev` | Stop and remove the MailDev Docker container |
| `MaildevStatus` | Check the running status of MailDev |
| `ListEmails` | Retrieve the list of received emails |
| `GetEmail` | Get the details of an email by ID, including attachment information |
| `GetEmailHtml` | Get the HTML body of an email by ID |
| `SearchEmails` | Search received emails by subject, sender, or recipient |
| `DeleteEmail` | Delete a single email by ID |
| `DeleteAllEmails` | Delete all received emails |
| `WaitForEmail` | Wait until a new email matching given criteria arrives (useful for test automation) |
| `GetAttachmentContent` | Get the raw Base64-encoded content of an email attachment |
| `VerifyAttachment` | Verify that an email attachment matches the original data by byte-by-byte comparison |

## Tool Behavior Notes

- `StartMaildev`
  - Supports custom SMTP and API ports
  - Supports optional SMTP authentication
  - Treats empty or whitespace-only credentials as authentication disabled
  - Supports TLS with a generated self-signed certificate
- `GetEmail` and `ListEmails`
  - Render missing or empty subjects as `(no subject)`
  - Render missing address lists as `(none)`
- `WaitForEmail`
  - Polls MailDev until a matching email arrives or the timeout is reached
  - Reports transient MailDev connection failures in the returned message when they occur during polling
- `VerifyAttachment`
  - Accepts a Base64-encoded original payload
  - Compares the received attachment byte-by-byte
  - Reports either exact match, size mismatch, or content mismatch

## Requirements and Assumptions

- Docker must be installed and available on the same machine where this MCP server runs
- MailDev is accessed through its HTTP API on `localhost`
- The configured API port in the MCP client must match the MailDev API port exposed by Docker

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/) or later
- [Docker](https://www.docker.com/) (for running the MailDev container)

## Building from Source

```sh
git clone https://github.com/pierre3/MailDevMcp.git
cd MailDevMcp
dotnet pack -c Release
dotnet tool install -g --add-source ./bin/Release MailDevMcp
```
## License

[MIT](LICENSE)
