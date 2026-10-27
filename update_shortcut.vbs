Set ws = CreateObject("WScript.Shell")
Set s = ws.CreateShortcut("C:\Users\bbara\Desktop\Project XO.lnk")
s.TargetPath = "C:\Users\bbara\Desktop\ProjectXO.exe"
s.IconLocation = "C:\Users\bbara\Desktop\app_icon.ico,0"
s.Description = "Project XO // BOGDAN ZHUKOV - Zero-Leak Shield"
s.WorkingDirectory = "C:\Users\bbara\Desktop"
s.Save
