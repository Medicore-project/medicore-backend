# MediCore — CI/CD & Azure Setup Guide

Complete step-by-step instructions for a **DevOps engineer** to set up GitHub Actions, branch protection, Azure resources, and the first deployment.

---

## Prerequisites

Install these tools on your machine:

```bash
# Homebrew (macOS)
brew install azure-cli gh
```

Verify:
```bash
az --version        # need 2.x+
gh --version        # need 2.x+
```

---

## Step 1 — Push the Repository to GitHub

```bash
cd "/Users/bawantha/Documents/SLIT/Y3S1/Medi Core/medicore-backend"

# If not already initialised:
git init
git add .
git commit -m "chore: initial project commit (Scrum 18-20)"

# Create the GitHub repo (GitHub CLI)
gh repo create chamika-kasthuri09/medicore-backend \
  --private \
  --description "MediCore hospital management system backend" \
  --push \
  --source .

# Or if repo already exists:
git remote add origin https://github.com/chamika-kasthuri09/medicore-backend.git
git push -u origin main
```

---

## Step 2 — Update CODEOWNERS with Your Username

Open [`.github/CODEOWNERS`](.github/CODEOWNERS) and replace every occurrence of `@your-github-username` with your real GitHub username (e.g. `@chamika-kasthuri09`).

Commit and push:
```bash
git add .github/CODEOWNERS
git commit -m "chore: set CODEOWNERS to real usernames"
git push
```

---

## Step 3 — Enable Branch Protection (Rulesets)

Go to: **GitHub → Your Repo → Settings → Rules → Rulesets → New branch ruleset**

### Ruleset for `main`

1. **Ruleset Name:** `Protect Main`
2. **Enforcement status:** Active
3. **Bypass list:** Leave this **Empty**. (This replaces the old "Do not allow bypassing" checkbox. An empty list means NO ONE, not even admins, can bypass these rules).
4. **Target branches:** Add target → Include by pattern → type `main`
5. **Branch rules (Check these boxes):**
   - ✅ Require a pull request before merging (Approvals: 1)
     - ✅ Require review from Code Owners
   - ✅ Require status checks to pass 
     - Add `Build & Test` and `Security Scan`
     - ✅ Require branches to be up to date before merging
6. Click **Create**

### Ruleset for `develop`

Repeat the steps above, but:
1. **Name:** `Protect Develop`
2. **Target branches:** type `develop`
3. **Bypass list:** Click "Add bypass" → select "Repository Admin". (This allows you to bypass the rules to merge hotfixes if absolutely necessary).
4. **Branch rules:** Same as `main`.

> **CLI alternative** (GitHub CLI, requires admin token):
> ```bash
> gh api repos/chamika-kasthuri09/medicore-backend/branches/main/protection \
>   --method PUT \
>   --field required_status_checks='{"strict":true,"contexts":["Build & Test","Security Scan"]}' \
>   --field enforce_admins=true \
>   --field required_pull_request_reviews='{"required_approving_review_count":1}' \
>   --field restrictions=null
> ```

---

## Step 4 — Enable Dependabot

Go to: **GitHub → Your Repo → Settings → Security → Code security and analysis**

Turn ON:
- ✅ Dependabot alerts
- ✅ Dependabot security updates
- ✅ Dependabot version updates (reads `.github/dependabot.yml` — already committed)

---

## Step 5 — Enable CodeQL / GitHub Advanced Security

Go to: **GitHub → Your Repo → Settings → Security → Code security and analysis**

Turn ON:
- ✅ Code scanning (GitHub Advanced Security — free for public repos)

> For private repos, CodeQL is free on GitHub Free/Pro/Team plans as of 2024.

---

## Step 6 — Create Azure Resources

```bash
# Login to Azure
az login

# Set your subscription (list with: az account list -o table)
az account set --subscription "<your-subscription-name-or-id>"

# ── Resource Group ────────────────────────────────────────────────────────────
az group create \
  --name medicore-rg \
  --location southeastasia

# ── App Service Plan (Basic B1 — cheapest tier that supports containers) ──────
az appservice plan create \
  --resource-group medicore-rg \
  --name medicore-plan \
  --is-linux \
  --sku B1

# ── App Service (Web App for Containers) ─────────────────────────────────────
az webapp create \
  --resource-group medicore-rg \
  --plan medicore-plan \
  --name medicore-identity \
  --deployment-container-image-name mcr.microsoft.com/dotnet/aspnet:8.0

# Enable container pull from GHCR (public image — no auth needed for public)
# If your GHCR package is PRIVATE, also run the next block:
az webapp config container set \
  --resource-group medicore-rg \
  --name medicore-identity \
  --docker-registry-server-url https://ghcr.io \
  --docker-registry-server-user "$GITHUB_ACTOR" \
  --docker-registry-server-password "$GHCR_PAT"

# ── PostgreSQL Flexible Server ────────────────────────────────────────────────
# (See ADR-001 for the decision rationale)
az postgres flexible-server create \
  --resource-group medicore-rg \
  --name medicore-postgres-slit \
  --location southeastasia \
  --admin-user medicore_admin \
  --admin-password "$(openssl rand -base64 24)" \
  --sku-name Standard_B1ms \
  --tier Burstable \
  --version 16 \
  --storage-size 32 \
  --yes

# Save the generated password — you'll need it in the next step!
# Run this to see it:
az postgres flexible-server show-connection-string \
  --server-name medicore-postgres-slit \
  --admin-user medicore_admin \
  --database-name medicore \
  --query connectionStrings.dotnet -o tsv

# Allow Azure services (App Service) to reach the DB
az postgres flexible-server firewall-rule create \
  --resource-group medicore-rg \
  --name medicore-postgres-slit \
  --rule-name AllowAzureServices \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0

# ── App Service environment variables ─────────────────────────────────────────
az webapp config appsettings set \
  --resource-group medicore-rg \
  --name medicore-identity \
  --settings \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__IdentityDatabase="Host=medicore-postgres-slit.postgres.database.azure.com;Port=5432;Database=medicore;Username=medicore_admin;Password=<YOUR_PASSWORD>;SslMode=Require;Search Path=medicore_identity" \
    Kafka__BootstrapServers="localhost:9092" \
    Seq__ServerUrl="https://your-seq-instance.example.com"
```

