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
- `WorldView` — draws cells (circle per cell, colour by type/state) and an optional
  morphogen heat-map layer for the selected field (auto-normalised). Read-only.
- `Hud` — transport (pause / step / reset), speed, cell radius, initial conditions,
  morphogen **field selector**, delete tool, per-**phenotype statistics** and a
  **selected-cell inspector** (state, energy, radius, age, neighbours, active/silenced
  genes, timers, local morphogen levels).
- Camera: continuous pan/zoom via `Camera2D`.

### SaveSystem
- Versioned JSON DTO (v2) capturing cells (with `GenomeId`, gene state, timers, silenced
  genes, `DivisionBlocked`), the morphogen fields, the RNG state and world time. A load
  continues bit-for-bit identically to the saved run.

### Tests
- `SimulationCore.Tests` (NUnit) validates diffusion stability & conservation properties,
  decay, spatial-hash correctness, division rules, morphogen sampling, gene regulation
  (inversion, AND logic, timers, silencing, apoptosis, division blocking), tissue mechanics
  (adhesion, directed division, inheritance) and deterministic reproducibility, plus the
  multicellular experiment profile.

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
                  GenomeSystem.Evaluate  (active genes -> effects; gradient available)
                  MorphogenSystem.Update (emission -> diffusion/decay)
                  CellMechanics.Resolve  (de-overlap with tissue adhesion)
                  CellSystem.ResolveDivisions (energy + free space + gradient bias)
WorldView._Draw reads Simulation.Cells + MorphogenSystem.GetField(selected)
Hud reads counts + the selected cell's regulation state (formatting only)
```

## 5. Key invariants

- Core has no dependency on Godot (`SimulationCore.csproj` references only the BCL).
- Cell positions are continuous floats; the morphogen grid is a computational aid only.
- No hard-coded limit on the number of cell types, genes, morphogens or rules.
- Phenotypes reference a shared genome; a phenotype change never mutates the genome.
- All randomness flows through the seeded RNG → reproducible runs (save/load preserves it).
- The fixed update order is deterministic and covered by tests.

## 6. Project layout

```
project.godot            Godot 4 (.NET) project file
Sim2.csproj              Godot C# assembly (references SimulationCore)
Sim2.slnx                .NET solution (app + core + tests)
config/                  simulation, morphogens, genomes, celltypes, adhesion
config/experiments/      named experiment profiles (e.g. multicellular)
scripts/                 Godot front-end (Main, WorldView, Hud)
scenes/                  Main.tscn
src/SimulationCore/      pure C# simulation library
src/SimulationCore.Tests/NUnit tests
docs:                    ARCHITECTURE.md, ROADMAP.md, README.md
```

## 7. Extension model

| To add…            | Do this                                                          |
|--------------------|------------------------------------------------------------------|
| a morphogen        | add an entry to `config/morphogens.json` (id must be unique)     |
| a gene             | add it to a genome in `config/genomes.json`                      |
| a phenotype        | add an entry to `config/celltypes.json` (set its `genomeId`)     |
| an adhesion pair   | add it to `config/adhesion.json`                                 |
| an experiment      | add a folder + profile under `config/experiments/`               |
| a new effect/rule  | handle a new `GeneEffectConfig.Kind` in `GenomeSystem`           |
| a new regulator    | handle a new `RegulatorConfig.Kind` in `GenomeSystem`            |
