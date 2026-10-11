using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class GeneRegulationTests
{
    private sealed class Context : GeneContext
    {
        public double Sample;
        public double Emitted;
        public Rng Rng { get; set; } = new(42);
        public int Killed;

        public double SampleMorphogen(int morphogen, double x, double y) => Sample;
        public void EmitMorphogen(int morphogen, double x, double y, double amountPerSecond) => Emitted += amountPerSecond;
        public void Kill(Cell cell)
        {
            if (cell.State != CellState.Dying)
            {
                cell.State = CellState.Dying;
                Killed++;
            }
        }
    }

    private static GenomeConfig GenomeOf(params GeneConfig[] genes) => new() { Id = 0, Genes = new List<GeneConfig>(genes) };

    private static CellTypeConfig Type() => new()
    {
        Id = 0,
        MaxRadius = 30.0,
        GenomeId = 0
    };

    private static Cell Cell0(double radius = 5, int type = 0) => new() { Id = 0, Radius = radius, TypeId = type, GenomeId = 0 };

    // --- Inversion ----------------------------------------------------------

    [Test]
    public void InvertedRegulator_FlipsTheCondition()
    {
        var ctx = new Context { Sample = 5.0 };
        var direct = new GeneConfig
        {
            Id = "g",
            Regulators = { new RegulatorConfig { Kind = "Morphogen", Morphogen = 0, Min = 0, Max = 2, Invert = false } },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var inverted = new GeneConfig
        {
            Id = "h",
            Regulators = { new RegulatorConfig { Kind = "Morphogen", Morphogen = 0, Min = 0, Max = 2, Invert = true } },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var cell = Cell0();

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(direct, inverted), ctx, 0.5);

        Assert.That(cell.StateOf("g").Active, Is.False, "Direct regulator fails when outside the range.");
        Assert.That(cell.StateOf("h").Active, Is.True, "Inverted regulator must pass when outside the range.");
    }

    // --- AND logic across regulators ----------------------------------------

    [Test]
    public void AllRegulators_AreAndCombined()
    {
        var gene = new GeneConfig
        {
            Id = "g",
            Regulators =
            {
                new RegulatorConfig { Kind = "Property", Property = "age", Min = 1, Max = 1e9 },
                new RegulatorConfig { Kind = "Property", Property = "radius", Min = 10, Max = 1e9 }
            },
            Effects = { new GeneEffectConfig { Kind = "Grow", Rate = 1.0 } }
        };
        var ctx = new Context();

        // age high, radius low -> NOT active.
        var low = Cell0();
        low.Age = 10.0;
        low.Radius = 5.0;
        GenomeSystem.Evaluate(low, Type(), GenomeOf(gene), ctx, 0.5);
        Assert.That(low.StateOf("g").Active, Is.False);

        // both high -> active.
        var ok = Cell0();
        ok.Age = 10.0;
        ok.Radius = 12.0;
        GenomeSystem.Evaluate(ok, Type(), GenomeOf(gene), ctx, 0.5);
        Assert.That(ok.StateOf("g").Active, Is.True);
    }

    // --- Phenotype regulator + SetCellType ----------------------------------

    [Test]
    public void PhenotypeRegulator_GatesByCellType()
    {
        var workerOnly = new GeneConfig
        {
            Id = "w",
            Regulators = { new RegulatorConfig { Kind = "Phenotype", TypeId = 1 } },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var ctx = new Context();
        var genome = GenomeOf(workerOnly);

        var stem = Cell0(type: 0);
        GenomeSystem.Evaluate(stem, Type(), genome, ctx, 0.5);
        Assert.That(stem.StateOf("w").Active, Is.False);

        var worker = Cell0(type: 1);
        GenomeSystem.Evaluate(worker, Type(), genome, ctx, 0.5);
        Assert.That(worker.StateOf("w").Active, Is.True);
    }

    [Test]
    public void SetCellType_ChangesType_OncePerActivation()
    {
        var gene = new GeneConfig
        {
            Id = "diff",
            Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
            Effects = { new GeneEffectConfig { Kind = "SetCellType", TypeId = 2, Once = true } }
        };
        var cell = Cell0();
        var ctx = new Context();

        // Step 1 activates: type flips and the once-effect runs.
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);
        Assert.That(cell.TypeId, Is.EqualTo(2));

        // Step 2 stays active but the once-effect must not re-run (type unchanged).
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);
        Assert.That(cell.TypeId, Is.EqualTo(2));
    }

    // --- Timer StartTimer effect + Timer regulator --------------------------

    [Test]
    public void StartTimer_SetsOnce_AndIsNotRestartedWhileRunning()
    {
        var start = new GeneConfig
        {
            Id = "start",
            Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
            Effects = { new GeneEffectConfig { Kind = "StartTimer", Timer = "life", Duration = 10.0 } }
        };
        var cell = Cell0();
        var ctx = new Context();

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(start), ctx, 1.0);
        Assert.That(cell.TimerRemaining("life"), Is.EqualTo(10.0).Within(1e-9));

        // Gene still active, timer still running: StartTimer must not reset it.
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(start), ctx, 1.0);
        Assert.That(cell.TimerRemaining("life"), Is.EqualTo(9.0).Within(1e-9));
    }

    [Test]
    public void TimerRegulator_RequiresRunningTimer()
    {
        var gate = new GeneConfig
        {
            Id = "g",
            Regulators = { new RegulatorConfig { Kind = "Timer", Timer = "life" } },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var ctx = new Context { Sample = 1.0 };

        // No timer yet -> inactive.
        var noTimer = Cell0();
        GenomeSystem.Evaluate(noTimer, Type(), GenomeOf(gate), ctx, 0.5);
        Assert.That(noTimer.StateOf("g").Active, Is.False);

        // Start a timer externally, then the gene activates.
        var timed = Cell0();
        timed.Timers["life"] = 3.0;
        GenomeSystem.Evaluate(timed, Type(), GenomeOf(gate), ctx, 0.5);
        Assert.That(timed.StateOf("g").Active, Is.True);
    }

    [Test]
    public void TimerRegulator_Inverted_IsMetAfterExpiry()
    {
        var gate = new GeneConfig
        {
            Id = "postmortem",
            Regulators = { new RegulatorConfig { Kind = "Timer", Timer = "life", Invert = true } },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var ctx = new Context { Sample = 1.0 };
        var cell = Cell0();
        cell.Timers["life"] = 0.5; // just under one step

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gate), ctx, 1.0);
        Assert.That(cell.TimerRemaining("life"), Is.EqualTo(0.0).Within(1e-9), "Timer remaining must be 0 after expiry.");
        Assert.That(cell.StateOf("postmortem").Active, Is.True, "Inverted timer regulator satisfied once expired.");
    }

    [Test]
    public void TimerRegulator_WithTimeWindow_IsMetAfterElapsed()
    {
        // The gene fires only after 5 seconds have elapsed since the timer started.
        var gate = new GeneConfig
        {
            Id = "g",
            Regulators = { new RegulatorConfig { Kind = "Timer", Timer = "life", Min = 5.0 } },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var ctx = new Context { Sample = 1.0 };
        var cell = Cell0();
        cell.Timers["life"] = 10.0;
        cell.TimerTotals["life"] = 10.0;

        // 0 s elapsed -> not yet.
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gate), ctx, 0.0);
        Assert.That(cell.StateOf("g").Active, Is.False, "Should wait for 5 seconds.");

        // Advance 5 s -> elapsed 5.0, condition met.
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gate), ctx, 5.0);
        Assert.That(cell.TimerElapsed("life"), Is.EqualTo(5.0).Within(1e-9));
        Assert.That(cell.StateOf("g").Active, Is.True);
    }

    [Test]
    public void TimerRegulator_TimeWindow_HasUpperBound()
    {
        var gate = new GeneConfig
        {
            Id = "g",
            Regulators = { new RegulatorConfig { Kind = "Timer", Timer = "life", Min = 2.0, Max = 4.0 } },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var ctx = new Context { Sample = 1.0 };
        var cell = Cell0();
        cell.Timers["life"] = 10.0;
        cell.TimerTotals["life"] = 10.0;

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gate), ctx, 3.0); // elapsed 3 -> inside
        Assert.That(cell.StateOf("g").Active, Is.True);

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gate), ctx, 2.0); // elapsed 5 -> above max
        Assert.That(cell.StateOf("g").Active, Is.False);
    }

    [Test]
    public void StartTimer_RecordsElapsed_AndKeepsFullDurationAfterExpiry()
    {
        var start = new GeneConfig
        {
            Id = "start",
            Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
            Effects = { new GeneEffectConfig { Kind = "StartTimer", Timer = "life", Duration = 2.0, Once = true } }
        };
        var cell = Cell0();
        var ctx = new Context();

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(start), ctx, 1.0);
        Assert.That(cell.TimerElapsed("life"), Is.EqualTo(0.0).Within(1e-9), "Just started: no time elapsed.");

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(start), ctx, 1.0);
        Assert.That(cell.TimerElapsed("life"), Is.EqualTo(1.0).Within(1e-9));

        // Cross expiry: remaining is clamped to 0, elapsed stays at the full duration.
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(start), ctx, 5.0);
        Assert.That(cell.TimerRemaining("life"), Is.EqualTo(0.0).Within(1e-9));
        Assert.That(cell.TimerElapsed("life"), Is.EqualTo(2.0).Within(1e-9));
    }

    // --- SilenceGene --------------------------------------------------------

    [Test]
    public void SilenceGene_BlocksReactivation()
    {
        var target = new GeneConfig
        {
            Id = "target",
            Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
            Effects = { TestFactory.Emit(0, 1) }
        };
        var silencer = new GeneConfig
        {
            Id = "silencer",
            Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
            Effects = { new GeneEffectConfig { Kind = "SilenceGene", Gene = "target", Once = true } }
        };
        var ctx = new Context { Sample = 1.0 };
        var cell = Cell0();

        // Silencer runs once, silencing target.
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(target, silencer), ctx, 0.5);
        Assert.That(cell.IsSilenced("target"), Is.True);
        Assert.That(cell.StateOf("target").Active, Is.False);

        // Even with conditions still met, a silenced gene cannot reactivate.
        GenomeSystem.Evaluate(cell, Type(), GenomeOf(target, silencer), ctx, 0.5);
        Assert.That(cell.StateOf("target").Active, Is.False);
        Assert.That(cell.TimerRemaining("target"), Is.EqualTo(0.0));
    }

    // --- Apoptosis ----------------------------------------------------------

    [Test]
    public void Apoptosis_Deterministic_KillsCell()
    {
        var gene = new GeneConfig
        {
            Id = "die",
            Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
            Effects = { new GeneEffectConfig { Kind = "Apoptosis", Probability = 0 } }
        };
        var ctx = new Context();
        var cell = Cell0();

        GenomeSystem.Evaluate(cell, Type(), GenomeOf(gene), ctx, 0.5);
        Assert.That(cell.State, Is.EqualTo(CellState.Dying));
    }

    [Test]
    public void Apoptosis_Probabilistic_IsDtInvariant()
    {
        // Probability per second p; per-step chance is 1-exp(-p*dt).
        // One step dt=1 must kill about the same fraction as two steps dt=0.5.
        const double p = 0.5;
        var gene = new GeneConfig
        {
            Id = "die",
            Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
            Effects = { new GeneEffectConfig { Kind = "Apoptosis", Probability = p } }
        };
        var genome = GenomeOf(gene);
        var type = Type();
        double expected = 1.0 - Math.Exp(-p); // 0.393

        // Each cell is evaluated `steps` times with `dt`; total elapsed = steps*dt.
        // Total kill fraction over many cells should be 1-exp(-p*steps*dt).
        double RunBatch(double dt, int steps)
        {
            var ctx = new Context { Rng = new Rng(777) };
            int n = 4000;
            int killed = 0;
            for (int i = 0; i < n; i++)
            {
                var cell = Cell0();
                for (int s = 0; s < steps; s++)
                    if (cell.State != CellState.Dying)
                        GenomeSystem.Evaluate(cell, type, genome, ctx, dt);
                if (cell.State == CellState.Dying) killed++;
            }
            return killed / (double)n;
        }

        double singleStep = RunBatch(1.0, 1);
        double twoHalfSteps = RunBatch(0.5, 2);

        Assert.That(singleStep, Is.EqualTo(expected).Within(0.06), "One full step must match 1-exp(-p).");
        Assert.That(twoHalfSteps, Is.EqualTo(expected).Within(0.06), "Two half-steps must match one full step (dt-invariance).");
    }

    // --- BlockDivision ------------------------------------------------------

    [Test]
    public void BlockDivision_PreventsDivision()
    {
        var blocker = new GeneConfig
        {
            Id = "block",
            Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
            Effects = { new GeneEffectConfig { Kind = "BlockDivision" } }
        };
        var divider = new GeneConfig
        {
            Id = "div",
            Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 0, Max = 1e9 } },
            Effects = { TestFactory.Divide(100.0) }
        };

        var setup = TestFactory.Setup(morphogenCount: 1, initialCells: 1, genome: new[] { blocker, divider });
        var sim = new Simulation(setup);
        sim.Cells[0].DivisionProgress = 1.0;

        int before = sim.Cells.Count;
        sim.Step(0.05);
        Assert.That(sim.Cells.Count, Is.EqualTo(before), "Blocked cell must not divide.");
    }

    // --- BlockDivision is re-armed each step (release on condition loss) -----

    [Test]
    public void BlockDivision_DoesNotPersist_OnceConditionGone()
    {
        var setup = TestFactory.Setup(
            morphogenCount: 1,
            initialCells: 1,
            genome: new GeneConfig
            {
                Id = "block",
                Regulators = { new RegulatorConfig { Kind = "Property", Property = "age", Min = 3, Max = 6 } },
                Effects = { new GeneEffectConfig { Kind = "BlockDivision" } }
            });
        var sim = new Simulation(setup);
        var cell = sim.Cells[0];

        // Age below the window -> no block.
        for (int i = 0; i < 20; i++) sim.Step(0.05); // age 1.0
        Assert.That(cell.DivisionBlocked, Is.False);

        // Age inside the window -> blocked for this step.
        for (int i = 0; i < 60; i++) sim.Step(0.05); // age 4.0
        Assert.That(cell.DivisionBlocked, Is.True);

        // Age above the window -> next step the flag is cleared again.
        for (int i = 0; i < 60; i++) sim.Step(0.05); // age 7.0
        Assert.That(cell.DivisionBlocked, Is.False,
            "BlockDivision only suppresses division while the gene is active.");
    }
}