# Run this ONCE to create the signing certificate and get the values
# needed for GitHub repository secrets.
#
# Usage:  .\scripts\setup-cert.ps1
#
# After running, add two secrets to your GitHub repo
# (Settings → Secrets and variables → Actions → New repository secret):
#
#   SIGNING_CERTIFICATE  — the base64 block printed by this script
#   CERTIFICATE_PASSWORD — the password you choose below

param([string]$Password = "")

if (-not $Password) {
    $secPwd = Read-Host "Choose a certificate password" -AsSecureString
} else {
    $secPwd = ConvertTo-SecureString $Password -AsPlainText -Force
}

Write-Host "`nCreating self-signed certificate (CN=9D1F8C40-115A-44D9-94E5-BA0ECC194C4C, valid 10 years)..."

$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject "CN=9D1F8C40-115A-44D9-94E5-BA0ECC194C4C" `
    -KeyUsage DigitalSignature `
    -FriendlyName "Hatch Store Publisher Signing" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
    -NotAfter (Get-Date).AddYears(10)

$pfxPath = Join-Path $PSScriptRoot "hatch-signing.pfx"
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $secPwd -Force | Out-Null

$cerPath = Join-Path $PSScriptRoot "install-cert.cer"
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null

$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath))

Write-Host "`nCertificate thumbprint: $($cert.Thumbprint)" -ForegroundColor Green
Write-Host "`n--- GitHub Secret: SIGNING_CERTIFICATE ---" -ForegroundColor Yellow
Write-Host $b64
Write-Host "------------------------------------------`n" -ForegroundColor Yellow
Write-Host "GitHub Secret: CERTIFICATE_PASSWORD = (the password you just entered)`n" -ForegroundColor Yellow
Write-Host "PFX saved to: $pfxPath" -ForegroundColor Gray
Write-Host "CER saved to: $cerPath  (distribute this to sideload users)" -ForegroundColor Gray
Write-Host "Keep the PFX safe — do NOT commit it to git." -ForegroundColor Gray
