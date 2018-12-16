del *.zip
rmdir /S /Q CraftMagicItems

mkdir CraftMagicItems || goto :error
xcopy CraftMagicItems.dll CraftMagicItems || goto :error
xcopy ..\..\..\Info.json CraftMagicItems || goto :error
xcopy /E /I ..\..\..\L10n CraftMagicItems\L10n || goto :error
xcopy /E /I ..\..\..\Icons CraftMagicItems\Icons || goto :error
xcopy /E /I ..\..\..\Icons CraftMagicItems\Data || goto :error
"C:\Program Files\7-Zip\7z.exe" a CraftMagicItems.zip CraftMagicItems || goto :error

"C:\Program Files\7-Zip\7z.exe" a CraftMagicItems-Source.zip ..\..\*.cs ../../../L10n || goto :error

xcopy /Y CraftMagicItems.dll "C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Kingmaker\Mods\CraftMagicItems" || goto :error
xcopy /E /I /Y ..\..\..\L10n "C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Kingmaker\Mods\CraftMagicItems\L10n" || goto :error
xcopy /E /I /Y ..\..\..\Icons "C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Kingmaker\Mods\CraftMagicItems\Icons" || goto :error
xcopy /E /I /Y ..\..\..\Data "C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Kingmaker\Mods\CraftMagicItems\Data" || goto :error

goto :EOF

:error
echo Failed with error #%errorlevel%.
exit /b %errorlevel%