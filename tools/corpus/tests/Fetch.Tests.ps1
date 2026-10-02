# Tickets P2-058 and P2-066: the global.json patch corpus.ps1 -Fetch applies to a checkout, against throwaway local repositories.
# Plain `if (...) { throw }` assertions keep this runnable under Windows PowerShell's bundled Pester 3.4 and Pester 5.
# Run: Invoke-Pester ./tools/corpus/tests/Fetch.Tests.ps1

Describe 'corpus.ps1 -Fetch global.json patch' {
    BeforeAll {
        # Dot-sourcing with the read-only -List set defines the script's functions here without fetching anything.
        . (Join-Path (Split-Path $PSScriptRoot -Parent) 'corpus.ps1') -List 6>$null
        $script:Root = Join-Path ([IO.Path]::GetTempPath()) ('equiv-fetch-' + [Guid]::NewGuid().ToString('N'))

        # A committed checkout whose global.json holds $GlobalJson, or none when it is null.
        function script:New-Checkout([string]$Name, [string]$GlobalJson) {
            $dir = Join-Path $script:Root $Name
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
            [IO.File]::WriteAllText((Join-Path $dir 'readme.txt'), 'made-up checkout')
            if ($GlobalJson) { [IO.File]::WriteAllText((Join-Path $dir 'global.json'), $GlobalJson) }
            & git -C $dir init -q 2>&1 | Out-Null
            & git -C $dir add -A 2>&1 | Out-Null
            & git -C $dir -c user.name=equiv -c user.email=equiv@example.invalid commit -q -m 'checkout' 2>&1 | Out-Null
            return $dir
        }

        function script:Get-Sdk([string]$Dir) {
            (Get-Content -LiteralPath (Join-Path $Dir 'global.json') -Raw | ConvertFrom-Json).sdk
        }

        function script:Assert-Clean([string]$Dir) {
            $dirty = & git -C $Dir status --porcelain
            if ($dirty) { throw "checkout is not clean after the patch: $dirty" }
        }
    }

    AfterAll {
        Remove-Item -LiteralPath $script:Root -Recurse -Force
    }

    It 'Pin_Without_RollForward_Becomes_LatestMajor' {
        $dir = New-Checkout 'none' '{ "sdk": { "version": "5.0.202" } }'
        Set-RollForwardLatestMajor $dir 6>$null
        $sdk = Get-Sdk $dir
        if ($sdk.rollForward -ne 'latestMajor') { throw "rollForward is '$($sdk.rollForward)'" }
        if ($sdk.version -ne '5.0.202') { throw "version changed to '$($sdk.version)'" }
        Assert-Clean $dir
    }

    # A version-upgrade pair's legacy side: the pin rolls forward, but never past its own major.
    It 'Pin_That_Stays_Within_Its_Major_Becomes_LatestMajor' {
        foreach ($policy in 'latestMinor', 'feature') {
            $dir = New-Checkout $policy ('{ "sdk": { "version": "8.0.404", "rollForward": "' + $policy + '" } }')
            Set-RollForwardLatestMajor $dir 6>$null
            $sdk = Get-Sdk $dir
            if ($sdk.rollForward -ne 'latestMajor') { throw "$policy became '$($sdk.rollForward)'" }
            if ($sdk.version -ne '8.0.404') { throw "version changed to '$($sdk.version)'" }
            Assert-Clean $dir
        }
    }

    It 'Pin_That_Already_Crosses_Majors_Is_Left_As_Written' {
        foreach ($policy in 'major', 'latestMajor') {
            $text = '{ "sdk": { "version": "9.0.100", "rollForward": "' + $policy + '" } }'
            $dir = New-Checkout "keeps-$policy" $text
            Set-RollForwardLatestMajor $dir 6>$null
            $after = [IO.File]::ReadAllText((Join-Path $dir 'global.json'))
            if ($after -ne $text) { throw "global.json was rewritten: $after" }
        }
    }

    It 'Checkout_Without_A_Pin_Is_Left_Alone' {
        $none = New-Checkout 'no-file' $null
        Set-RollForwardLatestMajor $none 6>$null
        if (Test-Path -LiteralPath (Join-Path $none 'global.json')) { throw 'a global.json was created' }

        $text = '{ "msbuild-sdks": { "Contoso.Sdk": "1.0.0" } }'
        $noSdk = New-Checkout 'no-sdk' $text
        Set-RollForwardLatestMajor $noSdk 6>$null
        $after = [IO.File]::ReadAllText((Join-Path $noSdk 'global.json'))
        if ($after -ne $text) { throw "global.json was rewritten: $after" }
    }
}
