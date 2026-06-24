# Setup Environment Variables for AI Providers
# Copy this file locally, fill in real keys, and never commit real secrets.

Write-Host "Setting up AI provider API keys..."
Write-Host ""

$env:GEMINI_API_KEY_1 = "<your-gemini-api-key-1>"
$env:GEMINI_API_KEY_2 = "<your-gemini-api-key-2>"
$env:GEMINI_API_KEY_3 = "<your-gemini-api-key-3>"
$env:GEMINI_MODEL = "gemini-2.5-flash"
$env:GROQ_API_KEY = "<your-groq-api-key>"
$env:OPENROUTER_API_KEY = "<your-openrouter-api-key>"

Write-Host "[+] Environment variable placeholders loaded for this session"
Write-Host ""
Write-Host "Replace placeholder values locally before starting the AI backend."
Write-Host ""
Write-Host "Next: Start Python backend with:"
Write-Host "  uvicorn main:app --reload --port 8000"
Write-Host ""
