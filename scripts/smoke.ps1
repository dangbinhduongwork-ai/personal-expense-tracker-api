param(
    [string]$BaseUrl = "http://localhost:8080"
)

$ErrorActionPreference = "Stop"
$trimmedBase = $BaseUrl.TrimEnd('/')

Write-Host "1. Checking GET $trimmedBase/health..."
$health = Invoke-RestMethod -Method Get -Uri "$trimmedBase/health"
if ($health -ne "Healthy") {
    throw "Expected 'Healthy' from /health, got '$health'"
}

Write-Host "2. Checking GET $trimmedBase/openapi/v1.json..."
$openapi = Invoke-RestMethod -Method Get -Uri "$trimmedBase/openapi/v1.json"
if (-not $openapi.openapi) {
    throw "OpenAPI document did not contain version field."
}

$email = "smoke.$([Guid]::NewGuid().ToString('N').Substring(0, 8))@example.com"
$pwBytes = New-Object byte[] 18
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $rng.GetBytes($pwBytes)
}
finally {
    $rng.Dispose()
}
$password = "Sm0ke!" + ([Convert]::ToBase64String($pwBytes) -replace '[+/=]', 'A')

Write-Host "3. Registering temporary user ($email)..."
$registerBody = @{ email = $email; password = $password; fullName = "Smoke Test User" } | ConvertTo-Json
$registered = Invoke-RestMethod -Method Post -Uri "$trimmedBase/api/auth/register" -ContentType "application/json" -Body $registerBody

Write-Host "4. Logging in..."
$loginBody = @{ email = $email; password = $password } | ConvertTo-Json
$auth = Invoke-RestMethod -Method Post -Uri "$trimmedBase/api/auth/login" -ContentType "application/json" -Body $loginBody
if ([string]::IsNullOrWhiteSpace($auth.accessToken) -or [string]::IsNullOrWhiteSpace($auth.refreshToken)) {
    throw "Login response omitted tokens."
}

$headers = @{ Authorization = "Bearer $($auth.accessToken)" }
$category = $null
$tx = $null

try {
    Write-Host "5. Verifying seeded system categories..."
    $categories = Invoke-RestMethod -Method Get -Uri "$trimmedBase/api/categories" -Headers $headers
    if ($categories.Count -lt 12) {
        throw "Expected at least 12 seeded categories, got $($categories.Count)"
    }

    Write-Host "6. Creating personal category & transaction..."
    $catBody = @{ name = "Smoke Category"; type = "Expense" } | ConvertTo-Json
    $category = Invoke-RestMethod -Method Post -Uri "$trimmedBase/api/categories" -Headers $headers -ContentType "application/json" -Body $catBody

    $today = (Get-Date).ToString("yyyy-MM-dd")
    $txBody = @{
        title = "Smoke Coffee"
        amount = 45000.00
        type = "Expense"
        categoryId = $category.id
        description = "Docker Compose verification"
        transactionDate = $today
    } | ConvertTo-Json
    $tx = Invoke-RestMethod -Method Post -Uri "$trimmedBase/api/transactions" -Headers $headers -ContentType "application/json" -Body $txBody

    Write-Host "7. Reading summary statistics..."
    $summary = Invoke-RestMethod -Method Get -Uri "$trimmedBase/api/statistics/summary" -Headers $headers
    if ([decimal]$summary.totalExpense -ne 45000.00 -or [int]$summary.transactionCount -ne 1) {
        throw "Unexpected summary totals: expense=$($summary.totalExpense), count=$($summary.transactionCount)"
    }

    Write-Host "SMOKE CHECK PASSED: userId=$($registered.id), seededCategories=$($categories.Count), summaryExpense=$($summary.totalExpense)"
}
finally {
    Write-Host "8. Cleaning up transaction, category, and logging out..."
    if ($null -ne $tx) {
        try { Invoke-RestMethod -Method Delete -Uri "$trimmedBase/api/transactions/$($tx.id)" -Headers $headers | Out-Null } catch {}
    }
    if ($null -ne $category) {
        try { Invoke-RestMethod -Method Delete -Uri "$trimmedBase/api/categories/$($category.id)" -Headers $headers | Out-Null } catch {}
    }
    try {
        $logoutBody = @{ refreshToken = $auth.refreshToken } | ConvertTo-Json
        Invoke-RestMethod -Method Post -Uri "$trimmedBase/api/auth/logout" -Headers $headers -ContentType "application/json" -Body $logoutBody | Out-Null
    } catch {}
}
