# Ticket P2-167: corpus.ps1 -Packages reads each side's restore output and prints the package version changes, against made-up checkouts.
# Plain `if (...) { throw }` assertions keep this runnable under Windows PowerShell's bundled Pester 3.4 and Pester 5.
# Run: Invoke-Pester ./tools/corpus/tests/Packages.Tests.ps1

Describe 'corpus.ps1 -Packages' {
    BeforeAll {
        # Dot-sourcing with the read-only -List set defines the script's functions here without fetching anything.
        . (Join-Path (Split-Path $PSScriptRoot -Parent) 'corpus.ps1') -List 6>$null
        $script:Root = Join-Path ([IO.Path]::GetTempPath()) ('equiv-packages-' + [Guid]::NewGuid().ToString('N'))

        # A made-up checkout: relative path -> file text.
        function script:New-Side([string]$Name, [hashtable]$Files) {
            $dir = Join-Path $script:Root $Name
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
            foreach ($relative in $Files.Keys) {
                $path = Join-Path $dir $relative
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
                [IO.File]::WriteAllText($path, $Files[$relative])
            }
            return $dir
        }

        function script:Get-Report([string]$LegacyDir, [string]$ModernDir) {
            @(Show-PackageChanges $LegacyDir $ModernDir 6>&1 | ForEach-Object { [string]$_ }) -join "`n"
        }

        function script:Assert-Like([string]$Text, [string]$Like) {
            if ($Text -notlike $Like) { throw "expected text like '$Like', got: $Text" }
        }
    }

    AfterAll {
        Remove-Item -LiteralPath $script:Root -Recurse -Force -ErrorAction SilentlyContinue
    }

    # The failure of 2026-10-12: with both sides restored, the report died reading the file count of the modern side.
    It 'Restored_Sides_Report_Changed_And_OneSided_Packages' {
        $legacy = New-Side 'legacy' @{
            'App/obj/project.assets.json' = '{ "version": 3, "libraries": { "Contoso.Json/12.0.3": { "type": "package" }, "Contoso.Old/1.0.0": { "type": "package" }, "Contoso.Same/2.0.0": { "type": "package" }, "Lib/1.0.0": { "type": "project" } } }'
            'Lib/packages.config'         = '<?xml version="1.0" encoding="utf-8"?><packages><package id="Contoso.Legacy" version="3.1.0" targetFramework="net48" /></packages>'
        }
        $modern = New-Side 'modern' @{
            'App/obj/project.assets.json' = '{ "version": 3, "libraries": { "Contoso.Json/13.0.1": { "type": "package" }, "Contoso.Same/2.0.0": { "type": "package" }, "Contoso.New/5.0.0": { "type": "package" } } }'
            'Lib/obj/project.assets.json' = '{ "version": 3, "libraries": { "contoso.json/13.0.3": { "type": "package" } } }'
        }

        $report = Get-Report $legacy $modern
        Assert-Like $report '*restore outputs read: legacy 2, modern 2 *'
        Assert-Like $report '*== package version changes: 1 changed, 2 legacy only, 1 modern only*'
        if ($report -notmatch 'contoso\.json\s+version changed\s+12\.0\.3\s+13\.0\.1, 13\.0\.3') { throw "no row for the changed package: $report" }
        if ($report -notmatch 'contoso\.legacy\s+legacy only\s+3\.1\.0') { throw "no row for the packages.config package: $report" }
        if ($report -notmatch 'contoso\.new\s+modern only\s+5\.0\.0') { throw "no row for the modern-only package: $report" }
        if ($report -match 'contoso\.same|(?m)^lib\s') { throw "an unchanged package or a project reference is listed: $report" }
        if ($report -like '*warning:*') { throw "warned about restored sides: $report" }
    }

    It 'Side_Without_Restore_Output_Warns_And_Lists_The_Other_Side' {
        $legacy = New-Side 'bare' @{ 'readme.txt' = 'not restored' }
        $modern = New-Side 'restored' @{ 'obj/project.assets.json' = '{ "libraries": { "Contoso.Json/13.0.1": { "type": "package" } } }' }

        $report = Get-Report $legacy $modern
        Assert-Like $report '*restore outputs read: legacy 0, modern 1 *'
        Assert-Like $report '*warning: a side has no restore output*'
        Assert-Like $report '*== package version changes: 0 changed, 0 legacy only, 1 modern only*'
    }

    It 'Same_Packages_On_Both_Sides_Print_No_Table' {
        $files = @{ 'obj/project.assets.json' = '{ "libraries": { "Contoso.Json/13.0.1": { "type": "package" } } }' }
        $report = Get-Report (New-Side 'same-legacy' $files) (New-Side 'same-modern' $files)
        Assert-Like $report '*== package version changes: 0 changed, 0 legacy only, 0 modern only'
    }

    # Restore output that lacks the property a package would be read from holds no package; it is not an error.
    It 'Restore_Output_Without_The_Expected_Property_Holds_No_Package' {
        $dir = New-Side 'sparse' @{
            'NoLibraries/obj/project.assets.json'   = '{ "version": 3 }'
            'NullLibraries/obj/project.assets.json' = '{ "version": 3, "libraries": null }'
            'NoneReferenced/obj/project.assets.json' = '{ "version": 3, "libraries": {} }'
            'NoType/obj/project.assets.json'        = '{ "libraries": { "Contoso.Untyped/1.0.0": { "sha512": "made-up" }, "Contoso.Typed/1.0.0": { "type": "package" } } }'
            'Empty/packages.config'                 = '<?xml version="1.0" encoding="utf-8"?><packages></packages>'
            'SelfClosed/packages.config'            = '<?xml version="1.0" encoding="utf-8"?><packages />'
        }

        $resolved = Get-ResolvedPackages $dir
        if ($resolved.Files -ne 6) { throw "read $($resolved.Files) files" }
        $ids = @($resolved.Versions.Keys) -join ','
        if ($ids -ne 'contoso.typed') { throw "packages: '$ids'" }
    }
}
