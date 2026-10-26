Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-PmaPublisher {
    param([string]$PublicationRoot)
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    if ([string]::IsNullOrWhiteSpace($PublicationRoot)) { $PublicationRoot = Join-Path $sourceRoot 'publish' }
    $publicationRoot = [IO.Path]::GetFullPath($PublicationRoot)
    if (!$publicationRoot.StartsWith($sourceRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Publication output must remain inside the formal project root.'
    }
    $stage = Join-Path $publicationRoot ('.stage-analyzer-' + [Guid]::NewGuid().ToString('N'))
    $ownedStage = $false
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
    $previousConsoleEncoding = [Console]::OutputEncoding
    $previousOutputEncoding = $OutputEncoding
    try {
        # Windows PowerShell must decode native UTF-8 output using the same encoding.
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        $OutputEncoding = [Console]::OutputEncoding
        if (!(Test-Path -LiteralPath (Join-Path $sourceRoot 'ProgramMigrationAnalyzer.sln') -PathType Leaf) -or
            !(Test-Path -LiteralPath (Join-Path $sourceRoot 'src/ProgramMigrationAnalyzer.App/App.xaml') -PathType Leaf)) {
            throw 'Publish from the formal ProgramMigrationAnalyzer project root.'
        }
        if (!(Test-Path -LiteralPath (Join-Path $sourceRoot 'config/embedded-login.json') -PathType Leaf)) {
            throw 'Embedded login configuration is missing. Run build/Set-EmbeddedLogin.ps1 before publishing.'
        }
        if (Test-Path -LiteralPath $stage) { throw 'Publication staging already exists.' }
        New-Item -ItemType Directory -Path $stage -ErrorAction Stop | Out-Null
        $ownedStage = $true
        $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $arguments = @('publish')
        $arguments += (Join-Path $sourceRoot 'src/ProgramMigrationAnalyzer.App/ProgramMigrationAnalyzer.App.csproj')
        $destination = Join-Path $publicationRoot 'win-x64-single-file'
        $exeName = 'ProgramMigrationAnalyzer.App.exe'
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
        Write-Host 'Ready for Windows x64. Sign in with the configured account. No external Key file is needed. HTML preview requires WebView2 Runtime.'
        return 0
    } catch {
        Write-Host ('Publish failed: ' + $_.Exception.Message)
        return 1
    } finally {
        [Console]::OutputEncoding = $previousConsoleEncoding
        $OutputEncoding = $previousOutputEncoding
        if ($ownedStage -and (Test-Path -LiteralPath $stage)) {
            $resolvedStage = [IO.Path]::GetFullPath($stage)
            if ($resolvedStage.StartsWith($publicationRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and
                (((Get-Item -LiteralPath $stage).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0)) {
                Remove-Item -LiteralPath $stage -Recurse -Force
            }
        }
    }
}
