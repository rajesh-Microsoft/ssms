$ErrorActionPreference = 'Stop'
$base = 'http://localhost:8080/api'
$h = @{ 'X-Tenant' = 'aadya'; 'Content-Type' = 'application/json' }

function Show($label, $obj) { Write-Host "--- $label ---" -ForegroundColor Cyan; $obj | ConvertTo-Json -Depth 6 -Compress }

$login = Invoke-RestMethod "$base/auth/login" -Method Post -Headers $h -Body (@{ username = 'Admin'; password = 'admin123' } | ConvertTo-Json)
$h['Authorization'] = "Bearer $($login.token)"
Write-Host "logged in as $($login.user.username)" -ForegroundColor Green

$y = 2026; $m = 9   # September, so it cannot collide with real August data

# Clean slate if a previous run left one behind
try {
    $existing = Invoke-RestMethod "$base/budgets?year=$y&month=$m" -Headers $h
    Invoke-RestMethod "$base/budgets/$($existing.id)" -Method Delete -Headers $h | Out-Null
    Write-Host "removed leftover budget $($existing.id)"
}
catch { }

Show 'SUGGEST' (Invoke-RestMethod "$base/budgets/suggest?year=$y&month=$m" -Headers $h)

$budget = Invoke-RestMethod "$base/budgets" -Method Post -Headers $h -Body (@{
        month = $m; year = $y; openingBalance = 82000; expectedCollection = 95000; status = 'Draft'; notes = 'smoke test'
    } | ConvertTo-Json)
Show 'CREATED' $budget

# Duplicate month must be refused
try {
    Invoke-RestMethod "$base/budgets" -Method Post -Headers $h -Body (@{ month = $m; year = $y; openingBalance = 1; expectedCollection = 1 } | ConvertTo-Json) | Out-Null
    Write-Host 'BUG: duplicate budget was allowed' -ForegroundColor Red
}
catch { Write-Host "duplicate refused: $($_.ErrorDetails.Message)" -ForegroundColor Green }

$item = Invoke-RestMethod "$base/budgets/$($budget.id)/items" -Method Post -Headers $h -Body (@{
        category = 'Electricity'; description = 'Common area power'; estimatedAmount = 12500; dueDate = "$y-09-10"
    } | ConvertTo-Json)
Show 'ITEM' $item

Invoke-RestMethod "$base/budgets/$($budget.id)/items" -Method Post -Headers $h -Body (@{
        category = 'Security'; estimatedAmount = 25000; dueDate = "$y-09-30" 
    } | ConvertTo-Json) | Out-Null

Show 'AFTER ITEMS' (Invoke-RestMethod "$base/budgets?year=$y&month=$m" -Headers $h)

# Bill arrives lower than planned
$converted = Invoke-RestMethod "$base/budgets/items/$($item.id)/convert" -Method Post -Headers $h -Body (@{
        actualAmount = 11980; expenseDate = "$y-09-11"; vendor = 'BESCOM'; paymentMode = 'Bank Transfer'
    } | ConvertTo-Json)
Show 'CONVERTED' $converted

# Second conversion must be refused
try {
    Invoke-RestMethod "$base/budgets/items/$($item.id)/convert" -Method Post -Headers $h -Body (@{ actualAmount = 999 } | ConvertTo-Json) | Out-Null
    Write-Host 'BUG: double booking was allowed' -ForegroundColor Red
}
catch { Write-Host "double booking refused: $($_.ErrorDetails.Message)" -ForegroundColor Green }

# Editing a booked line must be refused
try {
    Invoke-RestMethod "$base/budgets/items/$($item.id)" -Method Put -Headers $h -Body (@{ category = 'Electricity'; estimatedAmount = 1 } | ConvertTo-Json) | Out-Null
    Write-Host 'BUG: booked line was editable' -ForegroundColor Red
}
catch { Write-Host "editing booked line refused: $($_.ErrorDetails.Message)" -ForegroundColor Green }

# Deleting a budget with booked lines must be refused
try {
    Invoke-RestMethod "$base/budgets/$($budget.id)" -Method Delete -Headers $h | Out-Null
    Write-Host 'BUG: budget with booked lines was deleted' -ForegroundColor Red
}
catch { Write-Host "delete refused: $($_.ErrorDetails.Message)" -ForegroundColor Green }

Show 'FINAL BUDGET' (Invoke-RestMethod "$base/budgets?year=$y&month=$m" -Headers $h)
Show 'VARIANCE' (Invoke-RestMethod "$base/budgets/variance?year=$y&month=$m" -Headers $h)

$exp = Invoke-RestMethod "$base/expenses?year=$y&month=$m" -Headers $h
Show 'EXPENSES BOOKED' $exp
Write-Host "--- done ---" -ForegroundColor Green
