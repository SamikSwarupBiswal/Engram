Get-Process | Where-Object { $_.ProcessName -match 'engram|tauri|node|vite' } | Select-Object ProcessName, Id, StartTime | Format-Table -AutoSize
