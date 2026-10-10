# Architecture — Artificial Life Simulator

## 1. Goals

Simulate the development of multicellular organisms from a single founder cell using a
simplified ontogenesis model:

- All cells share one genome.
- Cells regulate gene expression through **morphogens** (diffusing chemical signals).
- Cells interact mechanically, **divide**, **differentiate**, **die** and **regenerate**.
- Reproduction works through spawning new organisms.

The visualisation layer must contain **no biological rules**. The simulation core must run
headless and independently from the interface.

## 2. Layered component view

```
+--------------------------------------------------------------+
|                        Godot front-end                        |
|   Main (orchestrator)  WorldView (draw)  Hud (controls)       |
|   owns a Simulation instance, reads state, submits commands   |
+----------------------------|----------------------------------+
                             | plain C# API (no Godot types)
+----------------------------v----------------------------------+
|                        SimulationCore                          |
|                                                                |
|  Simulation (world state + step orchestration)                 |
|    |-- CellSystem          cells, division, differentiation,   |
|    |                       apoptosis                            |
|    |-- GenomeSystem        genes, regulatory conditions,        |
|    |                       activation delay, effects            |
|    |-- MorphogenSystem     emission, diffusion, decay, sampling |
|    |-- CellMechanicsSystem overlap resolution, tissue cohesion  |
|    |-- SpatialHash         neighbour lookup (no O(n^2))         |
|    |-- ConfigLoader        JSON -> typed config                |
|    |-- SaveSystem          versioned world serialisation       |
|    +-- SimulationConfig / MorphogenConfig / GenomeConfig /     |
|        CellTypeConfig / AdhesionConfig / ExperimentConfig     |
+----------------------------------------------------------------+
```

## 3. Component responsibilities

### SimulationCore
- Holds the authoritative world state: list of cells, morphogen field, tick counter, sim time.
- `Step(dt)` runs the fixed update order: spatial index → genome/metabolism → morphogen
  emission → diffusion/decay → mechanics → division/apoptosis → cleanup.
- Owns the deterministic RNG (seeded) for reproducibility.

### CellSystem
- `Cell`: id, continuous position, radius, type, energy, state (Alive / Dying),
  age, neighbour count, per-gene state (`GeneState`: active flag + activation timer),
  named `Timers`, a `SilencedGenes` set and a `DivisionBlocked` flag.
- Division: a cell divides when its `DivisionProgress` reaches 1 (driven by the
  `Divide` gene effect) with enough energy, free space and no active `BlockDivision`.
  Daughters inherit the parent's phenotype, genome and regulation state
  (`CopyRegulationStateTo`: active genes, timers, silenced genes).
- Apoptosis: starvation (energy below zero for a grace period) or the `Apoptosis` effect.
- Differentiation: the `SetCellType` effect switches a cell's `TypeId`; the genome
  stays shared.

### GenomeSystem
- `GenomeConfig` = ordered list of `GeneConfig`; a `CellTypeConfig` (phenotype) only
  references a genome via `GenomeId`, so many phenotypes share one genome.
- `GeneConfig`: `Key` (stable id/name), regulators (AND-combined), activation `Delay`,
  and effects. Per-gene state is keyed by `Key`, so gene order is not significant.
- Regulators: `Morphogen`, `Property` (`energy`, `radius`, `age`, `neighbours`),
  `Phenotype`, `State`, `Timer` — each optionable with `invert` (negation).
- Effects: `EmitMorphogen`, `Absorb`, `Grow`, `Divide`, `SetCellType`, `StartTimer`,
  `Apoptosis`, `SilenceGene`, `BlockDivision`. Effects may be `once` (run only on the
  step a gene activates). Timers are not restarted while running. Probabilistic
  apoptosis is dt-invariant (`1 - exp(-p·dt)`).
- Genes are data, not code: adding genes / morphogens / cell types is a JSON change.

### MorphogenSystem
- A **separate** uniform grid (world rect + resolution) that does not constrain cell
  positions. Each morphogen has its own scalar field.
- Update: explicit diffusion (`D * laplacian`) + exponential decay + accumulated emission.
  The number of sub-steps is chosen automatically for stability
  (`D*dt/dx^2 <= 0.25`).
