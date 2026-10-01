param([string]$PublicKeyPath)
. (Join-Path $PSScriptRoot 'Publish-Artifact.ps1')
exit (Invoke-PmaPublisher -Kind Client -PublicKeyPath $PublicKeyPath)
