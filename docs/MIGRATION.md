# Migration — engram-dotnet

## v0.3.0

### What changed

- Initial public release
- Offline-first sync (mutations, enrollment, pause/resume)
- PostgreSQL backend
- 41 REST endpoints + 28 MCP tools

### Migration steps

1. **Pull latest code**
   ```bash
   git pull origin main
   dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/
   ```

2. **Update database** (PostgreSQL only)
   ```sql
   -- Tables are created automatically. If you get error 42P10:
   ALTER TABLE sync_enrolled_projects ADD UNIQUE (project, "user");
   ```

3. **Restart server**
   ```bash
   systemctl restart engram
   # or
   docker compose up -d --build
   ```

4. **Verify**
   ```bash
   curl http://localhost:7437/health
   # → Should show version "0.3.0"
   ```

### Rollback

```bash
git checkout <previous-tag>
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/
systemctl restart engram
```
