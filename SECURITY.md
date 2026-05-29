# Security Policy

## Reporting a Vulnerability

If you find a security vulnerability in engram-dotnet, **do not open a public issue**.

Send a private report to:

- **GitHub Security Advisory**: https://github.com/efreet111/engram-dotnet/security/advisories/new
- **Email**: efreet111@gmail.com

You should receive an acknowledgment within 48 hours. If you don't, follow up via email.

## What to include

- Description of the vulnerability
- Steps to reproduce (PoC preferred)
- Affected version(s)
- Potential impact

## Scope

The following are **in scope**:

- The REST API server (`Engram.Server`)
- The MCP server (`Engram.Mcp`)
- The CLI (`Engram.Cli`)
- Docker deployment configuration

The following are **out of scope**:

- The original [engram](https://github.com/Gentleman-Programming/engram) project
- Third-party MCP clients (Cursor, Claude, etc.)
- Issues that require physical access to the server

## Policy

- We will acknowledge receipt within 48 hours
- We will provide an estimated timeline for a fix
- We will credit you in the release notes (unless you prefer to remain anonymous)
- We ask that you do not disclose the vulnerability publicly until we release a fix

## Supported Versions

| Version | Supported |
|---------|-----------|
| Latest release (main) | ✅ |
| Older releases | ❌ |

We recommend always running the latest build from `main`.
