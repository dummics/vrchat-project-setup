Option Explicit

Dim args, commandLine, index, shell, exitCode

Set args = WScript.Arguments
If args.Count = 0 Then
  WScript.Quit 87
End If

commandLine = ""
For index = 0 To args.Count - 1
  If index > 0 Then
    commandLine = commandLine & " "
  End If
  commandLine = commandLine & QuoteArgument(args.Item(index))
Next

Set shell = CreateObject("WScript.Shell")
exitCode = shell.Run(commandLine, 0, True)
WScript.Quit exitCode

Function QuoteArgument(value)
  If InStr(value, " ") = 0 And InStr(value, vbTab) = 0 And InStr(value, Chr(34)) = 0 Then
    QuoteArgument = value
  Else
    QuoteArgument = Chr(34) & Replace(value, Chr(34), Chr(92) & Chr(34)) & Chr(34)
  End If
End Function
