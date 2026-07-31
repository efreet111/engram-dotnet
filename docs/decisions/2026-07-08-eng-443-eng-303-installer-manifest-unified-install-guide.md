---
observation_id: 24
type: "discovery"
title: "ENG-443 + ENG-303: installer manifest + unified install guide"
created_at: "2026-07-08 02:01:04"
topic_key: "eng-443-303-installation-docs"
project: "team/engram-dotnet"
scope: "team"
generated_at: "2026-07-21T22:00:59.5811321Z"
---

# ENG-443 + ENG-303: installer manifest + unified install guide

**What**: Closed ENG-443 (Stack Installer manifest) and ENG-303 (unified installation guide).

**Why**: ENG-443 was blocking OSS launch — manifest needed to document v1.3.0 as current stable. ENG-303 was P1 priority — users needed a single entry point for installation docs scattered across 7+ files.

**Where**: 
- FlowForge repo: install/manifest.yaml (bumped to 0.1.0-alpha.7, documented v1.3.0)
- engram-dotnet repo: docs/INSTALL.md (new unified guide), README.md, SETUP-WIZARD.md, QUICK-START.md (added links)

**Learned**: 
- ENG-443 was already partially done (manifest said >=0.4.0) — just needed documentation update
- ENG-303 work was mostly consolidation, not new content — existing docs were good but scattered
- INSTALL.md structure: Quick Decision table → 3 methods → MCP setup → Verify → Next steps → Troubleshooting → Uninstall
- Cross-linking strategy: INSTALL.md is the hub, QUICK-START/SETUP-WIZARD link to it as entry point
