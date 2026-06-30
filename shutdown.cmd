@echo off
setlocal enabledelayedexpansion

:menu
cls
echo =========================================
echo             PC Management
echo =========================================
echo 0 - Exit
echo 1 - Nastavit cas (Vypnuti/Hibernace)
echo 2 - Zrusit naplanovane vypnuti
echo 3 - Spustit updaty (tady)
echo 4 - Spustit updaty (v novym okne)
echo 5 - Zamknout PC
echo 6 - Menu
echo 7 - Pridat delay
echo =========================================
set /p "volba=Zadejte volbu (0-7): "

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
if "!char!"=="7" call :delay

set /a i+=1
goto loop

:time
cls
set /p "cas=Zadej cas v minutach (nebo 'x' pro zpet): "
if "!cas!"=="x" goto menu

echo.
echo Vyber akci pro tento cas:
echo 4 - Vypnuti
echo 5 - Hibernace
echo x - Zrusit a jit zpet
set /p "akce=Tvoje volba: "

if "!akce!"=="x" goto menu
if "!akce!"=="4" goto :vypnuti
if "!akce!"=="5" goto :hibernace

echo Spatna volba, zkus to znova.
pause
goto :time

:vypnuti
cls
:: Kontrola pro okamzite vypnuti
if "!cas!"=="0" (
    echo CHYSTAS SE: Vypnout PC HNED TED!
    set /p "confirm=Je to spravne? (j/n): "
    if /i not "!confirm!"=="j" goto menu
    shutdown -s -t 0
    goto :eof
)

set /a sekundy=%cas% * 60
set /a hodiny=%cas% / 60
set /a minuty=%cas% %% 60
echo CHYSTAS SE: Vypnout PC za %hodiny%h %minuty%m.
set /p "confirm=Je to spravne? (j/n): "
if /i not "!confirm!"=="j" goto menu

shutdown -s -t %sekundy%
echo.
echo Vypnuti naplanovano. (Zrusit ho muzes volbou 2 v menu)
pause
goto :eof

:hibernace
cls
:: Kontrola pro okamzitou hibernaci
if "!cas!"=="0" (
    echo CHYSTAS SE: Hibernovat PC HNED TED!
    set /p "confirm=Je to spravne? (j/n): "
    if /i not "!confirm!"=="j" goto menu
    shutdown /h
    goto :eof
)

set /a sekundy=%cas% * 60
set /a hodiny=%cas% / 60
set /a minuty=%cas% %% 60
echo CHYSTAS SE: Hibernovat PC za %hodiny%h %minuty%m.
echo (Pozor: Hibernaci zrusis zavrenim toho noveho okna s odpocitem!)
echo.
set /p "confirm=Je to spravne? (j/n): "
if /i not "!confirm!"=="j" goto menu

:: Spoustime v novem okne s titulkem, aby se to dalo poznat
start "HIBERNACE_ODPOCET" cmd /c "echo PC bude hibernovan za %cas% minut. Pro zruseni zavri tohle okno! & timeout /t %sekundy% /nobreak & shutdown /h"
goto :eof

:zrusit
cls
shutdown -a
echo.
echo Prikaz 'shutdown -a' byl odeslan (zastavi klasicke vypnuti).
echo Pokud mas spusteny odpocet na hibernaci, musis zavrit to druhe cerne okno!
pause
goto :eof

:updaty
winget upgrade --all --include-unknown --accept-source-agreements --accept-package-agreements --silent
pause
goto :eof

:updaty-newWin
start "UPDATY" cmd /c "winget upgrade --all --include-unknown --accept-source-agreements --accept-package-agreements --silent & pause"
goto :eof

:lock
rundll32.exe user32.dll,LockWorkStation
goto :eof

:delay
cls
set /p "delay_cas=Zadej delay v minutach: "
set /a delay_sekundy=!delay_cas! * 60
echo.
echo Cekam !delay_cas! minut(y) nez spustim dalsi prikaz...
timeout /t !delay_sekundy! /nobreak
goto :eof
