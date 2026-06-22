Mangrove - Windows server
==========================

Quick start
-----------
1. Extract this whole folder somewhere permanent, e.g. C:\Mangrove
   (Do NOT run it from inside the .zip - extract first.)
2. Double-click  Install-Mangrove.bat  and approve the administrator prompt.
   This installs the "Mangrove" Windows service, starts it, and sets it to
   start automatically on boot.
3. Open the web UI in your browser:
       http://localhost:5000
   From another device on your network use the PC's name or IP, e.g.
       http://YOUR-PC-NAME:5000

The first time you open it you'll be guided through creating the admin
account and adding your library.

Managing the service
--------------------
Run these from an elevated (Administrator) PowerShell/Command Prompt in this
folder, or just use the .bat files:

    Mangrove.exe install     install + start the service (Install-Mangrove.bat)
    Mangrove.exe start       start the service
    Mangrove.exe stop        stop the service
    Mangrove.exe restart     restart the service
    Mangrove.exe status      show service status
    Mangrove.exe uninstall   stop + remove the service (Uninstall-Mangrove.bat)

Changing the port
-----------------
The server listens on port 5000 by default. To use a different port, set the
MANGROVE_PORT environment variable for the service, then restart it. Example
(elevated PowerShell):

    [Environment]::SetEnvironmentVariable("MANGROVE_PORT","8080","Machine")
    Mangrove.exe restart

Data location
-------------
The database and cache are stored next to Mangrove.exe, so keep the folder in
a permanent location.
