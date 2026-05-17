Get-ChildItem "C:\projects\Engram\Engram\src\Engram.App\src-tauri\target\release\bundle" -Recurse -File | ForEach-Object {
    [PSCustomObject]@{
        Name = $_.Name
        SizeMB = [math]::Round($_.Length / 1MB, 2)
        Path = $_.FullName
    }
} | Format-Table -AutoSize
