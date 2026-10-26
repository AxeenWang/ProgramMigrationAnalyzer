if ($args.Count -ne 0) { throw 'Publish-Client.ps1 takes no arguments.' }
. (Join-Path $PSScriptRoot 'Publish-Artifact.ps1')
exit (Invoke-PmaPublisher)
