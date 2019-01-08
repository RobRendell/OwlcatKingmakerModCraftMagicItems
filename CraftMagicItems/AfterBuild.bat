del *.zip
rmdir /S /Q CraftMagicItems

mkdir CraftMagicItems || goto :error
xcopy CraftMagicItems.dll CraftMagicItems || goto :error
xcopy ..\..\..\Info.json CraftMagicItems || goto :error
xcopy /E /I ..\..\..\L10n CraftMagicItems\L10n || goto :error
xcopy /E /I ..\..\..\Icons CraftMagicItems\Icons || goto :error
xcopy /E /I ..\..\..\Data CraftMagicItems\Data || goto :error
"E:\Program Files\7-Zip\7z.exe" a CraftMagicItems.zip CraftMagicItems || goto :error

"E:\Program Files\7-Zip\7z.exe" a CraftMagicItems-Source.zip ..\..\*.cs ..\..\..\L10n ..\..\..\Icons ..\..\..\Data || goto :error

"E:\Program Files\7-Zip\7z.exe" x CraftMagicItems.zip -y -o"C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Kingmaker\Mods" || goto :error
copy CraftMagicItems.pdb "C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Kingmaker\Mods\CraftMagicItems"

goto :EOF

:error
echo Failed with error #%errorlevel%.
exit /b %errorlevel%