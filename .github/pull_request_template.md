## Summary
<!-- One sentence: what does this PR do? -->


## Linked Issue
<!-- Closes #<issue-number> -->
Closes #

## Type of Change
- [ ] 🐛 Bug fix
- [ ] ✨ New feature / implementation
- [ ] ♻️ Refactor (no behaviour change)
- [ ] 🔧 DevOps / infra / config
- [ ] 📝 Documentation only
- [ ] 🔒 Security fix

## Services Affected
<!-- Tick every service whose code changed — CI is path-filtered so only those rebuild -->
- [ ] Identity (`src/services/identity/`)
- [ ] Patient (`src/services/patient/`)
- [ ] Appointment (`src/services/appointment/`)
- [ ] Billing (`src/services/billing/`)
- [ ] Gateway (`src/gateway/`)
- [ ] Shared contracts (`src/shared/`)
- [ ] Infra / DevOps (`infra/`, `.github/`, `docker-compose.yml`)

## Checklist
- [ ] I have run `dotnet test --filter "Category!=Integration"` locally and all tests pass
- [ ] I have run `dotnet list package --vulnerable` — no high/critical vulnerabilities
- [ ] New public methods and classes have XML doc comments
- [ ] EF Core migrations have been added if the data model changed
- [ ] No secrets or passwords are committed (use `.env` / GitHub Secrets)
- [ ] `docker compose up -d` still works after my changes
- [ ] README / ADR updated if architectural decisions were made

## Evidence (Screenshots / Logs)
<!-- Paste CI run link or screenshot for security scans if this is a security-related PR -->
