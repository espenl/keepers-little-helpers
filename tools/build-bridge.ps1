param(
    [string]$GameDirectory = "C:/Program Files (x86)/Steam/steamapps/common/Graveyard Keeper 2",
    [string]$FrameworkDll = ""
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$FrameworkDll) { $FrameworkDll = Join-Path $root 'inspection/GK2-Mod-Framework/src/GK2.Framework/bin/Release/netstandard2.1/GK2.Framework.dll' }
if (!(Test-Path -LiteralPath $FrameworkDll)) { throw 'Supply -FrameworkDll pointing to GK2.Framework.dll (0.1.8+).' }
$managed = Join-Path $GameDirectory 'GraveyardKeeper2_Data/Managed'
$refs = @('netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll') | ForEach-Object { '/reference:' + (Join-Path $managed $_) }
$refs += '/reference:' + (Join-Path $GameDirectory 'BepInEx/core/BepInEx.dll')
$refs += '/reference:' + $FrameworkDll
$refs += '/reference:' + (Join-Path $root 'dist/KeepersJournal.dll')
& 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe' /nologo /target:library /optimize+ ('/out:' + (Join-Path $root 'dist/KeepersLittleHelpers.Framework.dll')) @refs (Join-Path $root 'bridge/FrameworkBridge.cs')
if ($LASTEXITCODE -ne 0) { throw 'Bridge compilation failed' }
