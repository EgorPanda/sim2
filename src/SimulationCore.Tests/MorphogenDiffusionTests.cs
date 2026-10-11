using NUnit.Framework;
using SimulationCore;

namespace SimulationCore.Tests;

[TestFixture]
public class MorphogenDiffusionTests
{
    [Test]
    public void Diffusion_SpreadsWithoutMassLoss_WhenDecayIsZero()
    {
        var system = new MorphogenSystem(256, 256, 8.0, new[] { 0 });
        // Seed a single point with mass.
        system.SetField(0, 16, 16, 1000.0);

        var configs = new Dictionary<int, MorphogenConfig>
        {
            [0] = new MorphogenConfig { Id = 0, Diffusion = 10.0, Decay = 0.0 }
        };

        double before = system.TotalMass(0);
        for (int i = 0; i < 50; i++) system.Update(0.05, configs);
        double after = system.TotalMass(0);

        // Zero-flux boundaries -> mass is (numerically) conserved.
        Assert.That(after, Is.EqualTo(before).Within(before * 0.02),
            "Diffusion with zero decay must conserve mass.");
    }

    [Test]
    public void Diffusion_ReducesPeak_AndSpreadsNeighbourhood()
    {
        var system = new MorphogenSystem(256, 256, 8.0, new[] { 0 });
        system.SetField(0, 16, 16, 1000.0);

        var configs = new Dictionary<int, MorphogenConfig>
        {
            [0] = new MorphogenConfig { Id = 0, Diffusion = 10.0, Decay = 0.0 }
        };

        double peakBefore = system.GetField(0, 16, 16);
        for (int i = 0; i < 20; i++) system.Update(0.05, configs);

        double peakAfter = system.GetField(0, 16, 16);
        double neighbour = system.GetField(0, 17, 16);

        Assert.That(peakAfter, Is.LessThan(peakBefore), "Peak concentration should fall as it spreads.");
        Assert.That(neighbour, Is.GreaterThan(0.0), "Adjacent cells should receive morphogen.");
    }

    [Test]
    public void Decay_ReducesTotalMass_AtExpectedRate()
    {
        var system = new MorphogenSystem(64, 64, 8.0, new[] { 0 });
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
                system.SetField(0, x, y, 1.0);

        var configs = new Dictionary<int, MorphogenConfig>
        {
            [0] = new MorphogenConfig { Id = 0, Diffusion = 0.0, Decay = 1.0 }
        };

        double before = system.TotalMass(0);
        system.Update(1.0, configs);
        double after = system.TotalMass(0);

        // exp(-1) ~= 0.3679.
        Assert.That(after / before, Is.EqualTo(Math.Exp(-1.0)).Within(0.01));
    }

    [Test]
    public void Sampling_AtFieldCenter_ReturnsStoredValue()
    {
        var system = new MorphogenSystem(256, 256, 8.0, new[] { 0 });
        system.SetField(0, 10, 10, 5.0);

        // Grid cell (10,10) covers world [80,88] x [80,88]; centre is 84.
        double value = system.Sample(0, 84.0, 84.0);
        Assert.That(value, Is.EqualTo(5.0).Within(1e-9));
    }

    [Test]
    public void Update_IsStable_UnderLargeDiffusion()
    {
        var system = new MorphogenSystem(128, 128, 8.0, new[] { 0 });
        system.SetField(0, 8, 8, 1e6);

        var configs = new Dictionary<int, MorphogenConfig>
        {
            [0] = new MorphogenConfig { Id = 0, Diffusion = 500.0, Decay = 0.1 }
        };

        for (int i = 0; i < 100; i++) system.Update(0.05, configs);

        // Explicit diffusion must remain finite (sub-stepping guarantees stability).
        Assert.That(double.IsFinite(system.TotalMass(0)), Is.True);
        Assert.That(double.IsNaN(system.TotalMass(0)), Is.False);
    }
}
