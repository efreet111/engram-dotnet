# Migration — engram-dotnet

## v1.0.0 → v1.1.0

### What changed

- Added offline-first sync (mutations, enrollment, pause/resume)
- Added structured logging middleware
- Added PostgreSQL backend improvements

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
   # → Should show version "1.1.0"
   ```

### Rollback

```bash
git checkout <previous-tag>
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/
systemctl restart engram
```
