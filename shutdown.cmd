@echo off
:menu
cls
echo Vyber akci:
echo 1 - Nastavit cas vypnuti
echo 2 - Nastavit cas hibernace
echo 3 - Zrusit naplanovane vypnuti
echo 4 - Spustit updaty (tady)
echo 5 - Spustit updaty (v novym okne)
echo 6 - Exit
set /p "volba=Zadejte volbu (1-6): "

if "%volba%"=="1" goto vypnuti
if "%volba%"=="2" goto hibernace
if "%volba%"=="3" goto zrusit
if "%volba%"=="4" goto updaty
if "%volba%"=="5" goto updaty-newWin
if "%volba%"=="6" goto exit

echo Nezadals číslo v rozmezí ty čůráku vole debílní
pause
goto menu



:vypnuti
cls
set /p "cas=Za jak dlouho se ma PC vypnout (v minutach): "
set /a sekundy=cas*60
set /a hodiny=cas / 60
set /a minuty=cas %% 60

shutdown -s -t %sekundy%
echo.
echo Vypnuti naplanovano za %hodiny%h %minuty%m
echo.

set /p "exit=Pro exit zadej 4: "
if "%exit%"=="4" goto exit
goto menu



:hibernace
cls
set /p "cas=Za jak dlouho ma PC hibernovat (v minutach): "
set /a sekundy=cas*60
set /a hodiny=cas / 60
set /a minuty=cas %% 60

echo.
echo Hibernace naplanovana za %hodiny%h %minuty%m
timeout /t %sekundy% /nobreak
shutdown /h
exit



:zrusit
cls
shutdown -a
echo.
echo Naplanovane vypnuti/hibernace bylo zruseno.
pause
goto menu


:updaty
winget upgrade --all --include-unknown --accept-source-agreements --accept-package-agreements --silent
pause
goto menu



:updaty-newWin
start cmd /c "winget upgrade --all --include-unknown --accept-source-agreements --accept-package-agreements --silent & pause"
goto menu



:exit
taskkill/im shutdown.cmd
exit
