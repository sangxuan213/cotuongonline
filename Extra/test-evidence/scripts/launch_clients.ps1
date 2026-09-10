$wsDir = "D:\UTH\Nam hai(2025-2026)\HK_he\Laptrinhmang\thuchanh\cotuongonline_tester"
Set-Location $wsDir

Write-Host "Launching Client A on Desktop..."
Start-Process dotnet -ArgumentList "run --project Code/src/XiangqiOnline.Client -c Release --no-build" -WorkingDirectory $wsDir

Start-Sleep -Seconds 2

Write-Host "Launching Client B on Desktop..."
Start-Process dotnet -ArgumentList "run --project Code/src/XiangqiOnline.Client -c Release --no-build" -WorkingDirectory $wsDir

Write-Host "Both clients launched successfully."