- `Sample(x, y)` gives smooth bilinear concentration; `Gradient(x, y)` gives a
  central-difference vector (used to bias division direction), never NaN.
- The field count spans up to the largest configured morphogen id (ready for ~12).

### CellMechanicsSystem
- Prevents overlaps: pairwise, position-based separation of overlapping circles using the
  spatial hash for neighbourhoods (O(n·k)).
- Tissue cohesion: per-type-pair adhesion strength reduces the rest distance to
  `touch · (1 - maxOverlap · adhesion)`, so coupled cells settle without oscillating.

### SpatialHash
- Uniform grid of buckets keyed by `(int,int)`; supports `QueryCircle(x, y, r)` and
  `QueryNeighbours(id, r)`.
- Rebuilt every step from current positions (cheap) with `cellSize >= 2 * maxRadius`.

### SimulationUI (Godot)
- `Main` — owns `Simulation`, accumulates real time, calls `Step`, hosts camera + children.
  Loads the base world from `res://config` or an experiment via `--experiment <path>`.
  Holds a **working draft** (`SimulationSetup` deep clone) edited by the Laboratory;
  «Применить» validates it and rebuilds the world. `WorldView` and `Hud` stay read-only.
- `WorldView` — draws cells (circle per cell, colour by type/state) plus **seven
  analytical layers**: cell types, morphogen concentration (auto-normalised heat map,
  legend, optional cell tinting, absolute scale override), gradient vectors, gene
  activity, silenced genes, energy, neighbour density. Read-only.
- `Hud` (Russian) — transport (pause / step / reset), speed, layer selector, delete tool,
  laboratory toggle, help button; per-**phenotype statistics**, a **selected-cell
  inspector** (clickable genes open the editor), a legend strip and notifications.
- `ui/ResearchPanel` — the **Laboratory** (`L`): tabs «Мир», «Геном», «Морфогены»,
  «Типы клеток», «Настройки эксперимента», «Справочник»; toolbar Применить / Проверить /
  Восстановить / Сохранить / Загрузить….
- `ui/GenomeEditor` — gene list + card: display data, AND/OR **groups** (OR between
  groups, AND inside, NOT per condition), thresholds ≥ / ≤ / range / = (derived from
  the stored `[min, max]`), effects list. `ui/MorphogenEditor`, `ui/CellTypeEditor`,
  `ui/ExperimentEditor` edit the equivalent models; deletes are guarded by
  `ConfigDraft` reference discovery.
- `ui/HelpPanel` — help browser over the central `HelpCatalog` in the core.
- Camera: continuous pan/zoom via `Camera2D`.

### Localization, help and configuration tooling (SimulationCore)
- `Localization` — centralised Russian display names for regulators, effects,
  properties, states, genes/morphogens/types; `DescribeRange` renders the operator
  notation (≥/≤/…/=) for the stored interval.
- `HelpCatalog` — ~50 Russian articles (keys `concept.*`, `logic.*`, `op.*`,
  `regulator.*`, `effect.*`, `property.*`, `param.*`, `experiment.*`, `celltype.*`,
  `timer`) with search and related-key resolution.
- `ConfigValidator` — returns `ConfigIssue[]` (severity, section, file location,
  message, fix hint); used by the panel before applying. `ConfigWriter` serialises a
  setup to experiment profile files. `ConfigDraft` — unique gene ids, gene copy, and
  reference discovery for guarded deletes. All live in the core so coverage is testable.

### SaveSystem
- Versioned JSON DTO (v2) capturing cells (with `GenomeId`, gene state, timers, silenced
  genes, `DivisionBlocked`), the morphogen fields, the RNG state and world time. A load
  continues bit-for-bit identically to the saved run.

### Tests
- `SimulationCore.Tests` (NUnit) validates diffusion stability & conservation properties,
  decay, spatial-hash correctness, division rules, morphogen sampling, gene regulation
  (inversion, AND/OR groups, timers, silencing, apoptosis, division blocking), tissue mechanics
  (adhesion, directed division, inheritance) and deterministic reproducibility, plus the
  multicellular experiment profile. M1.5 adds coverage for regulator groups,
  configuration validation, serialization round-trips, localization completeness and
  help-catalog completeness (every regulator/effect/property has an article; related
  links resolve).

