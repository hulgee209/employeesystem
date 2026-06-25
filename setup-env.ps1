# Setup Environment Variables for AI Providers
# Copy this file locally, fill in real keys, and never commit real secrets.

Write-Host "Setting up AI provider API keys..."
Write-Host ""

$env:GEMINI_API_KEY = "<your-gemini-api-key>"
$env:GEMINI_MODEL = "gemini-3-pro"
$env:GROQ_API_KEY = "<your-groq-api-key>"
$env:OPENROUTER_API_KEY = "<your-openrouter-api-key>"

Write-Host "[+] Environment variable placeholders loaded for this session"
Write-Host ""
Write-Host "Replace placeholder values locally before starting the ASP.NET app."
Write-Host ""
