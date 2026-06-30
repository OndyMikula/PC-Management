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
echo 6 - Nastavit delay
echo 7 - Menu
set /p "volba=Zadejte volbu (0-7): "


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
if "!char!"=="6" call :delay
if "!char!"=="7" goto menu

set /a i+=1
goto loop



:time
cls
set /p "cas=Zadej cas v minutach: "
if "!cas!"=="x" goto menu

echo.
echo Vyber akci pro tento cas:
echo 4 - Vypnuti
echo 5 - Hibernace
echo x - Zrusit a jit zpet
set /p "akce=Tvoje volba: "

if "!akce!"=="4" goto :vypnuti
if "!akce!"=="5" goto :hibernace
if "!akce!"=="x" goto menu

echo Spatna volba, zkus to znova.
pause
goto :time


:vypnuti
cls
set /a sekundy=%cas% * 60
set /a hodiny=%cas% / 60
set /a minuty=%cas% %% 60
echo CHYSTAS SE: Vypnout PC za %hodiny%h %minuty%m.
set /p "confirm=Je to spravne? (1/0): "
if /i not "!confirm!"=="1" goto menu

shutdown -s -t %sekundy%
echo.
goto :eof


::echo Pro exit zadej 4: 
::set /p "exit=Pro zamknuti PC zadej 5: "

::if "%exit%"=="4" exit
::if "%exit%"=="5" call lock
goto :eof



:hibernace
cls
set /a sekundy=%cas% * 60
set /a hodiny=%cas% / 60
set /a minuty=%cas% %% 60
echo CHYSTAS SE: Hibernovat PC za %hodiny%h %minuty%m.
echo (Pozor: Hibernaci zrusis zavrenim toho noveho okna s odpocitem!)
echo.
set /p "confirm=Je to spravne? (1/0): "
if /i not "!confirm!"=="1" goto menu

:: Spoustime v novem okne s titulkem, aby se to dalo poznat
start "HIBERNACE_ODPOCET" cmd /c "echo PC bude hibernovan za %cas% minut. Pro zruseni zavri tohle okno! & timeout /t %sekundy% /nobreak & shutdown /h"
goto :eof



:zrusit
cls
shutdown -a
echo.
echo Naplanovane vypnuti bylo zruseno.
echo Pokud mas spusteny odpocet na hibernaci, musis zavrit to druhe cerne okno!
pause
goto :eof



:updaty

:: Tento řádek zajistí, že se okno automaticky připne navrch obrazovky:
powershell -Command "$s='[DllImport(\"user32.dll\")]public static extern IntPtr GetForegroundWindow();[DllImport(\"user32.dll\")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f);';$t=Add-Type -MemberDefinition $s -Name 'W' -PassThru;$h=$t::GetForegroundWindow();$t::SetWindowPos($h,-1,0,0,0,0,3)"

winget upgrade --all --include-unknown --accept-source-agreements --accept-package-agreements --silent
pause
goto :eof



:updaty-newWin

:: Tento řádek zajistí, že se okno automaticky připne navrch obrazovky (PiP):
powershell -Command "$s='[DllImport(\"user32.dll\")]public static extern IntPtr GetForegroundWindow();[DllImport(\"user32.dll\")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f);';$t=Add-Type -MemberDefinition $s -Name 'W' -PassThru;$h=$t::GetForegroundWindow();$t::SetWindowPos($h,-1,0,0,0,0,3)"

start cmd /c "winget upgrade --all --include-unknown --accept-source-agreements --accept-package-agreements --silent & pause"
goto :eof



:lock
rundll32.exe user32.dll,LockWorkStation ::zamknuti PC
goto :eof



:delay
cls
set /p "delay_cas=Zadej delay v minutach: "
set /a delay_sekundy=!delay_cas! * 60
echo.
echo Cekam !delay_cas! minut
timeout /t !delay_sekundy! /nobreak
goto :eof
