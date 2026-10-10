# Artificial Life Simulator

Multicellular development simulator built with **Godot 4 (.NET)** and **C#**.
A single founder cell grows into a organism through gene regulation, morphogen
signalling, cell division, differentiation, death and regeneration.

- Visualisation contains **no biological rules** — all simulation logic lives in
  the pure C# library `src/SimulationCore` and runs headless.
- Genes, morphogens and cell types are described in JSON under `config/`.

## Prerequisites (Windows)

| Tool          | Version            | Install                                   |
|---------------|--------------------|-------------------------------------------|
| Godot .NET    | 4.7.2 (or 4.4+)    | `winget install GodotEngine.GodotEngine.Mono` |
| .NET SDK      | 8.0+ (10.x works)  | `winget install Microsoft.DotNet.SDK.10`  |
| Git           | any recent         | `winget install Git.Git`                  |

Godot executable (installed via winget):
`%LOCALAPPDATA%\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe`

## Build, test, run

```bat
:: restore + build everything (app, core, tests)
dotnet build Sim2.slnx

:: run unit tests
dotnet test Sim2.slnx

:: build only the core + tests (no Godot SDK needed)
dotnet build src\SimulationCore\SimulationCore.csproj

:: run the app (GUI)
godot --path E:\sim2

:: run the app headless self-test (no window; steps the sim and quits)
godot --path E:\sim2 --headless -- --selftest

:: open the Godot editor
godot -e --path E:\sim2
```

> If `godot` is not on PATH in a fresh shell, restart the terminal (winget adds
> the alias on install) or call the full path shown above.

## Controls

| Input               | Action                                   |
|---------------------|------------------------------------------|
| Left mouse          | select cell (Delete mode: remove cell)   |
| Middle/Right drag   | pan camera                               |
| Mouse wheel         | zoom                                     |
| Space               | pause / resume                           |
| `.` (period) / →    | single step while paused                 |
| R                   | reset experiment                         |
| Delete / Backspace  | remove selected cell                     |
| E                   | toggle delete mode (left click removes)  |
| WASD                | pan camera                               |

## Project layout

```
project.godot, Sim2.csproj   Godot app (rendering + UI only)
Sim2.slnx                   .NET solution (app + core + tests)
config/                     simulation / genomes / morphogens (JSON)
scripts/                    Godot front-end (Main, WorldView, Hud)
src/SimulationCore/         pure C# simulation core
src/SimulationCore.Tests/   NUnit tests
ARCHITECTURE.md, ROADMAP.md design documents
```

See `ARCHITECTURE.md` for the component model and `ROADMAP.md` for the plan.
