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
        "EmitMorphogen", "Absorb", "Grow", "Divide", "SetCellType",
        "StartTimer", "Apoptosis", "SilenceGene", "BlockDivision"
    };

    /// <summary>Known cell properties usable by the "Property" regulator.</summary>
    public static readonly string[] PropertyNames =
    {
        "energy", "radius", "age", "neighbours"
    };

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
        ["Absorb"] = "Поглощение морфогена",
        ["Grow"] = "Рост",
        ["Divide"] = "Деление",
        ["SetCellType"] = "Смена типа клетки",
        ["StartTimer"] = "Запуск таймера",
        ["Apoptosis"] = "Апоптоз (программируемая гибель)",
        ["SilenceGene"] = "Подавление гена",
        ["BlockDivision"] = "Блокировка деления",
    };

    private static readonly Dictionary<string, string> EffectShort = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EmitMorphogen"] = "клетка выбрасывает морфоген в среду",
        ["Absorb"] = "клетка поглощает морфоген и получает энергию",
        ["Grow"] = "увеличивает радиус клетки, расходуя энергию",
        ["Divide"] = "накапливает готовность к делению",
        ["SetCellType"] = "переключает фенотип (тип) клетки",
        ["StartTimer"] = "запускает именованный таймер",
        ["Apoptosis"] = "запускает гибель клетки",
        ["SilenceGene"] = "навсегда подавляет другой ген в этой клетке",
        ["BlockDivision"] = "запрещает деление, пока ген активен",
    };

    private static readonly Dictionary<string, string> PropertyNamesRu = new(StringComparer.OrdinalIgnoreCase)
    {
        ["energy"] = "Энергия",
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

    /// <summary>
    /// Human-readable gene name: the explicit display name, otherwise a name
    /// derived from the id, otherwise a clear placeholder that flags the need to
    /// fill in a description.
    /// </summary>
    public static string GeneName(GeneConfig gene)
    {
        if (!string.IsNullOrWhiteSpace(gene.DisplayName)) return gene.DisplayName!;
        string key = gene.Key;
        if (!string.IsNullOrWhiteSpace(key)) return $"Ген «{key}» (без названия)";
        return "Безымянный ген";
    }

    /// <summary>Human-readable morphogen name.</summary>
    public static string MorphogenName(MorphogenConfig m)
        => string.IsNullOrWhiteSpace(m.DisplayName) ? m.Name : m.DisplayName!;

    /// <summary>Human-readable cell type name.</summary>
    public static string CellTypeName(CellTypeConfig t)
        => string.IsNullOrWhiteSpace(t.DisplayName) ? t.Name : t.DisplayName!;

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
