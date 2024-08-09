# Temporary script that installs WIM explorer to DISMTools copies

[Net.ServicePointManager]::SecurityProtocol = "Tls12"

Write-Host "Downloading WIM Explorer..."
New-Item -Path ".\temp" -ItemType Directory -Force | Out-Null
Invoke-WebRequest -Uri "https://github.com/CodingWonders/WIM-Explorer/raw/main/build/Build.zip" -OutFile ".\temp\wimexp.zip"
if (Test-Path ".\temp\wimexp.zip")
{
	Write-Host "Installing WIM Explorer..."
	Expand-Archive -Path ".\temp\wimexp.zip" -Destination "$((Get-Location).Path)" -Verbose -Force
	if ($?)
	{
		New-Item -Path "$((Get-Location).Path)\DT" -ItemType File -Force | Out-Null
		Set-ItemProperty -Path "$((Get-Location).Path)\DT" -Name Attributes -Value Hidden
	}
}
