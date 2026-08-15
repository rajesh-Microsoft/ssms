param([string]$HostName = 'ssms.yuvaansoft.shop', [int]$Port = 443)

$client = [System.Net.Sockets.TcpClient]::new($HostName, $Port)
$ssl = [System.Net.Security.SslStream]::new($client.GetStream(), $false, { $true })
try {
    $ssl.AuthenticateAsClient($HostName)
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($ssl.RemoteCertificate)
    Write-Output "Subject : $($cert.Subject)"
    Write-Output "Issuer  : $($cert.Issuer)"
    Write-Output "NotAfter: $($cert.NotAfter)"
    $san = $cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' }
    if ($san) { Write-Output "SANs    : $($san.Format($false))" }
}
finally {
    $ssl.Dispose(); $client.Dispose()
}
