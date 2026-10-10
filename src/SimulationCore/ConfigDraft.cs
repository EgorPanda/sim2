namespace SimulationCore;

/// <summary>
/// Helpers for safe draft editing of genomes and cross-references: unique id
/// generation, gene copy/creation and reference discovery used by the editor to
/// warn about dependencies before destructive operations.
/// </summary>
public static class ConfigDraft
{
    /// <summary>Returns an id based on <paramref name="baseId"/> that is unused in the genome.</summary>
    public static string UniqueGeneId(GenomeConfig genome, string baseId)
    {
        string root = string.IsNullOrWhiteSpace(baseId) ? "gene" : baseId.Trim();
        if (!GeneIdExists(genome, root)) return root;
        int n = 2;
        while (GeneIdExists(genome, $"{root}{n}")) n++;
        return $"{root}{n}";
    }

    public static bool GeneIdExists(GenomeConfig genome, string id)
        => genome.Genes.Any(g => string.Equals(g.Key, id, StringComparison.Ordinal));

    /// <summary>Creates a new, valid gene with a unique id and a placeholder description.</summary>
    public static GeneConfig NewGene(GenomeConfig genome, string? id = null)
    {
        string key = UniqueGeneId(genome, id ?? "новый_ген");
        return new GeneConfig
        {
            Id = key,
            Name = key,
            DisplayName = "Новый ген",
            Description = "Опишите назначение этого гена.",
            Regulators = new List<RegulatorConfig>(),
            Effects = new List<GeneEffectConfig>()
        };
    }

    /// <summary>Copies a gene, giving the copy a fresh unique id (all else preserved).</summary>
    public static GeneConfig CopyGene(GenomeConfig genome, GeneConfig source)
    {
        string key = UniqueGeneId(genome, source.Key + "_copy");
        var copy = CloneGene(source);
        copy.Id = key;
        copy.Name = key;
        copy.DisplayName = string.IsNullOrWhiteSpace(source.DisplayName)
            ? $"Копия «{source.Key}»"
            : $"{source.DisplayName} (копия)";
        return copy;
    }

    /// <summary>Deep copy of a single gene (groups, regulators, effects).</summary>
    public static GeneConfig CloneGene(GeneConfig source)
    {
        static RegulatorConfig CloneReg(RegulatorConfig r) => new()
        {
            Kind = r.Kind, Invert = r.Invert, Property = r.Property,
            Morphogen = r.Morphogen, TypeId = r.TypeId, Timer = r.Timer, Min = r.Min, Max = r.Max
        };
        static GeneEffectConfig CloneEff(GeneEffectConfig e) => new()
        {
            Kind = e.Kind, Once = e.Once, Morphogen = e.Morphogen, TypeId = e.TypeId,
            Timer = e.Timer, Gene = e.Gene, Rate = e.Rate, Duration = e.Duration, Probability = e.Probability
        };

        var clone = new GeneConfig
        {
            Id = source.Id,
            Name = source.Name,
            DisplayName = source.DisplayName,
            Description = source.Description,
            Role = source.Role,
            ActivationDelay = source.ActivationDelay,
            Regulators = source.Regulators.Select(CloneReg).ToList(),
            Effects = source.Effects.Select(CloneEff).ToList()
        };
        if (source.RegulatorGroups is { } groups)
            clone.RegulatorGroups = groups.Select(g => new RegulatorGroupConfig
            {
                Operator = g.Operator,
                Regulators = g.Regulators.Select(CloneReg).ToList()
            }).ToList();
        return clone;
    }

    /// <summary>
    /// Finds places that reference a gene id (silencing effects), so the editor
    /// can warn before a rename or delete changes behaviour.
    /// </summary>
    public static List<string> FindGeneReferences(SimulationSetup setup, string geneId)
    {
        var refs = new List<string>();
        foreach (var genome in setup.Genomes)
            foreach (var gene in genome.Genes)
                foreach (var effect in gene.Effects)
                    if (string.Equals(effect.Kind, "SilenceGene", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(effect.Gene, geneId, StringComparison.Ordinal))
                        refs.Add($"ген «{gene.Key}» (эффект «Подавление гена»)");
        return refs;
    }

    /// <summary>Finds genes that reference a morphogen (emit/absorb regulators/effects).</summary>
    public static List<string> FindMorphogenReferences(SimulationSetup setup, int morphogenId)
    {
        var refs = new List<string>();
        foreach (var genome in setup.Genomes)
            foreach (var gene in genome.Genes)
            {
                bool hit = gene.Effects.Any(e =>
                        (Eq(e.Kind, "EmitMorphogen") || Eq(e.Kind, "Absorb")) && e.Morphogen == morphogenId)
                    || gene.EffectiveGroups().Any(g => g.Regulators.Any(r =>
                        Eq(r.Kind, "Morphogen") && r.Morphogen == morphogenId));
                if (hit) refs.Add($"ген «{gene.Key}»");
            }
        return refs;
    }

    /// <summary>Finds genes and types that reference a cell type id.</summary>
    public static List<string> FindCellTypeReferences(SimulationSetup setup, int typeId)
    {
        var refs = new List<string>();
        if (setup.Simulation.FounderTypeId == typeId) refs.Add("эксперимент (тип основателя)");
        foreach (var genome in setup.Genomes)
            foreach (var gene in genome.Genes)
            {
                bool hit = gene.Effects.Any(e => Eq(e.Kind, "SetCellType") && e.TypeId == typeId)
                    || gene.EffectiveGroups().Any(g => g.Regulators.Any(r => Eq(r.Kind, "Phenotype") && r.TypeId == typeId));
                if (hit) refs.Add($"ген «{gene.Key}»");
            }
        return refs;
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
