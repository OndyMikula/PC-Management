@echo off
setlocal enabledelayedexpansion ::pro cyklus v vyber

:menu
cls
echo Vyber akci:
echo 0 - Exit
echo 1 - Nastavit cas
echo 2 - Zrusit naplanovane vypnuti
echo 3 - Spustit updaty (tady)
echo 4 - Spustit updaty (v novym okne)
echo 5 - Zamknout PC
echo 6 - Menu
set /p "volba=Zadejte volbu (0-6): "


:: Projde kazdy znak ze zadane sekvence
set i=0
:loop
set "char=!volba:~%i%,1!"
if "!char!"=="" goto menu

if "!char!"=="0" exit
if "!char!"=="1" call :time
if "!char!"=="2" call :zrusit
if "!char!"=="3" call :updaty
if "!char!"=="4" call :updaty-newWin
if "!char!"=="5" call :lock
if "!char!"=="6" goto menu

set /a i+=1
goto loop



:time
cls ::clear screen
set /p "cas=Zadej cas v minutach: "
echo.
echo Pro planovani vypnuti zvol 4: 
set /p "shutdown=Pro planovani hibernace zvol 5: "

if "%shutdown%"=="4" goto :vypnuti
if "%shutdown%"=="5" goto :hibernace

echo Zvolils spatne cislo, zkus to znova
pause
goto :eof ::end of file, hodi zpatky tam od kama jsi prisel



:vypnuti
cls
echo Zvolil sis Vypnuti
set /a sekundy=%cas% * 60 ::pro command
set /a hodiny=%cas% / 60 ::oboje pro echo
set /a minuty=%cas% %% 60

shutdown -s -t %sekundy%
echo.
echo Vypnuti naplanovano za %hodiny%h %minuty%m
echo.


echo Pro exit zadej 4: 
set /p "exit=Pro zamknuti PC zadej 5: "

if "%exit%"=="4" exit
if "%exit%"=="5" call lock
goto :eof



:hibernace
cls
echo Zvolil sis Hibernaci
set /a sekundy=cas*60
set /a hodiny=cas / 60
set /a minuty=cas %% 60

echo.
echo Hibernace naplanovana za %hodiny%h %minuty%m

::set /p "lock=Jestli chces pred spustenim casovace zamknout PC, zadej 5: "
::if "!char!"=="5" call :lock
::if "%lock%"=="5" rundll32.exe user32.dll,LockWorkStation

start cmd /c "timeout /t %sekundy% /nobreak & shutdown /h"
::shutdown /h
goto :eof



:zrusit
cls
shutdown -a
echo.
echo Naplanovane vypnuti/hibernace bylo zruseno.
pause
goto :eof



:updaty
winget upgrade --all --include-unknown --accept-source-agreements --accept-package-agreements --silent
pause
goto :eof



:updaty-newWin
start cmd /c "winget upgrade --all --include-unknown --accept-source-agreements --accept-package-agreements --silent & pause"
goto :eof



:lock
rundll32.exe user32.dll,LockWorkStation ::zamknuti PC
goto :eof
