# Roadmap — Artificial Life Simulator

Legend: `[x]` done, `[~]` in progress, `[ ]` planned.

## M0 — Runnable prototype

- [x] Environment check (Godot 4.7.2 .NET, .NET SDK, Git)
- [x] Layered architecture: SimulationCore (pure C#) / Godot front-end / NUnit tests
- [x] Continuous 2D cell coordinates, spatial-hash neighbour queries
- [x] One founder cell, deterministic seeded RNG
- [x] Gene model (regulators, activation delay, effects) driven by JSON
- [x] One morphogen (`nutrient`): emission, diffusion, decay, bilinear sampling
- [x] Division, growth, starvation/apoptosis rules
- [x] Renderer: cell types/states, morphogen overlay, camera pan/zoom
- [x] HUD: pause / step / speed / reset / manual cell deletion, live stats
- [x] Tests: diffusion, sampling, spatial hash, mechanics, division, reproducibility

## M1 — Multicellularity basics (current release)

- [x] Multiple cell types (stem / worker / skin) sharing one genome
- [x] Differentiation: genes can change a cell's type id (`SetCellType`)
- [x] Shared `GenomeConfig` referenced by phenotypes (`CellTypeConfig.GenomeId`)
- [x] Up to 12 morphogens with activation thresholds (French-flag style bands)
- [x] Chemotaxis: division biased towards a morphogen gradient
- [x] Tissue cohesion (adhesion strengths per cell-type pair, controlled overlap)
- [x] Regulators: Morphogen / Property / Phenotype / State / Timer (+ `invert`, AND-combined)
- [x] Effects: Emit / Absorb / Grow / Divide / SetCellType / StartTimer / Apoptosis /
      SilenceGene / BlockDivision, with once-per-activation semantics
- [x] Named cell timers; probabilistic (dt-invariant) apoptosis
- [x] Regulation state (active genes, timers, silenced genes) inherited on division
- [x] Experiment profiles under `config/experiments/` (self-contained file sets)
- [x] Full world save/load, versioned DTO (SaveSystem v2)
- [x] Research UI: morphogen field selector, per-phenotype stats, cell inspector

## M1.6 — Morphogen-triggered genes and cellular memory (current release)

- [x] Mandatory morphogen trigger for newly-created genes (`RequireMorphogenTrigger`,
      `RequiredMorphogenId`, `RequiredMorphogenThreshold`)
- [x] Trigger works for external (field sample) and internal (per-cell) morphogens
- [x] Legacy genes without the trigger field keep historical always-on behaviour
- [x] Continuous morphogen production while a gene is active; `once` mode for single
      impulse effects
- [x] Internal vs external morphogen types (`MorphogenConfig.Type`)
- [x] Per-cell internal morphogen concentrations (`Cell.InternalMorphogenConcentrations`)
- [x] Local internal production, decay and inheritance on division
- [x] Initial external sources and founder internal concentrations (`SimulationConfig`)
- [x] Save/load internal concentrations (SaveSystem v3); old v2 saves load with empty state
- [x] Genome editor: mandatory trigger section, status text, gene filter, morphogen link
- [x] Morphogen editor: type selector, conditional fields, type display in list
- [x] New analytical layer: internal morphogen concentration per cell
- [x] Experiment editor: initial signal sources and founder internal concentrations
- [x] Demo experiment `config/experiments/cell_memory/` (external signal → internal
      memory → persistent gene activation after signal fades; fixed seed)
- [x] Tests: trigger behaviour, internal morphogens, production/decay, cell memory,
      initialization, type migration, config validation (167 NUnit tests total)

## M1.5 — Genetic Lab

- [x] Russian-language interface and help catalog (`Localization`, `HelpCatalog` in core)
- [x] Analytical layers: morphogen concentration, gradient vectors, gene activity,
      silenced genes, energy, neighbour density (+ legend, layer options in «Мир»)
- [x] Visual genome editor: add/copy/delete genes, display data, AND/OR groups, NOT
- [x] Comparison operators ≥ / ≤ / range / = derived from the stored [min, max] interval
- [x] Regulator groups in the engine: OR across groups, AND inside a group,
      empty group is satisfied, unknown regulators evaluate to false
- [x] Morphogen editor (diffusion/decay/colour/names, dependency-guarded delete)
- [x] Cell-type editor (all phenotype parameters + colour + linked genome)
- [x] Experiment-settings editor (world size, resolution, seed, founders)
- [x] Help browser built into the panel (search + related links, context «?» buttons)
- [x] Working draft → validation (ConfigValidator) → apply (world rebuild)
- [x] Save experiment to profile JSON; load experiment from the panel
- [x] Dependency reference discovery before destructive edits (genes/morphogens/types)
- [x] Tests: regulator groups, validator, writer, localization, help catalog coverage
- [x] Automated UI checks (`--selftest`, `--uitest`) — headless and in a real window,
      clean exit without resource leaks (verified 2026-10)
- [x] Birth differentiation: `Divide.ChildTypeId` (daughter born as an explicit
      phenotype while the parent stays; legacy `null` inherits; last-applies-wins;
      per-step intent cleared after division; bounded `DivisionLog` diagnostics)
- [x] Morphogen editor: searchable list, copy with unique id, delete with
      reference-confirmation dialog and full cleanup (effects, regulator conditions,
      empty groups, division-gradient selector); empty morphogen list is a valid state
- [x] Validator: NaN guards, missing `ChildTypeId`, conflicting active Divides in one gene
- [x] Demo experiment `config/experiments/differentiation/` (birth-differentiation colony:
      signal decides worker vs skin at birth; workers/skin never divide; fixed seed)
- [x] Tests: `CellDifferentiationTests`, `MorphogenManagementTests`, demo experiment
      (116 NUnit tests total at release)

## M2 — Persistence and experimentation

- [x] Full save/load of world state (SaveSystem: cells, morphogens, RNG state)
- [x] Experiment profiles (named presets loaded from `config/experiments/`)
- [ ] Headless batch runner (run N seeds, export CSV statistics)
- [ ] Recording/playback of simulation timelines
- [ ] Save/load and experiment selection exposed in the HUD

## M3 — Ontogenesis and reproduction

- [ ] Multiple organisms: founder cells seed separate bodies
- [ ] Reproduction: organism-level trigger spawns a new founder elsewhere
- [ ] Death of the whole organism when structure collapses
- [ ] Regeneration: damage triggers morphogen response and regrowth
- [ ] Body plans: axial patterning via morphogen gradients (head/tail axes)

## M4 — Performance and tooling

- [x] Multithreaded morphogen diffusion (chunked job system)
- [x] Incremental spatial-hash updates instead of per-step rebuild
- [x] In-editor inspector for genomes/morphogens (visual rule editor) — delivered in M1.5
- [ ] Profiling overlay, deterministic replay debugging

## M5 — Research features

- [ ] Genetic algorithms over genomes (fitness = survival time / population)
- [ ] Environment map (nutrient sources, obstacles)
- [ ] Export/import of organisms as reusable genome files
