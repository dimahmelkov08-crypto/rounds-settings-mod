#!/bin/bash
# Build + install RoundsSettingsMod (run from this folder)
cd "$(dirname "$0")"
G="C:/Program Files (x86)/Steam/steamapps/common/ROUNDS"
M="$G/ROUNDS_Data/Managed"
B="${BEPINEX_CORE:-C:/Program Files (x86)/Steam/steamapps/common/Castle Story/BepInEx/core}"
taskkill //IM ROUNDS.exe //F >/dev/null 2>&1; sleep 4
/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe -nologo -nostdlib -noconfig -target:library -out:RoundsSettingsMod.dll \
 -r:"$M/mscorlib.dll" -r:"$M/netstandard.dll" -r:"$M/System.dll" -r:"$M/System.Core.dll" -r:"$M/Assembly-CSharp.dll" \
 -r:"$M/UnityEngine.dll" -r:"$M/UnityEngine.CoreModule.dll" -r:"$M/UnityEngine.IMGUIModule.dll" -r:"$M/UnityEngine.TextRenderingModule.dll" \
 -r:"$M/UnityEngine.InputLegacyModule.dll" -r:"$M/PhotonUnityNetworking.dll" -r:"$M/PhotonRealtime.dll" -r:"$M/Photon3Unity3D.dll" \
 -r:"$M/Unity.TextMeshPro.dll" -r:"$M/Unity.Localization.dll" -r:"$M/UnityEngine.UI.dll" -r:"$M/UnityEngine.UIModule.dll" \
 -r:"$B/BepInEx.dll" -r:"$B/0Harmony.dll" *.cs 2>&1 | grep -v CS1701 | grep -v "netstandard.dll"
[ ${PIPESTATUS[0]} -eq 0 ] || exit 1
cp RoundsSettingsMod.dll "$G/BepInEx/plugins/RoundsSettingsMod/" && echo installed
