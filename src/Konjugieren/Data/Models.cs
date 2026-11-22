namespace Konjugieren.Data;

public sealed record VerbSummary(string Infinitive, bool HasConjugation, bool? IsIrregular);

public sealed record FormHit(string Infinitive, string TenseName, int Person);

/// <summary>One stored form. <see cref="Subordinate"/> marks the verb-final word order (dass ich anrufe).</summary>
public sealed record VerbForm(int TenseId, int Person, string Value, string? Note, bool Subordinate, bool? Irregular);

public sealed class VerbDetail
{
    public int Id { get; init; }
    public string Infinitive { get; init; } = "";
    /// <summary>KaikkiVerbs' class, from the German Wiktionary table: weak, 7 strong, irregular weak, present …</summary>
    public string? ConjugationClass { get; init; }
    public bool Reflexive { get; init; }
    /// <summary>The separable prefix (an in anrufen), or null.</summary>
    public string? Particle { get; init; }
    public string? Auxiliary { get; init; }
    public bool HasConjugation { get; init; }
    public bool? IsIrregular { get; init; }
    public int? IrregularForms { get; init; }
    /// <summary>sich sputen → sputen, anrufen → rufen, when that verb is in the dictionary.</summary>
    public string? BaseInfinitive { get; init; }
    public string? LemmaInfinitive { get; init; }
    public string? Etymology { get; init; }
    public string? Url { get; init; }
    /// <summary>The source marks the Konjunktiv II as formal and rare: speech uses the würde form.</summary>
    public bool SubjunctiveIIRare { get; init; }
    public List<string> Definitions { get; } = [];
    public List<string> Pronunciations { get; } = [];
    public List<VerbForm> Forms { get; } = [];

    /// <summary>A weak verb where almost nothing matches the weak model: the Wiktionary table is most likely broken.</summary>
    public bool LooksUnreliable => IrregularForms >= 25 && ConjugationClass == "weak";
}

/// <summary>Tense ids, as KaikkiVerbs stores them for German.</summary>
public static class TenseId
{
    public const int Infinitive = 1, PresentParticiple = 3, PastParticiple = 4, ZuInfinitive = 5,
        Present = 10, Preterite = 11, SubjunctiveI = 30, SubjunctiveII = 31, Imperative = 40;

    public static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
    {
        [Infinitive] = "Infinitiv", [PresentParticiple] = "Partizip I", [PastParticiple] = "Partizip II",
        [ZuInfinitive] = "zu-Infinitiv", [Present] = "Präsens", [Preterite] = "Präteritum",
        [SubjunctiveI] = "Konjunktiv I", [SubjunctiveII] = "Konjunktiv II", [Imperative] = "Imperativ",
    };
}
