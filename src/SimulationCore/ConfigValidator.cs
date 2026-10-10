namespace SimulationCore;

public enum ConfigIssueSeverity
{
    /// <summary>Blocks applying/saving the configuration.</summary>
    Error,
    /// <summary>Suspicious but non-blocking.</summary>
    Warning
}

/// <summary>A single validation finding, localized for the UI.</summary>
public sealed class ConfigIssue
{
    public ConfigIssueSeverity Severity { get; init; }
    public string Section { get; init; } = "";
    public string Location { get; init; } = "";
    public string Message { get; init; } = "";
    public string Hint { get; init; } = "";

    public bool IsError => Severity == ConfigIssueSeverity.Error;

    public override string ToString()
        => $"[{(IsError ? "Ошибка" : "Внимание")}] {Section}{(Location.Length > 0 ? " / " + Location : "")}: {Message}" +
           (Hint.Length > 0 ? $" ({Hint})" : "");
}

/// <summary>
/// Centralized validation of editable configurations. Checks identifier
/// uniqueness, reference existence, numeric ranges, logical groups, required
/// effect parameters and backward compatibility of the schema.
/// </summary>
public static class ConfigValidator
{
    private static readonly string[] ValidStates = { "Alive", "Dividing", "Dying" };

    public static List<ConfigIssue> Validate(SimulationSetup setup)
    {
        var issues = new List<ConfigIssue>();

        var morphogenIds = new HashSet<int>(setup.Morphogens.Select(m => m.Id));
        var typeIds = new HashSet<int>(setup.CellTypes.Select(t => t.Id));
        var genomeIds = new HashSet<int>(setup.Genomes.Select(g => g.Id));
        var geneIdsByGenome = new Dictionary<GenomeConfig, HashSet<string>>();

        ValidateSimulation(setup, issues, typeIds, morphogenIds);
        ValidateMorphogens(setup, issues);
        ValidateCellTypes(setup, issues, genomeIds);
        ValidateGenomes(setup, issues, morphogenIds, typeIds, geneIdsByGenome);

        if (setup.Genomes.Length == 0)
            issues.Add(Err("Геномы", "", "Не задано ни одного генома.",
                "Добавьте геном в genomes.json."));
        if (setup.CellTypes.Length == 0)
            issues.Add(Err("Типы клеток", "", "Не задано ни одного типа клеток.",
                "Добавьте тип клеток в celltypes.json."));
        if (setup.Morphogens.Length == 0)
            issues.Add(Warn("Морфогены", "", "Не задано ни одного морфогена.",
                "Гены, использующие морфогены, останутся неактивными или получат ошибку."));

        return issues;
    }

    public static bool HasErrors(IEnumerable<ConfigIssue> issues) => issues.Any(i => i.IsError);

    private static void ValidateSimulation(
        SimulationSetup setup, List<ConfigIssue> issues, HashSet<int> typeIds, HashSet<int> morphogenIds)
    {
        var s = setup.Simulation;
        if (s.WorldWidth <= 0) issues.Add(Err("Эксперимент", "worldWidth", "Ширина мира должна быть положительной.", "Например 800."));
        if (s.WorldHeight <= 0) issues.Add(Err("Эксперимент", "worldHeight", "Высота мира должна быть положительной.", "Например 800."));
        if (s.MorphogenResolution <= 0) issues.Add(Err("Эксперимент", "morphogenResolution", "Разрешение сетки морфогенов должно быть положительным.", "Обычно 4–8."));
        if (s.FixedTimeStep <= 0) issues.Add(Err("Эксперимент", "fixedTimeStep", "Шаг времени должен быть положительным.", "Обычно 0.05."));
        if (s.DefaultSpeed <= 0) issues.Add(Err("Эксперимент", "defaultSpeed", "Начальная скорость должна быть положительной.", "Обычно 1.0."));
        if (s.CellRadius <= 0) issues.Add(Err("Эксперимент", "cellRadius", "Базовый радиус клетки должен быть положительным.", ""));
        if (s.InitialCells < 1) issues.Add(Err("Эксперимент", "initialCells", "Число начальных клеток должно быть не меньше 1.", ""));
        if (!typeIds.Contains(s.FounderTypeId) && typeIds.Count > 0)
            issues.Add(Err("Эксперимент", "founderTypeId", $"Тип основателя {s.FounderTypeId} не существует.",
                "Выберите существующий идентификатор типа клеток."));
        if (s.DivisionGradientMorphogen >= 0 && !morphogenIds.Contains(s.DivisionGradientMorphogen))
            issues.Add(Err("Эксперимент", "divisionGradientMorphogen", $"Морфоген {s.DivisionGradientMorphogen} не существует.",
                "Выберите существующий морфоген или -1."));
    }

