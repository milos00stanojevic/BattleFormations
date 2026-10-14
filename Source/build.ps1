# Builds 1.6/Assemblies/BattleFormations.dll with the .NET Framework compiler (no SDK needed).
param([string]$RimWorld = "E:\Steam\steamapps\common\RimWorld")

$managed = Join-Path $RimWorld "RimWorldWin64_Data\Managed"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = Join-Path $PSScriptRoot "..\1.6\Assemblies\BattleFormations.dll"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

$refs = "mscorlib","System","System.Core","netstandard","Assembly-CSharp","Unity.Mathematics","UnityEngine","UnityEngine.CoreModule","UnityEngine.IMGUIModule","UnityEngine.TextRenderingModule","UnityEngine.InputLegacyModule" |
    ForEach-Object { "/reference:" + (Join-Path $managed "$_.dll") }
$sources = Get-ChildItem (Join-Path $PSScriptRoot "BattleFormations") -Filter *.cs | ForEach-Object { $_.FullName }

& $csc /nologo /noconfig /nostdlib+ /target:library /optimize+ /nowarn:1701,1702 "/out:$out" $refs $sources
exit $LASTEXITCODE
