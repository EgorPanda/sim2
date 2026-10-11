using NUnit.Framework;
using System.Diagnostics;
using SimulationCore;

namespace SimulationCore.Tests;

/// <summary>
/// Diagnostic performance measurements for the morphogen pipeline. The test
/// itself does not assert thresholds; it prints timings so regressions and
/// hotspots can be spotted in CI logs.
/// </summary>
[TestFixture]
public class MorphogenPerformanceTests
{
    [Test]
    public void MeasureMorphogenPipeline()
    {
        int fields = 4;
        int cells = 400;
        var setup = TestFactory.Setup(
            morphogenCount: fields,
            diffusion: 50.0,
            decay: 0.1,
            world: 512.0,
            resolution: 4.0,
            initialCells: cells,
            genome: TestFactory.Gene("emit", TestFactory.Emit(0, 20.0)));

        var sim = new Simulation(setup);
        // Warm-up.
        for (int i = 0; i < 20; i++) sim.Step(0.05);

        int steps = 100;
        var swTotal = Stopwatch.StartNew();
        for (int i = 0; i < steps; i++) sim.Step(0.05);
        swTotal.Stop();

        double msPerStep = swTotal.Elapsed.TotalMilliseconds / steps;
        TestContext.WriteLine($"[perf] fields={fields} cells={cells} grid={sim.Morphogens.GridWidth}x{sim.Morphogens.GridHeight}");
        TestContext.WriteLine($"[perf] {steps} steps in {swTotal.Elapsed.TotalMilliseconds:F1} ms -> {msPerStep:F3} ms/step");

        Assert.That(msPerStep, Is.LessThan(50.0),
            "A single step with several fields and hundreds of cells should stay well under one frame at 20 fps.");
    }
}