    private static void ValidateMorphogens(SimulationSetup setup, List<ConfigIssue> issues)
    {
        var seen = new HashSet<int>();
        foreach (var m in setup.Morphogens)
        {
            string loc = $"морфоген {m.Id}";
            if (!seen.Add(m.Id))
                issues.Add(Err("Морфогены", loc, "Повторяющийся идентификатор морфогена.", "Идентификаторы должны быть уникальны."));
            if (m.Id < 0)
                issues.Add(Err("Морфогены", loc, "Идентификатор морфогена не может быть отрицательным.", ""));
            if (m.Diffusion < 0 || double.IsNaN(m.Diffusion))
                issues.Add(Err("Морфогены", loc, "Коэффициент диффузии должен быть числом ≥ 0.", "Обычно 0–200."));
            if (m.Decay < 0 || double.IsNaN(m.Decay))
                issues.Add(Err("Морфогены", loc, "Скорость затухания должна быть числом ≥ 0.", "Обычно 0–5."));
            if (!IsHexColor(m.Color))
                issues.Add(Warn("Морфогены", loc, $"Цвет «{m.Color}» не распознан.", "Используйте HEX, например #4ea8de."));
        }
    }

    private static void ValidateCellTypes(SimulationSetup setup, List<ConfigIssue> issues, HashSet<int> genomeIds)
    {
        var seen = new HashSet<int>();
        foreach (var t in setup.CellTypes)
        {
            string loc = $"тип {t.Id}";
            if (!seen.Add(t.Id))
                issues.Add(Err("Типы клеток", loc, "Повторяющийся идентификатор типа клеток.", "Идентификаторы должны быть уникальны."));
            if (!genomeIds.Contains(t.GenomeId) && genomeIds.Count > 0)
                issues.Add(Err("Типы клеток", loc, $"Ссылка на несуществующий геном {t.GenomeId}.", "Выберите существующий геном."));
            if (t.BaseRadius <= 0 || double.IsNaN(t.BaseRadius))
                issues.Add(Err("Типы клеток", loc, "Базовый радиус должен быть положительным числом.", ""));
            if (t.MaxRadius < t.BaseRadius || double.IsNaN(t.MaxRadius))
                issues.Add(Err("Типы клеток", loc, "Максимальный радиус меньше базового или некорректен.", "MaxRadius ≥ BaseRadius."));
            if (t.MaxEnergy <= 0 || double.IsNaN(t.MaxEnergy))
                issues.Add(Err("Типы клеток", loc, "Максимальная энергия должна быть положительным числом.", ""));
            if (t.MaintenanceCost < 0 || double.IsNaN(t.MaintenanceCost))
                issues.Add(Err("Типы клеток", loc, "Расход на поддержание должен быть числом ≥ 0.", ""));
            if (t.GrowEnergyCostPerRadius < 0 || double.IsNaN(t.GrowEnergyCostPerRadius))
                issues.Add(Err("Типы клеток", loc, "Стоимость роста должна быть числом ≥ 0.", ""));
            if (t.DivisionEnergyCost < 0 || double.IsNaN(t.DivisionEnergyCost))
                issues.Add(Err("Типы клеток", loc, "Стоимость деления должна быть числом ≥ 0.", ""));
            if (!IsHexColor(t.Color))
                issues.Add(Warn("Типы клеток", loc, $"Цвет «{t.Color}» не распознан.", "HEX, например #7bd88f."));
        }
    }

