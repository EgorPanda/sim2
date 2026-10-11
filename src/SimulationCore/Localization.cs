namespace SimulationCore;

/// <summary>
/// Centralized Russian localization catalog for the simulation domain. It maps
/// technical identifiers (regulator kinds, effect kinds, cell properties,
/// comparison operators, cell states) to human-readable Russian names and short
/// explanations. Keeping it in the core lets tests guarantee full coverage and
/// prevents UI scripts from duplicating the same texts.
/// </summary>
public static class Localization
{
    /// <summary>Known regulator kinds supported by the engine.</summary>
    public static readonly string[] RegulatorKinds =
    {
        "Morphogen", "Property", "Phenotype", "State", "Timer"
    };

    /// <summary>Known gene effect kinds supported by the engine.</summary>
    public static readonly string[] EffectKinds =
    {
        "EmitMorphogen", "Grow", "Divide", "SetCellType",
        "StartTimer", "Apoptosis", "SilenceGene", "BlockDivision", "Move"
    };

    /// <summary>Known cell properties usable by the "Property" regulator.</summary>
    public static readonly string[] PropertyNames =
    {
        "radius", "age", "neighbours"
    };

    /// <summary>Known morphogen types.</summary>
    public static readonly string[] MorphogenTypes = { "External", "Internal" };

