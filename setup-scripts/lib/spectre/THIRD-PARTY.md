# Spectre.Console runtime

This folder contains the .NET 8 runtime assemblies for Spectre.Console 0.57.2
and Spectre.Console.Ansi 0.57.2. They provide the optional enhanced terminal UI
when the wizard runs in PowerShell 7.

- Project: https://github.com/spectreconsole/spectre.console
- Packages: `Spectre.Console` and `Spectre.Console.Ansi`, version `0.57.2`
- License: MIT; see `LICENSE.md` in this folder.

Windows PowerShell 5.1 and terminals where these assemblies cannot be loaded
continue to use the built-in text-menu fallback.