---

## Step 7 — Create the Azure Service Principal for CI

GitHub Actions needs credentials to deploy to Azure.

```bash
# Replace <subscription-id> with your actual subscription ID
SUBSCRIPTION_ID=$(az account show --query id -o tsv)

az ad sp create-for-rbac \
  --name "medicore-github-actions" \
  --role contributor \
  --scopes /subscriptions/$SUBSCRIPTION_ID/resourceGroups/medicore-rg \
  --sdk-auth
```

This prints a **JSON block**. Copy the entire JSON output — you'll need it in Step 8.

---

## Step 8 — Create GitHub Secrets

Go to: **GitHub → Your Repo → Settings → Secrets and variables → Actions → New repository secret**

Create these 4 secrets:

| Secret name | Value | How to get it |
|-------------|-------|---------------|
| `AZURE_CREDENTIALS` | The full JSON from Step 7 | Output of `az ad sp create-for-rbac --sdk-auth` |
| `AZURE_WEBAPP_NAME` | `medicore-identity` | The App Service name you chose in Step 6 |
| `AZURE_RESOURCE_GROUP` | `medicore-rg` | The resource group name from Step 6 |
| `GHCR_PAT` | A GitHub Personal Access Token | GitHub → Settings → Developer settings → Personal access tokens → New token → check `write:packages` and `read:packages` |

> **CLI shortcut (GitHub CLI):**
> ```bash
> # Set all secrets from a .env file (never commit this file!)
> gh secret set AZURE_CREDENTIALS < /tmp/azure-sp.json
> gh secret set AZURE_WEBAPP_NAME --body "medicore-identity"
> gh secret set AZURE_RESOURCE_GROUP --body "medicore-rg"
> gh secret set GHCR_PAT --body "<your-pat>"
> ```

---

## Step 9 — First Deployment

```bash
# Create a develop branch and push
git checkout -b develop
git push -u origin develop

# Make a change to trigger the workflow
git checkout -b feature/scrum-20-ci
# (edit any file in src/services/identity/)
git add .
git commit -m "ci: trigger first identity CI run (Scrum 20)"
git push -u origin feature/scrum-20-ci

# Open a PR to main via GitHub UI or CLI:
gh pr create \
  --title "ci: Scrum 20 — CI Pipeline, Security Scanning & First Deployment" \
  --body "Implements Scrum 20 acceptance criteria." \
  --base main
```

The CI pipeline will run. After approval and merge to `main`, the deploy job pushes the image to Azure App Service automatically.

---

## Step 10 — Verify Deployment

```bash
# Check App Service is running
az webapp show \
  --resource-group medicore-rg \
  --name medicore-identity \
  --query state -o tsv

# Health check
curl -s https://medicore-identity.azurewebsites.net/health/live
# Expected: Healthy

# Check logs
az webapp log tail \
  --resource-group medicore-rg \
  --name medicore-identity
```

---

## Taking Screenshots (SecOps Evidence)

For the scrum board, screenshot each of these GitHub Actions pages:

1. **Actions tab** → `CI — Identity Service` → latest run → show all 4 jobs green
2. **Actions tab** → `Security — Full Scan` → latest run → Gitleaks / CodeQL / Trivy jobs
3. **Security tab** → Code scanning alerts (show 0 high/critical)
4. **Packages tab** → `medicore-identity` image → show published tags

---

## Port & URL Reference

| Environment | URL | Port |
|-------------|-----|------|
| Local (docker run) | `http://localhost:5001` | 5001 |
| Azure App Service | `https://medicore-identity.azurewebsites.net` | 443 |
| Health check | `.../health/live` | — |
| Metrics | `.../metrics` | — |
| Swagger (dev only) | `.../swagger` | — |