    private static readonly Dictionary<string, string> RegulatorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Morphogen"] = "Концентрация морфогена",
        ["Property"] = "Свойство клетки",
        ["Phenotype"] = "Тип клетки (фенотип)",
        ["State"] = "Состояние клетки",
        ["Timer"] = "Состояние таймера",
    };

    private static readonly Dictionary<string, string> EffectNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EmitMorphogen"] = "Выделение морфогена",
        ["Grow"] = "Рост",
        ["Divide"] = "Деление",
        ["SetCellType"] = "Смена типа клетки",
        ["StartTimer"] = "Запуск таймера",
        ["Apoptosis"] = "Апоптоз (программируемая гибель)",
        ["SilenceGene"] = "Подавление гена",
        ["BlockDivision"] = "Блокировка деления",
        ["Move"] = "Движение по градиенту",
    };

    private static readonly Dictionary<string, string> EffectShort = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EmitMorphogen"] = "клетка выбрасывает морфоген в среду",
        ["Grow"] = "увеличивает радиус клетки",
        ["Divide"] = "накапливает готовность к делению",
        ["SetCellType"] = "переключает фенотип (тип) клетки",
        ["StartTimer"] = "запускает именованный таймер",
        ["Apoptosis"] = "запускает гибель клетки",
        ["SilenceGene"] = "навсегда подавляет другой ген в этой клетке",
        ["BlockDivision"] = "запрещает деление, пока ген активен",
        ["Move"] = "смещает клетку вдоль (или против) градиента морфогена",
    };

    private static readonly Dictionary<string, string> PropertyNamesRu = new(StringComparer.OrdinalIgnoreCase)
    {
        ["radius"] = "Радиус",
        ["age"] = "Возраст",
        ["neighbours"] = "Количество соседей",
    };

    private static readonly Dictionary<string, string> StateNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alive"] = "Живая",
        ["Dividing"] = "Делится",
        ["Dying"] = "Отмирает",
    };

    private static readonly Dictionary<string, string> MorphogenTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["External"] = "Внешний",
        ["Internal"] = "Внутренний",
    };

    private static readonly Dictionary<string, string> GeneStatusNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["notConfigured"] = "Не настроен",
        ["morphogenMissing"] = "Не найден морфоген",
        ["belowThreshold"] = "Концентрация ниже порога",
        ["waitingConditions"] = "Обязательный сигнал присутствует, ожидаются дополнительные условия",
        ["active"] = "Ген активен",
        ["suppressed"] = "Ген подавлен",
    };

    /// <summary>Russian name of a regulator kind, or a safe fallback.</summary>
    public static string Regulator(string kind) => Lookup(RegulatorNames, kind, "Регулятор");

    /// <summary>Russian name of an effect kind, or a safe fallback.</summary>
    public static string Effect(string kind) => Lookup(EffectNames, kind, "Эффект");

    /// <summary>Short explanation of what an effect does.</summary>
    public static string EffectExplanation(string kind) => Lookup(EffectShort, kind, "неизвестный эффект");

    /// <summary>Russian name of a cell property.</summary>
    public static string Property(string property) => Lookup(PropertyNamesRu, property, $"Свойство «{property}»");

    /// <summary>Russian name of a cell state.</summary>
    public static string State(CellState state) => Lookup(StateNames, state.ToString(), "Состояние");

    /// <summary>Russian name of a cell state given its integer value.</summary>
    public static string StateByValue(int value) => State((CellState)value);

    /// <summary>True when the regulator kind is supported by the engine.</summary>
    public static bool IsKnownRegulator(string kind)
        => !string.IsNullOrEmpty(kind) && RegulatorKinds.Contains(kind, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the effect kind is supported by the engine.</summary>
    public static bool IsKnownEffect(string kind)
        => !string.IsNullOrEmpty(kind) && EffectKinds.Contains(kind, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the property is supported by the engine.</summary>
    public static bool IsKnownProperty(string property)
        => !string.IsNullOrEmpty(property) && PropertyNames.Contains(property, StringComparer.OrdinalIgnoreCase);

    /// <summary>Russian name of a morphogen type.</summary>
    public static string MorphogenType(string type) => Lookup(MorphogenTypeNames, type, "Тип морфогена");

    /// <summary>True when the morphogen type is supported.</summary>
    public static bool IsKnownMorphogenType(string type)
        => !string.IsNullOrEmpty(type) && MorphogenTypes.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));

    /// <summary>Russian status text for a gene card.</summary>
    public static string GeneStatus(string key) => Lookup(GeneStatusNames, key, "Состояние гена");

    /// <summary>
    /// Human-readable gene name: the explicit name, otherwise a name derived from
    /// the id, otherwise a clear placeholder.
    /// </summary>
    public static string GeneName(GeneConfig gene)
    {
        if (!string.IsNullOrWhiteSpace(gene.Name)) return gene.Name!;
        string key = gene.Key;
        if (!string.IsNullOrWhiteSpace(key)) return $"Ген «{key}» (без названия)";
        return "Безымянный ген";
    }

    /// <summary>Human-readable morphogen name.</summary>
    public static string MorphogenName(MorphogenConfig m)
        => string.IsNullOrWhiteSpace(m.Name) ? $"Морфоген {m.Id}" : m.Name!;

    /// <summary>Human-readable cell type name.</summary>
    public static string CellTypeName(CellTypeConfig t)
        => string.IsNullOrWhiteSpace(t.Name) ? $"Тип {t.Id}" : t.Name!;

    /// <summary>
    /// Unified label for morphogen selectors: [ID] Name — Role — Type.
    /// </summary>
    public static string MorphogenLabel(MorphogenConfig m)
    {
        string name = MorphogenName(m);
        string role = string.IsNullOrWhiteSpace(m.Role) ? "без роли" : m.Role;
        string type = MorphogenType(m.Type).ToLowerInvariant();
        return $"[{m.Id}] {name} — {role} — {type}";
    }

    /// <summary>Short unified label for inline morphogen references.</summary>
    public static string MorphogenInline(MorphogenConfig m)
    {
        string name = MorphogenName(m);
        string type = MorphogenType(m.Type).ToLowerInvariant();
        return $"«{name}» (ID {m.Id}, {type})";
    }

    /// <summary>Russian name for a division inheritance mode.</summary>
    public static string DivisionInheritance(string mode) => mode.ToLowerInvariant() switch
    {
        "reset" => "Не наследовать — дочь получает 0",
        "copy" => "Копировать — дочь получает такое же значение",
        "split" => "Разделить — пополам между родителем и дочерью",
        _ => $"Неизвестный режим «{mode}»"
    };

    /// <summary>
    /// Describes the comparison encoded by a regulator's [min, max] interval in
    /// words, so the UI can present an operator without inventing new fields.
    /// </summary>
    public static string DescribeRange(double min, double max)
    {
        bool hasMin = !double.IsNegativeInfinity(min);
        bool hasMax = !double.IsPositiveInfinity(max);
        if (hasMin && hasMax)
        {
            if (Math.Abs(min - max) < 1e-12) return $"= {Num.Fmt(max)}";
            return $"{Num.Fmt(min)} … {Num.Fmt(max)}";
        }
        if (hasMin) return $"≥ {Num.Fmt(min)}";
        if (hasMax) return $"≤ {Num.Fmt(max)}";
        return "любое значение";
    }

    private static string Lookup(Dictionary<string, string> map, string key, string fallbackPrefix)
    {
        if (!string.IsNullOrEmpty(key) && map.TryGetValue(key, out var value)) return value;
        return $"{fallbackPrefix} «{key}»";
    }

    /// <summary>Russian description of how a group's regulators are combined.</summary>
    public static string GroupOperator(string op)
        => string.Equals(op, "Or", StringComparison.OrdinalIgnoreCase)
            ? "любое условие (ИЛИ)"
            : "все условия (И)";
}