    private static void ValidateGenomes(
        SimulationSetup setup, List<ConfigIssue> issues,
        HashSet<int> morphogenIds, HashSet<int> typeIds,
        Dictionary<GenomeConfig, HashSet<string>> geneIdsByGenome)
    {
        var seenGenomeIds = new HashSet<int>();
        foreach (var genome in setup.Genomes)
        {
            string gLoc = $"геном {genome.Id}";
            if (!seenGenomeIds.Add(genome.Id))
                issues.Add(Err("Геномы", gLoc, "Повторяющийся идентификатор генома.", "Идентификаторы должны быть уникальны."));

            var keys = new HashSet<string>();
            geneIdsByGenome[genome] = keys;

            if (genome.Genes.Count == 0)
                issues.Add(Warn("Геномы", gLoc, "В геноме нет ни одного гена.", "Добавьте хотя бы один ген."));

            foreach (var gene in genome.Genes)
            {
                string loc = $"{gLoc}, ген «{gene.Key}»";
                if (string.IsNullOrWhiteSpace(gene.Key))
                    issues.Add(Err("Геномы", gLoc, "Ген без идентификатора и имени.", "Задайте id или name."));
                else if (!keys.Add(gene.Key))
                    issues.Add(Err("Геномы", loc, "Повторяющийся идентификатор гена внутри генома.", "Идентификаторы генов должны быть уникальны."));
                if (gene.ActivationDelay < 0)
                    issues.Add(Err("Геномы", loc, "Задержка активации не может быть отрицательной.", ""));
                if (gene.Effects.Count == 0)
                    issues.Add(Warn("Геномы", loc, "У гена нет эффектов — он ни на что не влияет.", "Добавьте хотя бы один эффект."));
                if (string.IsNullOrWhiteSpace(gene.Description) && string.IsNullOrWhiteSpace(gene.DisplayName))
                    issues.Add(Warn("Геномы", loc, "У гена нет понятного названия и описания.",
                        "Заполните название и назначение, чтобы ген был понятен."));

                var groups = gene.EffectiveGroups();
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    var group = groups[gi];
                    string groupLoc = $"{loc}, группа {gi + 1}";
                    if (!string.Equals(group.Operator, "And", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(group.Operator, "Or", StringComparison.OrdinalIgnoreCase))
                        issues.Add(Err("Геномы", groupLoc, $"Неизвестный оператор группы «{group.Operator}».", "Используйте And или Or."));
                    if (group.Regulators.Count == 0)
                        issues.Add(Warn("Геномы", groupLoc, "Пустая группа условий.", "Пустая группа всегда истинна — добавьте условия или удалите группу."));
                    foreach (var reg in group.Regulators)
                        ValidateRegulator(issues, groupLoc, reg, morphogenIds, typeIds);
                }

                foreach (var effect in gene.Effects)
                    ValidateEffect(issues, loc, effect, morphogenIds, typeIds, keys);

                // M1.5+: conflicting simultaneously-active Divide effects in one gene.
                // Multiple Divide effects are legal (they accumulate progress), but
                // they must not disagree about the child phenotype: mixing "inherit"
                // (null) with an explicit phenotype, or two different phenotypes,
                // makes the outcome depend on effect order.
                var divideEffects = gene.Effects.Where(e => Eq(e.Kind, "Divide")).ToList();
                if (divideEffects.Count > 1)
                {
                    var distinct = divideEffects.Select(e => e.ChildTypeId).Distinct().ToList();
                    if (distinct.Count > 1)
                        issues.Add(Err("Геномы", loc,
                            $"Ген содержит {divideEffects.Count} эффекта деления с разными фенотипами потомка.",
                            "Задайте одинаковый фенотип потомка во всех эффектах деления (или везде «как у родителя»)."));
                }
            }

            // Second pass: silence references to genes that must exist.
            foreach (var gene in genome.Genes)
                foreach (var effect in gene.Effects.Where(e => Eq(e.Kind, "SilenceGene")))
                {
                    if (string.IsNullOrWhiteSpace(effect.Gene))
                        issues.Add(Err("Геномы", $"{gLoc}, ген «{gene.Key}»", "Эффект подавления без указания гена.", "Укажите «Ген, который нужно подавить»."));
                    else if (!genome.Genes.Any(g => string.Equals(g.Key, effect.Gene, StringComparison.Ordinal)))
                        issues.Add(Warn("Геномы", $"{gLoc}, ген «{gene.Key}»", $"Подавляемый ген «{effect.Gene}» не найден в этом геноме.", "Проверьте идентификатор."));
                }
        }
    }

