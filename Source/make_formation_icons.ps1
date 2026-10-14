# Builds and runs IconGen: renders Textures/UI/BattleFormations/Formations/*.png from Defs/FormationDefs.xml.
# Optional arg: path for a preview sheet.
param([string]$Sheet, [string]$RimWorld = "E:\Steam\steamapps\common\RimWorld")

$managed = Join-Path $RimWorld "RimWorldWin64_Data\Managed"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$root = Split-Path $PSScriptRoot
$tmp = Join-Path $env:TEMP "BattleFormationsIconGen"
New-Item -ItemType Directory -Force $tmp | Out-Null
$exe = Join-Path $tmp "IconGen.exe"

$refs = "Assembly-CSharp", "Unity.Mathematics", "UnityEngine.CoreModule", "netstandard" | ForEach-Object { "/reference:" + (Join-Path $managed "$_.dll") }
$src = @(
    (Join-Path $PSScriptRoot "IconGen\IconGen.cs"),
    (Join-Path $PSScriptRoot "BattleFormations\FormationDef.cs"),
    (Join-Path $PSScriptRoot "BattleFormations\FormationLayout.cs")
)
& $csc /nologo /target:exe /platform:x64 /nowarn:1684,1701,1702 "/out:$exe" /reference:System.Drawing.dll /reference:System.Xml.dll $refs $src
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$out = Join-Path $root "Textures\UI\BattleFormations\Formations"
Get-ChildItem $out -Filter *.png -ErrorAction SilentlyContinue | Remove-Item
$runArgs = @($managed, (Join-Path $root "Defs\FormationDefs.xml"), $out)
if ($Sheet) { $runArgs += $Sheet }
& $exe $runArgs
exit $LASTEXITCODE
