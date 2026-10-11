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

    /// <summary>
    /// Creates a new gene. M1.6: genes are inactive by default and carry a
    /// mandatory morphogen trigger when the draft contains morphogens.
    /// </summary>
    public static GeneConfig NewGene(GenomeConfig genome, MorphogenConfig[] morphogens, string? id = null)
    {
        string key = UniqueGeneId(genome, id ?? "новый_ген");
        var firstMorphogen = morphogens.FirstOrDefault(m => m.IsExternal);
        return new GeneConfig
        {
            Id = key,
            Name = "Новый ген",
            Role = "",
            Description = "Опишите назначение этого гена.",
            Regulators = new List<RegulatorConfig>(),
            Effects = new List<GeneEffectConfig>(),
            RequireMorphogenTrigger = true,
            RequiredMorphogenId = firstMorphogen?.Id ?? -1,
            RequiredMorphogenThreshold = 0.5
        };
    }

    /// <summary>Copies a gene, giving the copy a fresh unique id (all else preserved).</summary>
    public static GeneConfig CopyGene(GenomeConfig genome, GeneConfig source)
    {
        string key = UniqueGeneId(genome, source.Key + "_copy");
        var copy = CloneGene(source);
        copy.Id = key;
        copy.Name = string.IsNullOrWhiteSpace(source.Name)
            ? $"Копия «{source.Key}»"
            : $"{source.Name} (копия)";
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
            Timer = e.Timer, Gene = e.Gene, Rate = e.Rate, Duration = e.Duration, Probability = e.Probability,
            ChildTypeId = e.ChildTypeId, Repel = e.Repel
        };

        var clone = new GeneConfig
        {
            Id = source.Id,
            Name = source.Name,
            Role = source.Role,
            Description = source.Description,
            ActivationDelay = source.ActivationDelay,
            RequireMorphogenTrigger = source.RequireMorphogenTrigger,
            RequiredMorphogenId = source.RequiredMorphogenId,
            RequiredMorphogenThreshold = source.RequiredMorphogenThreshold,
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

    /// <summary>Finds genes that reference a morphogen (emit/move effects, morphogen regulators) and the division gradient.</summary>
    public static List<string> FindMorphogenReferences(SimulationSetup setup, int morphogenId)
    {
        var refs = new List<string>();
        if (setup.Simulation.DivisionGradientMorphogen == morphogenId)
            refs.Add("эксперимент (морфоген направления деления)");
        foreach (var genome in setup.Genomes)
            foreach (var gene in genome.Genes)
            {
                bool hit = gene.Effects.Any(e =>
                        (Eq(e.Kind, "EmitMorphogen") || Eq(e.Kind, "Move")) && e.Morphogen == morphogenId)
                    || gene.EffectiveGroups().Any(g => g.Regulators.Any(r =>
                        Eq(r.Kind, "Morphogen") && r.Morphogen == morphogenId));
                if (hit) refs.Add($"ген «{gene.Key}»");
            }
        return refs;
    }

    /// <summary>Finds genes, types and world settings that reference a cell type id.</summary>
    public static List<string> FindCellTypeReferences(SimulationSetup setup, int typeId)
    {
        var refs = new List<string>();
        if (setup.Simulation.FounderTypeId == typeId) refs.Add("эксперимент (тип основателя)");
        foreach (var genome in setup.Genomes)
            foreach (var gene in genome.Genes)
            {
                bool hit = gene.Effects.Any(e =>
                        (Eq(e.Kind, "SetCellType") && e.TypeId == typeId) ||
                        (Eq(e.Kind, "Divide") && e.ChildTypeId == typeId))
                    || gene.EffectiveGroups().Any(g => g.Regulators.Any(r => Eq(r.Kind, "Phenotype") && r.TypeId == typeId));
                if (hit) refs.Add($"ген «{gene.Key}»");
            }
        return refs;
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Deletes a morphogen from the working set and removes every reference to
    /// it: emit/move effects, morphogen regulators (dropping groups that become
    /// empty), and the division-gradient selector when it pointed at this id.
    /// Returns the affected gene ids so the UI can report the cleanup.
    /// </summary>
    public static List<string> RemoveMorphogen(SimulationSetup setup, int morphogenId)
    {
        var affected = new List<string>();
        foreach (var genome in setup.Genomes)
            foreach (var gene in genome.Genes)
            {
                bool touched = gene.Effects.RemoveAll(e =>
                    (Eq(e.Kind, "EmitMorphogen") || Eq(e.Kind, "Move")) && e.Morphogen == morphogenId) > 0;

                if (gene.RegulatorGroups != null)
                {
                    foreach (var group in gene.RegulatorGroups)
                        if (group.Regulators.RemoveAll(r => Eq(r.Kind, "Morphogen") && r.Morphogen == morphogenId) > 0)
                            touched = true;
                    gene.RegulatorGroups.RemoveAll(g => g.Regulators.Count == 0);
                }
                else if (gene.Regulators.RemoveAll(r => Eq(r.Kind, "Morphogen") && r.Morphogen == morphogenId) > 0)
                {
                    touched = true;
                }

                if (gene.RequireMorphogenTrigger && gene.RequiredMorphogenId == morphogenId)
                {
                    gene.RequiredMorphogenId = -1;
                    touched = true;
                }

                if (touched) affected.Add($"ген «{gene.Key}»");
            }

        if (setup.Simulation.DivisionGradientMorphogen == morphogenId)
        {
            setup.Simulation.DivisionGradientMorphogen = -1;
            affected.Add("эксперимент (морфоген направления деления)");
        }

        if (setup.Simulation.InitialExternalSources.RemoveAll(s => s.MorphogenId == morphogenId) > 0)
            affected.Add("начальные внешние источники");

        setup.Morphogens = setup.Morphogens.Where(m => m.Id != morphogenId).ToArray();
        return affected;
    }

    /// <summary>
    /// Removes initial external sources that contradict the morphogen's current
    /// type. Called when a morphogen type is switched in the editor.
    /// </summary>
    public static void ReconcileInitialSignals(SimulationSetup setup, int morphogenId)
    {
        var cfg = setup.Morphogens.FirstOrDefault(m => m.Id == morphogenId);
        if (cfg == null) return;
        if (cfg.IsInternal)
            setup.Simulation.InitialExternalSources.RemoveAll(s => s.MorphogenId == morphogenId);
    }

    /// <summary>Returns a morphogen id unused in the working set.</summary>
    public static int UniqueMorphogenId(SimulationSetup setup, int baseId)
    {
        bool used(int id) => setup.Morphogens.Any(m => m.Id == id);
        int id = baseId;
        while (used(id)) id++;
        return id;
    }

    /// <summary>Returns a genome id unused in the working set.</summary>
    public static int UniqueGenomeId(SimulationSetup setup, int baseId)
    {
        bool used(int id) => setup.Genomes.Any(g => g.Id == id);
        int id = baseId;
        while (used(id)) id++;
        return id;
    }

    /// <summary>Creates a new empty genome with a fresh unique id.</summary>
    public static GenomeConfig NewGenome(SimulationSetup setup)
    {
        int id = UniqueGenomeId(setup, setup.Genomes.Length > 0 ? setup.Genomes.Max(g => g.Id) + 1 : 0);
        var genome = new GenomeConfig { Id = id, Name = $"genome{id}" };
        genome.Genes.Add(NewGene(genome, setup.Morphogens));
        return genome;
    }

    /// <summary>Deep copy of a genome (all genes) with a fresh unique id.</summary>
    public static GenomeConfig CopyGenome(SimulationSetup setup, GenomeConfig source)
    {
        int id = UniqueGenomeId(setup, source.Id + 1);
        return new GenomeConfig
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(source.Name)
                ? $"genome{id}"
                : $"{source.Name}_copy",
            Genes = source.Genes.Select(CloneGene).ToList()
        };
    }

    /// <summary>
    /// Deletes a genome when possible. Returns null on success, otherwise a
    /// short explanation: the last genome cannot be deleted, and a genome that
    /// phenotype references stays until the reference is reassigned.
    /// </summary>
    public static string? RemoveGenome(SimulationSetup setup, GenomeConfig genome)
    {
        if (setup.Genomes.Length <= 1)
            return "нельзя удалить последний геном";
        if (setup.CellTypes.Any(t => t.GenomeId == genome.Id))
            return "на геном ссылаются типы клеток";
        setup.Genomes = setup.Genomes.Where(g => g.Id != genome.Id).ToArray();
        return null;
    }

    /// <summary>
    /// Auto-repairs external references to missing morphogens so a configuration
    /// that survived manual edits (or an older save) can always be loaded and
    /// applied: disables the division-gradient selector and drops initial
    /// signals that point at a morphogen that no longer exists.
    /// </summary>
    public static void SanitizeMorphogenReferences(SimulationSetup setup)
    {
        var external = setup.Morphogens.Where(m => m.IsExternal).Select(m => m.Id).ToHashSet();

        if (setup.Simulation.DivisionGradientMorphogen >= 0 && !external.Contains(setup.Simulation.DivisionGradientMorphogen))
            setup.Simulation.DivisionGradientMorphogen = -1;

        setup.Simulation.InitialExternalSources.RemoveAll(s => !external.Contains(s.MorphogenId));
    }

    /// <summary>Deep copy of a single morphogen with a fresh, unique id.</summary>
    public static MorphogenConfig CopyMorphogen(SimulationSetup setup, MorphogenConfig source)
    {
        int newId = UniqueMorphogenId(setup, source.Id + 1);
        return new MorphogenConfig
        {
            Id = newId,
            Name = string.IsNullOrWhiteSpace(source.Name)
                ? $"Копия «{source.Id}»"
                : $"{source.Name} (копия)",
            Role = source.Role,
            Description = source.Description,
            Type = source.Type,
            Diffusion = source.Diffusion,
            Decay = source.Decay,
            Color = source.Color,
            DivisionInheritance = source.DivisionInheritance
        };
    }

    /// <summary>Returns a cell type id unused in the working set.</summary>
    public static int UniqueCellTypeId(SimulationSetup setup, int baseId)
    {
        bool used(int id) => setup.CellTypes.Any(t => t.Id == id);
        int id = baseId;
        while (used(id)) id++;
        return id;
    }

    /// <summary>Deep copy of a single cell type with a fresh, unique id.</summary>
    public static CellTypeConfig CopyCellType(SimulationSetup setup, CellTypeConfig source)
    {
        int newId = UniqueCellTypeId(setup, source.Id + 1);
        return new CellTypeConfig
        {
            Id = newId,
            Name = string.IsNullOrWhiteSpace(source.Name)
                ? $"Копия «{source.Id}»"
                : $"{source.Name} (копия)",
            Description = source.Description,
            Color = source.Color,
            GenomeId = source.GenomeId,
            BaseRadius = source.BaseRadius,
            MaxRadius = source.MaxRadius
        };
    }
}
