$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe' /nologo /target:exe ('/out:' + (Join-Path $root 'tests/RuleTests.exe')) (Join-Path $root 'src/JournalRules.cs') (Join-Path $root 'src/HelperRules.cs') (Join-Path $root 'tests/RuleTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& (Join-Path $root 'tests/RuleTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