    private static void ValidateRegulator(
        List<ConfigIssue> issues, string loc, RegulatorConfig reg, HashSet<int> morphogenIds, HashSet<int> typeIds)
    {
        if (!Localization.IsKnownRegulator(reg.Kind))
        {
            issues.Add(Err("Геномы", loc, $"Неизвестный регулятор «{reg.Kind}».", "Выберите поддерживаемый регулятор."));
            return;
        }

        if (reg.Min > reg.Max)
            issues.Add(Err("Геномы", loc, "Минимум условия больше максимума.", "Убедитесь, что min ≤ max."));
        if (double.IsNaN(reg.Min) || double.IsNaN(reg.Max))
            issues.Add(Err("Геномы", loc, "Границы условия не могут быть NaN.", "Задайте конечные числа."));

        switch (reg.Kind)
        {
            case "Morphogen":
                if (!morphogenIds.Contains(reg.Morphogen))
                    issues.Add(Err("Геномы", loc, $"Ссылка на несуществующий морфоген {reg.Morphogen}.", "Выберите существующий морфоген."));
                break;
            case "Property":
                if (!Localization.IsKnownProperty(reg.Property))
                    issues.Add(Err("Геномы", loc, $"Неизвестное свойство «{reg.Property}».", "Доступны: energy, radius, age, neighbours."));
                break;
            case "Phenotype":
                if (!typeIds.Contains(reg.TypeId))
                    issues.Add(Err("Геномы", loc, $"Ссылка на несуществующий тип клеток {reg.TypeId}.", "Выберите существующий тип."));
                break;
            case "State":
                if (reg.TypeId < 0 || reg.TypeId >= ValidStates.Length)
                    issues.Add(Err("Геномы", loc, $"Недопустимое состояние клетки {reg.TypeId}.", "0=Живая, 1=Делится, 2=Отмирает."));
                break;
            case "Timer":
                if (string.IsNullOrWhiteSpace(reg.Timer))
                    issues.Add(Err("Геномы", loc, "Не указано имя таймера.", "Задайте имя таймера."));
                break;
        }
    }

    private static void ValidateEffect(
        List<ConfigIssue> issues, string loc, GeneEffectConfig e,
        HashSet<int> morphogenIds, HashSet<int> typeIds, HashSet<string> geneKeys)
    {
        if (!Localization.IsKnownEffect(e.Kind))
        {
            issues.Add(Err("Геномы", loc, $"Неизвестный эффект «{e.Kind}».", "Выберите поддерживаемый эффект."));
            return;
        }

        switch (e.Kind)
        {
            case "EmitMorphogen":
                if (!morphogenIds.Contains(e.Morphogen))
                    issues.Add(Err("Геномы", loc, $"Выделение: несуществующий морфоген {e.Morphogen}.", "Выберите морфоген."));
                if (e.Rate < 0 || double.IsNaN(e.Rate))
                    issues.Add(Err("Геномы", loc, "Интенсивность выделения должна быть числом ≥ 0.", ""));
                break;
            case "Absorb":
                if (!morphogenIds.Contains(e.Morphogen))
                    issues.Add(Err("Геномы", loc, $"Поглощение: несуществующий морфоген {e.Morphogen}.", "Выберите морфоген."));
                break;
            case "Grow":
                if (e.Rate < 0 || double.IsNaN(e.Rate))
                    issues.Add(Err("Геномы", loc, "Прирост размера должен быть числом ≥ 0.", ""));
                break;
            case "Divide":
                if (e.Rate < 0 || double.IsNaN(e.Rate))
                    issues.Add(Err("Геномы", loc, "Скорость деления должна быть числом ≥ 0.", ""));
                if (e.ChildTypeId is { } childId && !typeIds.Contains(childId))
                    issues.Add(Err("Геномы", loc, $"Деление: несуществующий фенотип потомка {childId}.", "Выберите существующий тип клеток или оставьте «как у родителя»."));
                break;
            case "SetCellType":
                if (!typeIds.Contains(e.TypeId))
                    issues.Add(Err("Геномы", loc, $"Смена типа: несуществующий тип {e.TypeId}.", "Выберите тип клеток."));
                break;
            case "StartTimer":
                if (string.IsNullOrWhiteSpace(e.Timer))
                    issues.Add(Err("Геномы", loc, "Запуск таймера без имени таймера.", "Задайте имя таймера."));
                if (e.Duration < 0 || double.IsNaN(e.Duration))
                    issues.Add(Err("Геномы", loc, "Длительность таймера должна быть числом ≥ 0.", ""));
                break;
            case "Apoptosis":
                if (e.Probability < 0 || double.IsNaN(e.Probability))
                    issues.Add(Err("Геномы", loc, "Вероятность апоптоза должна быть числом ≥ 0.", ""));
                break;
            case "SilenceGene":
                if (string.IsNullOrWhiteSpace(e.Gene))
                    issues.Add(Err("Геномы", loc, "Подавление гена без указания гена.", "Задайте ген."));
                break;
            case "BlockDivision":
                break;
        }
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsHexColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return false;
        string c = color.StartsWith('#') ? color[1..] : color;
        return (c.Length == 6 || c.Length == 8) && c.All(Uri.IsHexDigit);
    }

    private static ConfigIssue Err(string section, string location, string message, string hint)
        => new() { Severity = ConfigIssueSeverity.Error, Section = section, Location = location, Message = message, Hint = hint };

    private static ConfigIssue Warn(string section, string location, string message, string hint)
        => new() { Severity = ConfigIssueSeverity.Warning, Section = section, Location = location, Message = message, Hint = hint };
}
