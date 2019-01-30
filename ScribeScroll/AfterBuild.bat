del *.zip
rmdir /S /Q ScribeScroll

mkdir ScribeScroll || goto :error
xcopy ScribeScroll.dll ScribeScroll || goto :error
xcopy ..\..\..\Info.json ScribeScroll || goto :error
"E:\Program Files\7-Zip\7z.exe" a ScribeScroll.zip ScribeScroll || goto :error

"E:\Program Files\7-Zip\7z.exe" a ScribeScroll-Source.zip ..\..\*.cs || goto :error

"E:\Program Files\7-Zip\7z.exe" x ScribeScroll.zip -y -o"C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Kingmaker\Mods" || goto :error
copy ScribeScroll.pdb "C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Kingmaker\Mods\ScribeScroll"

goto :EOF

:error
echo Failed with error #%errorlevel%.
exit /b %errorlevel%
