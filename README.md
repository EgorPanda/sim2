# Sim2 — Artificial Life Simulator

Russian-language multicellular development simulator built with **Godot 4 (.NET)**
and **C#**. A single founder cell grows into an organism through gene regulation,
morphogen signalling, cell division, differentiation, death and regeneration.

- Visualisation contains **no biological rules** — all simulation logic lives in
  the pure C# library `src/SimulationCore` and runs headless.
- Genes, morphogens and cell types are described in JSON under `config/`.
- The **Laboratory** (research panel, `L`) lets you visualise the world across
  eight analytical layers and edit the live configuration: gene logic (AND/OR/NOT
  groups), morphogens, cell phenotypes and experiment settings — all in Russian.

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
dotnet test src\SimulationCore.Tests\SimulationCore.Tests.csproj

:: build only the core + tests (no Godot SDK needed)
dotnet build src\SimulationCore\SimulationCore.csproj

:: run the app (GUI) with the default world
godot --path E:\sim23

:: run the app with an experiment profile
godot --path E:\sim23 -- --experiment res://config/experiments/multicellular.json

:: run the app headless self-test (no window; steps the sim and quits)
godot --path E:\sim23 --headless -- --selftest
godot --path E:\sim23 --headless -- --selftest --experiment res://config/experiments/multicellular.json

:: automated UI smoke test -- opens the Laboratory, help (F1), routes
:: OpenHelp/OpenGene/OpenMorphogen, switches all 8 layers, applies a draft,
:: saves an experiment profile with an unusual name, then quits:
:: exit code 0 = all checks passed.
godot --path E:\sim23 --headless -- --uitest
godot --path E:\sim23 -- --uitest          :: same checks in a real window

:: open the Godot editor
godot -e --path E:\sim23
```

> If `godot` is not on PATH in a fresh shell, restart the terminal (winget adds
> the alias on install) or call the full path shown above.

> Verification status (2026-10-12): `dotnet build Sim2.slnx` clean; 214 NUnit tests
> pass (full suite ~2 minutes); `--selftest` and `--uitest` pass both headless and in a real window on
> Windows (no resource-leak messages on exit). The bundled `multicellular`,
> `differentiation`, `cell_memory` and `morphogen_spread` demo experiments are
> covered by tests and can be opened from «Настройки эксперимента» → «Открыть…».
> Old configs with `displayName`, `initialConcentration` or
> `initialInternalMorphogens` load transparently via migration. Interactive-only
> flows (mouse gestures, message boxes in the save/load dialogs) still need a human
> click.

## Controls

| Input               | Action                                   |
|---------------------|------------------------------------------|
| Left mouse          | select cell (Delete mode: remove cell)   |
| Middle/Right drag   | pan camera                               |
| Mouse wheel         | zoom                                     |
| Space               | pause / resume                           |
| `.` (period) / →    | single step while paused                 |
| R                  | reset experiment                         |
| Delete / Backspace  | remove selected cell                     |
| E                   | toggle delete mode (left click removes)  |
| L                   | open / close the Laboratory              |
| F1                  | open the built-in help browser           |
| Esc                 | close the Laboratory                     |
| WASD                | pan camera                               |

## What you can do

- **No built-in death**: cells never die of old age or starvation — death happens
  only through gene-driven effects («Апоптоз», manual deletion with `E` mode).
  Life, growth and death are fully gene-controlled.
- **Analytical layers** (left panel): cell types, external morphogen
  concentration (auto-normalised heat map + legend), gradient vectors, internal
  morphogen concentration per cell, active gene, silenced genes, neighbour
  density. Use **«Показать морфогены»**, **«Выбрать морфогены…»** and **«Посмотреть сквозь»**
  in the HUD to control the overlay and cell transparency.
- **Phenotypes** (right panel) lists live population counts per cell type.
- **Selected cell** (right panel) updates live: state, radius, age, neighbours,
  active/silenced genes (clickable — opens the genome editor), timers and local
  external/internal morphogen levels.
- **Laboratory** (`L`) — opens as a **separate resizable window** so it never covers
  the simulation. Five tabs:
  - *Геном*: create/copy/delete genomes and genes, edit name/role/description,
    mandatory morphogen trigger, AND/OR groups with NOT, thresholds (≥ / ≤ / range /
    = automatically derived from the stored interval), and the list of effects
    (emit/grow/divide/move/type switch/timers/silencing/apoptosis/division block);
    a compact **gene → morphogen quick-link** section edits mandatory triggers in one
    place; the «Деление» effect can set the **child phenotype**; the «Выделение морфогена»
    effect has an **EmissionRadius** for spatial spreading of external signals;
  - *Морфогены*: searchable list, copy, delete with automatic cleanup of every
    reference, name, role, type (External/Internal), diffusion, decay,
    division inheritance (Reset/Copy/Split for internal morphogens), colour;
    morphogen IDs are arbitrary integers, only External morphogens allocate spatial fields;
  - *Типы клеток*: all phenotype parameters + name/description + colour + linked genome;
  - *Настройки эксперимента*: world size, solver resolution, seed, founders,
    external division-gradient morphogen and initial external sources;
  - *Справочник*: Russian help browser with search and cross-links.
- **Morphogen picker** (HUD button «Выбрать морфогены…»): while the simulation is running,
  choose which external and internal morphogens are shown. External morphogens render
  as concentration fields; internal morphogens tint cells by their local concentration.
  The picker supports «All external», «All internal» and «None» presets.
- **Apply / Проверить / Восстановить / Сохранить / Сохранить как… / Открыть…** in the panel
  toolbar: changes live in a working draft until validated and applied; the world
  rebuilds; experiments can be saved to an experiment profile JSON and reloaded;
  genomes are created/copied/deleted right in the «Геном» tab.

## Project layout

```
project.godot, Sim2.csproj   Godot app (rendering + UI only)
Sim2.slnx                   .NET solution (app + core + tests)
config/                     simulation / genomes / morphogens / celltypes / adhesion (JSON)
config/experiments/         named experiment profiles (multicellular.json, differentiation.json, cell_memory.json, morphogen_spread.json)
config/experiments/differentiation/   demo «дифференцировка при делении»
scripts/                    Godot front-end (Main, Hud, WorldView, UiKit, ui/… editors)
src/SimulationCore/         pure C# simulation core (+ Localization, HelpCatalog, ConfigValidator, ConfigWriter, ConfigDraft)
src/SimulationCore.Tests/   NUnit tests
docs/USER_GUIDE_RU.md       user guide (Russian)
docs/REPORT_M1.6.md         M1.6 milestone report (types, trigger, cell memory)
ARCHITECTURE.md, ROADMAP.md design documents
```

See `ARCHITECTURE.md` for the component model, `ROADMAP.md` for the plan,
`docs/REPORT_M1.6.md` for the M1.6 report and
`docs/USER_GUIDE_RU.md` for the in-depth user manual.
