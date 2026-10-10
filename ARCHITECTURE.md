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
|    |                       activation delay                     |
|    |-- MorphogenSystem     emission, diffusion, decay, sampling |
|    |-- CellMechanicsSystem overlap resolution, tissue cohesion  |
|    |-- SpatialHash         neighbour lookup (no O(n^2))         |
|    |-- ConfigLoader        JSON -> typed config                |
|    +-- SimulationConfig / GenomeConfig / MorphogenConfig       |
+----------------------------------------------------------------+
```

## 3. Component responsibilities

### SimulationCore
- Holds the authoritative world state: list of cells, morphogen field, tick counter, sim time.
- `Step(dt)` runs the fixed update order: spatial index → genome/metabolism → morphogen
  emission → diffusion/decay → mechanics → division/apoptosis → cleanup.
- Owns the deterministic RNG (seeded) for reproducibility.

### CellSystem
- `Cell`: id, continuous position, radius, type, energy, state (Alive / Dividing / Dying),
  age, per-cell gene state (activation timers).
- Division: budget cell grows to a maximum radius; when it has enough energy and there is
  free space (checked through CellMechanics + SpatialHash) it splits at a random angle into
  two daughter cells that inherit the genome.
- Apoptosis: starvation (energy below zero for a grace period) or explicit removal.
- Differentiation: switching a cell's `CellType` is supported by the data model; v0.1 uses a
  single type.

### GenomeSystem
- `Genome` = ordered list of `Gene`.
- `Gene`: id, list of `RegulatorCondition` (AND-combined), activation `Delay`, and a list of
  `GeneEffect`.
- Effects implemented in v0.1:
  - `EmitMorphogen(morphogenId, rate)` — secrete a morphogen while active.
  - `Grow(rate)` — convert energy into size (up to the type's max radius).
  - `Absorb(morphogenId, rate)` — draw energy from a sampled morphogen concentration.
  - `Divide` — accumulate division progress; triggers division when progress >= 1.
- Regulator conditions:
  - `Morphogen(id, min, max)` — sampled concentration in range.
  - `Property(name, min, max)` — cell property (`energy`, `radius`, `age`, `neighbours`).
- Genes are data, not code: adding genes / morphogens / cell types is a JSON change.

### MorphogenSystem
- A **separate** uniform grid (world rect + resolution) that does not constrain cell
  positions. Each morphogen has its own scalar field (`float[]`).
- Update: explicit diffusion (`D * laplacian`) + exponential decay + accumulated emission.
  The number of sub-steps is chosen automatically for stability
  (`D*dt/dx^2 <= 0.25`).
- `SampleBilinear(x, y)` gives cells a smooth concentration value.
- v0.1 ships with one morphogen (`nutrient`); the storage is `count x cells`, ready for ~12.

### CellMechanicsSystem
- Prevents overlaps: pairwise, position-based separation of overlapping circles using the
  spatial hash for neighbourhoods (O(n·k)).
- Optional soft tissue cohesion (spring-like pull) is prepared but off by default.

### SpatialHash
- Uniform grid of buckets keyed by `(int,int)`; supports `QueryCircle(x, y, r)` and
  `QueryNeighbours(id, r)`.
- Rebuilt every step from current positions (cheap) with `cellSize >= 2 * maxRadius`.

### SimulationUI (Godot)
- `Main` — owns `Simulation`, accumulates real time, calls `Step`, hosts camera + children.
- `WorldView` — draws cells (circle per cell, colour by type/state) and an optional morphogen
  heat-map layer. Read-only over core state.
- `Hud` — pause/step/reset, speed, cell radius, morphogen overlay toggle, live stats
  (cells, FPS, sim time), delete-cell tool.
- Camera: continuous pan/zoom via `Camera2D`.

### SaveSystem
- v0.1: JSON load of configuration (genes/morphogens/world). Save/load of full world state is
  on the roadmap (ROADMAP.md, M2).

### Tests
- `SimulationCore.Tests` (NUnit) validates diffusion stability & conservation properties,
  decay, spatial-hash correctness, division rules, morphogen sampling and deterministic
  reproducibility (same seed → identical state).

## 4. Data flow

```
config/*.json --ConfigLoader--> SimulationConfig + GenomeConfig[] + MorphogenConfig[]
Simulation.Reset(spawn) --init--> cells + morphogen field
each frame: Main accumulates dt*speed -> Simulation.Step(dt)
Simulation.Step:  SpatialHash.Rebuild
                  CellSystem.Update     (energy, growth, apoptosis)
                  GenomeSystem.Evaluate (activation -> effects)
                  MorphogenSystem.Update (emission -> diffusion/decay)
                  CellMechanics.Resolve  (de-overlap)
                  CellSystem.ResolveDivisions
WorldView._Draw reads Simulation.Cells + MorphogenSystem.GetField
```

## 5. Key invariants

- Core has no dependency on Godot (`SimulationCore.csproj` references only the BCL).
- Cell positions are continuous floats; the morphogen grid is a computational aid only.
- No hard-coded limit on the number of cell types, genes, morphogens or rules.
- All randomness flows through the seeded RNG → reproducible runs.
- The fixed update order is deterministic and covered by tests.

## 6. Project layout

```
project.godot            Godot 4 (.NET) project file
Sim2.csproj              Godot C# assembly (references SimulationCore)
Sim2.sln                 solution (app + core + tests)
config/                  simulation.json, genomes.json, morphogens.json
scripts/                 Godot front-end (Main, WorldView, Hud)
scenes/                  Main.tscn
src/SimulationCore/      pure C# simulation library
src/SimulationCore.Tests/NUnit tests
docs:                    ARCHITECTURE.md, ROADMAP.md, README.md
```

## 7. Extension model

| To add…            | Do this                                                        |
|--------------------|----------------------------------------------------------------|
| a morphogen        | add an entry to `config/morphogens.json` (id must be unique)   |
| a gene             | add it to a genome in `config/genomes.json`                    |
| a cell type        | add a genome with its own parameters                           |
| a new effect/rule  | add a `GeneEffect` type in `GenomeSystem` (open/closed enum)   |
| a new regulator    | add a `RegulatorCondition` kind in `GenomeSystem`              |
