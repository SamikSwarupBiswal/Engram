$msvcPath = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207"
$hostBin = "$msvcPath\bin\Hostx64\x64"
Write-Host "link.exe exists: $(Test-Path "$hostBin\link.exe")"
Write-Host "Host bin contents:"
Get-ChildItem $hostBin -Filter "*.exe" | Select-Object Name -First 10
