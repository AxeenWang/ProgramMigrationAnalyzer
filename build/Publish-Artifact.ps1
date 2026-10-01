Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-PmaPublisher {
    param([ValidateSet('Client','Issuer')][string]$Kind, [string]$PublicKeyPath, [string]$PublicationRoot)
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    if ([string]::IsNullOrWhiteSpace($PublicationRoot)) { $PublicationRoot = Join-Path $sourceRoot 'publish' }
    $publicationRoot = [IO.Path]::GetFullPath($PublicationRoot)
    if (!$publicationRoot.StartsWith($sourceRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Publication output must remain inside the formal project root.'
    }
    $stage = Join-Path $publicationRoot ('.stage-' + $Kind.ToLowerInvariant() + '-' + [Guid]::NewGuid().ToString('N'))
    $ownedStage = $false
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
    try {
        if ($Kind -eq 'Client' -and !(Test-Path -LiteralPath (Join-Path $sourceRoot 'src/ProgramMigrationAnalyzer.App/LoginWindow.xaml') -PathType Leaf)) {
            throw 'The formal project root does not contain the current login source.'
        }
        if (Test-Path -LiteralPath $stage) { throw 'Publication staging already exists.' }
        New-Item -ItemType Directory -Path $stage -ErrorAction Stop | Out-Null
        $ownedStage = $true
        $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $arguments = @('publish')
        if ($Kind -eq 'Client') {
            if ([string]::IsNullOrWhiteSpace($PublicKeyPath)) { $PublicKeyPath = Join-Path $sourceRoot 'config/authorization-public-key.pem' }
            $publicInput = [IO.Path]::GetFullPath($PublicKeyPath)
            $preparation = Join-Path $sourceRoot 'build/ProgramMigrationAnalyzer.PublishPreparation/ProgramMigrationAnalyzer.PublishPreparation.csproj'
            $prepared = @(& $dotnet run --project $preparation --configuration Release --verbosity quiet -- --public-key $publicInput --staging (Join-Path $stage 'public'))
            if ($LASTEXITCODE -ne 0 -or $prepared.Count -eq 0) { throw 'Company public key preparation failed.' }
            $snapshot = [string]$prepared[-1]
            if (!(Test-Path -LiteralPath $snapshot -PathType Leaf)) { throw 'No validated company public key snapshot.' }
            $arguments += (Join-Path $sourceRoot 'src/ProgramMigrationAnalyzer.App/ProgramMigrationAnalyzer.App.csproj')
            $arguments += ('-p:AuthorizationPublicKeyPath=' + $snapshot)
            $destination = Join-Path $publicationRoot 'win-x64-single-file'
            $exeName = 'ProgramMigrationAnalyzer.App.exe'
        } else {
            $arguments += (Join-Path $sourceRoot 'src/ProgramMigrationAnalyzer.LicenseIssuer/ProgramMigrationAnalyzer.LicenseIssuer.csproj')
            $destination = Join-Path $publicationRoot 'company-license-issuer'
            $exeName = 'ProgramMigrationAnalyzer.LicenseIssuer.exe'
        }
        $stagedOutput = Join-Path $stage 'artifact'
        $arguments += @('--configuration','Release','--runtime','win-x64','--self-contained','true','--output',$stagedOutput,
            '-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:PublishTrimmed=false',
            '-p:PublishDocumentationFiles=false','-p:DebugType=None','-p:DebugSymbols=false','-p:UseSharedCompilation=false','-m:1','-nr:false')
        Write-Host ('Build source: ' + $sourceRoot)
        & $dotnet @arguments | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { throw 'Publication failed. The previous executable is preserved.' }
        $stagedExe = Join-Path $stagedOutput $exeName
        if (!(Test-Path -LiteralPath $stagedExe -PathType Leaf)) { throw 'Expected executable was not produced.' }
        $files = @(Get-ChildItem -LiteralPath $stagedOutput -Recurse -File)
        if ($files.Count -ne 1 -or $files[0].FullName -ne $stagedExe) { throw 'Single-file output contains unexpected delivery files.' }
        foreach ($path in @($stage,$destination)) {
            $resolved = [IO.Path]::GetFullPath($path)
            if (!$resolved.StartsWith($publicationRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe publication destination.' }
            for ($ancestor = [IO.DirectoryInfo]$resolved; $null -ne $ancestor; $ancestor = $ancestor.Parent) {
                if ($ancestor.Exists -and (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'Reparse publication destination is not supported.' }
            }
        }
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        $destinationExe = Join-Path $destination $exeName
        if (Test-Path -LiteralPath $destinationExe) {
            if (((Get-Item -LiteralPath $destinationExe).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Unsafe executable destination.' }
            [IO.File]::Replace($stagedExe,$destinationExe,(Join-Path $stage 'previous-executable.backup'))
        } else { [IO.File]::Move($stagedExe,$destinationExe) }
        Write-Host ('Ready: ' + $destinationExe)
        if ($Kind -eq 'Client') { Write-Host 'Share only the client EXE. Company authorization Key and credentials are required. HTML preview requires WebView2 Runtime.' }
        else { Write-Host 'Company use only. Keep the issuer and encrypted private key outside customer delivery.' }
        return 0
    } catch {
        Write-Host ('Publish failed: ' + $_.Exception.Message)
        return 1
    } finally {
        if ($ownedStage -and (Test-Path -LiteralPath $stage)) {
            $resolvedStage = [IO.Path]::GetFullPath($stage)
            if ($resolvedStage.StartsWith($publicationRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and
                (((Get-Item -LiteralPath $stage).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0)) {
                Remove-Item -LiteralPath $stage -Recurse -Force
            }
        }
    }
}
