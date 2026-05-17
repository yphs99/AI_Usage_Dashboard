# Security Policy

## Supported Versions

This project is currently pre-1.0. Security fixes are applied to the default branch.

## Reporting a Vulnerability

Please report security issues privately to the project maintainers. Do not open a public issue for suspected credential exposure, authentication bypass, data leakage, or other sensitive findings.

## Secret Handling

- Do not commit real API keys, Azure client secrets, MongoDB credentials, `.env` files, local `appsettings.*.json` files, certificates, private keys, or exported production data.
- Use environment variables, .NET User Secrets, Azure Key Vault, GitHub Actions secrets, or the deployment platform's secret manager.
- If a secret is ever committed or included in build output, rotate or revoke it immediately. Removing it from the working tree is not enough once it may have been shared.

Common local environment variables:

```powershell
$env:OpenAI__AdminKey = "<openai-admin-key>"
$env:MongoDB__ConnectionString = "mongodb://localhost:27017"
$env:AzureCost__TenantId = "<tenant-id>"
$env:AzureCost__ClientId = "<client-id>"
$env:AzureCost__ClientSecret = "<client-secret>"
```

## Deployment Notes

The current API has no built-in authentication or authorization. Do not expose it publicly without adding AuthN/AuthZ, rate limiting, request logging/auditing, CORS hardening, and export/maintenance endpoint protections.