## 4. Data flow

```
config/*.json --ConfigLoader--> SimulationSetup
                 (SimulationConfig + MorphogenConfig[] + GenomeConfig[]
                  + CellTypeConfig[] + AdhesionConfig)
                 experiments/*.json --> LoadExperiment (files resolved
                  relative to the experiment directory)
Simulation.Reset(founder) --init--> cells + morphogen field
each frame: Main accumulates dt*speed -> Simulation.Step(dt)
Simulation.Step:  SpatialHash.Rebuild + neighbour counts
                  CellSystem.Metabolism  (energy, ageing, starvation)
                  GenomeSystem.Evaluate  (regulator groups -> active genes -> effects)
                  MorphogenSystem.Update (emission -> diffusion/decay)
                  CellMechanics.Resolve  (de-overlap with tissue adhesion)
                  CellSystem.ResolveDivisions (energy + free space + gradient bias)
WorldView._Draw reads Simulation.Cells + MorphogenSystem fields (layer-dependent)
Hud reads counts + the selected cell's regulation state (formatting only)

Laboratory (ui/ResearchPanel): editors mutate WorkingDraft (deep clone)
  -> ConfigValidator.Validate(draft) -> on apply: new Simulation(validated setup)
  -> ConfigWriter.SaveExperiment(profile, draft) persists to JSON files
  -> ConfigDraft.Find*References guards deletes
```

## 5. Key invariants

- Core has no dependency on Godot (`SimulationCore.csproj` references only the BCL).
- Cell positions are continuous floats; the morphogen grid is a computational aid only.
- No hard-coded limit on the number of cell types, genes, morphogens or rules.
- Phenotypes reference a shared genome; a phenotype change never mutates the genome.
- All randomness flows through the seeded RNG → reproducible runs (save/load preserves it).
- The fixed update order is deterministic and covered by tests.
- The UI never mutates the running world: editors change a deep-cloned draft that is
  validated and applied atomically (world rebuild).

## 6. Project layout

```
project.godot            Godot 4 (.NET) project file
Sim2.csproj              Godot C# assembly (references SimulationCore)
Sim2.slnx                .NET solution (app + core + tests)
config/                  simulation, morphogens, genomes, celltypes, adhesion
config/experiments/      named experiment profiles (e.g. multicellular)
scripts/                 Godot front-end (Main, WorldView, Hud, UiKit, ui/* editors)
scenes/                  Main.tscn
src/SimulationCore/      pure C# simulation library (+ Localization, HelpCatalog,
                         ConfigValidator, ConfigWriter, ConfigDraft)
src/SimulationCore.Tests/NUnit tests
docs:                    ARCHITECTURE.md, ROADMAP.md, README.md, USER_GUIDE_RU.md
```

## 7. Extension model

| To add…           | Do this                                                          |
|-------------------|------------------------------------------------------------------|
| a morphogen       | add an entry to `config/morphogens.json` or in the Laboratory      |
| a gene            | add it to a genome in `config/genomes.json` or in the Геном tab    |
| a phenotype       | add an entry to `config/celltypes.json` (set its `genomeId`)       |
| an adhesion pair  | add it to `config/adhesion.json`                                   |
| an experiment     | add a folder + profile under `config/experiments/`, or save from the panel |
| a new effect/rule | handle a new `GeneEffectConfig.Kind` in `GenomeSystem`             |
| a new regulator   | handle a new `RegulatorConfig.Kind` in `GenomeSystem`              |
| help content      | add a `HelpArticle` in `HelpCatalog.Data` (+ tests cover search/link resolution) |

Note on regulation groups: `GeneConfig.RegulatorGroups` is optional. When absent,
the legacy `Regulators` list acts as a single AND-group (backward compatible).
Within a group all regulators must match (AND), groups are combined with OR, an
empty group is vacuously satisfied, and an unknown regulator evaluates to false
(and is flagged by the validator).
