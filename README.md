# PC-Management

## 2.2
Addition:
- 'Toast' notification if user chooses 2. action
- Any countdown is now displayed in the console
- Action 2 now cancles nearest countdown
- The update dialog now has its own interactive window with a "Don't show this again" option. <br>

Fixed:
- Hibernation was not correctly detecting in some cases, now it checks multiple registry keys to ensure accuracy.
- Exiting application will not cancle the hibernation countdown (if its set, it has to be cancled by action 2)
- If an error occurs during the execution of a chain of actions, it is now logged to the console instead of crashing the application.<br>

## General
PCManager_App.exe is a portable app, everything it needs is in the .exe file
<br>
**no need for installation**

You can combine the options by input in menu 
<br>(f.e. 150 - You will set the time and choose if you want shutdown or hibermation --> Locks your PC --> Exits program)
<br>(you need to have hibernation option turned on in Control Panels for full funcionality)

Program has Czech and English UI
It offers 3 themes - Dark mode, Light 'Classic' mode and Cyberpunk

Console is generating what command will the program send to cmd, shows all countdowns and errors

Preview: <br>
<img width="632" height="1032" alt="image" src="https://github.com/user-attachments/assets/438556d8-8d5a-48bc-9d0e-61216d333d23" />
