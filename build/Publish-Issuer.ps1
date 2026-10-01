. (Join-Path $PSScriptRoot 'Publish-Artifact.ps1')
exit (Invoke-PmaPublisher -Kind Issuer)
