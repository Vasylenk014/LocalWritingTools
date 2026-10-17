Set WshShell = CreateObject("WScript.Shell")
Set FSO = CreateObject("Scripting.FileSystemObject")
currentDir = FSO.GetParentFolderName(WScript.ScriptFullName)
exePath = currentDir & "\publish-standalone\WritingTools.exe"
WshShell.Run Chr(34) & exePath & Chr(34), 0, False
Set WshShell = Nothing
Set FSO = Nothing
