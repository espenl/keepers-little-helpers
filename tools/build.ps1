param(
    [string]$GameDirectory = "C:/Program Files (x86)/Steam/steamapps/common/Graveyard Keeper 2"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$managed = Join-Path $GameDirectory 'GraveyardKeeper2_Data/Managed'
New-Item -ItemType Directory -Path (Join-Path $root 'dist') -Force | Out-Null
$csc = 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$refs = @('Assembly-CSharp.dll','LazyBearTechnology.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ImageConversionModule.dll','UnityEngine.UI.dll','UnityEngine.UIModule.dll','UnityEngine.PhysicsModule.dll','Unity.TextMeshPro.dll','netstandard.dll') | ForEach-Object { '/reference:' + (Join-Path $managed $_) }
$refs += '/reference:' + (Join-Path $GameDirectory 'BepInEx/core/BepInEx.dll')
$refs += '/reference:' + (Join-Path $GameDirectory 'BepInEx/core/0Harmony.dll')
& $csc /nologo /target:library /optimize+ ('/out:' + (Join-Path $root 'dist/KeepersJournal.dll')) @refs (Join-Path $root 'src/JournalRules.cs') (Join-Path $root 'src/JournalPlugin.cs') (Join-Path $root 'src/MaterialsPin.cs') (Join-Path $root 'src/MoveHelper.cs') (Join-Path $root 'src/HelpersSettings.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Get-Item (Join-Path $root 'dist/KeepersJournal.dll') | Select-Object Name,Length
