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

- [ ] Multithreaded morphogen diffusion (chunked job system)
- [ ] Incremental spatial-hash updates instead of per-step rebuild
- [ ] In-editor inspector for genomes/morphogens (visual rule editor)
- [ ] Profiling overlay, deterministic replay debugging

## M5 — Research features

- [ ] Genetic algorithms over genomes (fitness = survival time / population)
- [ ] Environment map (nutrient sources, obstacles)
- [ ] Export/import of organisms as reusable genome files
