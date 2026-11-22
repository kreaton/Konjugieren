namespace Konjugieren.Data;

/// <summary>
/// The weak (regular) German paradigm: machen → machte, gemacht. Every form a verb has is compared
/// with it, so strong verbs (fahren → fährt, fuhr, gefahren) show their vowel changes as irregular.
/// </summary>
/// <remarks>
/// Spelling rules that are still regular: an extra e after stems in -d/-t and in consonant + m/n
/// (arbeitest, arbeitete, atmet, rechnet); du + -t after s, ß, x, z (du reist); -eln/-ern verbs
/// (ich sammle, wir sammeln). The ge- of the past participle is accepted with or without, because
/// it follows stress (gemacht, but besucht, studiert) rather than the verb's class.
/// Separable verbs put the particle last in a main clause and first in a subordinate one
/// (ruft an / anruft); reflexive verbs carry mich, dich, sich … the same way.
/// </remarks>
public static class RegularConjugator
{
    public sealed record Key(int Tense, int Person, bool Subordinate);

    private static readonly string[] Reflexive = ["", "mich", "dich", "sich", "uns", "euch", "sich"];

    /// <summary>
    /// The accepted regular forms per (tense, person, word order), or null when the infinitive doesn't fit
    /// the model. <paramref name="particle"/> is the separable part as it stands in a main clause:
    /// "an" (ruft an), "wieder gut" (macht wieder gut).
    /// </summary>
    public static Dictionary<Key, HashSet<string>>? Generate(string infinitive, string? particle)
    {
        var reflexive = infinitive.StartsWith("sich ", StringComparison.Ordinal);
        var core = reflexive ? infinitive[5..] : infinitive;
        var joined = particle?.Replace(" ", "");
        var separable = joined is not null && core.StartsWith(joined, StringComparison.Ordinal) && core.Length > joined.Length + 2;
        var prefix = separable ? joined : null;
        var verb = prefix is null ? core : core[prefix.Length..];
        if (verb.Contains(' ')) return null;

        var paradigm = Paradigm(verb);
        if (paradigm is null) return null;

        var result = new Dictionary<Key, HashSet<string>>();
        foreach (var ((tense, person), forms) in paradigm)
        {
            if (person == 0)
            {
                result[new(tense, 0, false)] = forms.Select(f => NonFinite(tense, f, verb, prefix, reflexive)).ToHashSet();
                continue;
            }
            var pronoun = reflexive ? Reflexive[person] : null;
            result[new(tense, person, false)] = forms.Select(f => Join(f, pronoun, separable ? particle : null)).ToHashSet();
            if (tense != TenseId.Imperative && (pronoun is not null || prefix is not null))
                result[new(tense, person, true)] = forms.Select(f => (pronoun is null ? "" : pronoun + " ") + prefix + f).ToHashSet();
        }
        return result;
    }

    /// <summary>
    /// Whether a stored form is the regular one. The ge- of the past participle and the zu of the
    /// zu-infinitive go after a stressed first part of a compound (mausgerutscht, mauszurutschen), so
    /// they are accepted anywhere in the word.
    /// </summary>
    public static bool IsRegular(Dictionary<Key, HashSet<string>> model, Key key, string value)
    {
        if (!model.TryGetValue(key, out var expected)) return true;
        if (expected.Contains(value)) return true;
        return key.Tense switch
        {
            TenseId.PastParticiple => Removals(value, "ge").Any(expected.Contains),
            TenseId.ZuInfinitive => model.TryGetValue(new(TenseId.Infinitive, 0, false), out var inf)
                                    && Removals(value, "zu").Any(inf.Contains),
            _ => false,
        };
    }

    /// <summary>The word with one occurrence of the infix taken out: "mauszurutschen" → "mausrutschen".</summary>
    private static IEnumerable<string> Removals(string word, string infix)
    {
        for (var i = word.IndexOf(infix, StringComparison.Ordinal); i >= 0; i = word.IndexOf(infix, i + 1, StringComparison.Ordinal))
            yield return word.Remove(i, infix.Length).Replace("  ", " ").Trim();
    }

    /// <summary>Main-clause order: verb, then reflexive pronoun, then particle (kenne mich aus).</summary>
    private static string Join(string form, string? pronoun, string? particle) =>
        form + (pronoun is null ? "" : " " + pronoun) + (particle is null ? "" : " " + particle);

    private static string NonFinite(int tense, string form, string verb, string? prefix, bool reflexive)
    {
        var value = tense switch
        {
            TenseId.ZuInfinitive => prefix is null ? "zu " + verb : prefix + "zu" + verb,
            _ => prefix + form,
        };
        return reflexive ? "sich " + value : value;
    }

    private static Dictionary<(int Tense, int Person), string[]>? Paradigm(string verb)
    {
        var p = new Dictionary<(int, int), string[]>();
        void Add(int tense, int person, params string[] forms) => p[(tense, person)] = forms.Distinct().ToArray();

        if (verb.Length > 3 && (verb.EndsWith("eln", StringComparison.Ordinal) || verb.EndsWith("ern", StringComparison.Ordinal)))
        {
            // sammeln: stem sammel, contracted samml (ich sammle); wandern: wander, wandr.
            var stem = verb[..^1];
            var contracted = stem[..^2] + stem[^1];
            string[] s1 = [contracted + "e", stem + "e"];
            // Wiktionary also lists the uncontracted and the e-less forms (ich radel, du sammelest).
            string[] s1Present = [contracted + "e", stem + "e", stem];
            Add(TenseId.Infinitive, 0, verb);
            Add(TenseId.PresentParticiple, 0, verb + "d");
            Add(TenseId.PastParticiple, 0, "ge" + stem + "t", stem + "t");
            Add(TenseId.ZuInfinitive, 0, verb);
            Add(TenseId.Present, 1, s1Present);
            Add(TenseId.Present, 2, stem + "st");
            Add(TenseId.Present, 3, stem + "t");
            Add(TenseId.Present, 4, verb);
            Add(TenseId.Present, 5, stem + "t");
            Add(TenseId.Present, 6, verb);
            Weak(stem, needsE: false);
            Add(TenseId.SubjunctiveI, 1, s1);
            Add(TenseId.SubjunctiveI, 2, stem + "st", contracted + "est", stem + "est");
            Add(TenseId.SubjunctiveI, 3, s1);
            Add(TenseId.SubjunctiveI, 4, verb);
            Add(TenseId.SubjunctiveI, 5, stem + "t", contracted + "et", stem + "et");
            Add(TenseId.SubjunctiveI, 6, verb);
            Add(TenseId.Imperative, 2, contracted + "e", stem + "e", stem);
            Add(TenseId.Imperative, 5, stem + "t");
            return p;
        }

        // machen → mach-; tun, sein → tu-, sei- (the few infinitives in a vowel + -n).
        string? s = verb.Length >= 4 && verb.EndsWith("en", StringComparison.Ordinal) ? verb[..^2]
            : verb.Length >= 3 && verb[^1] == 'n' && IsVowel(verb[^2]) ? verb[..^1]
            : null;
        if (s is null) return null;
        var e = NeedsE(s);
        var sibilant = s[^1] is 's' or 'ß' or 'x' or 'z';
        var t = e ? "et" : "t";

        Add(TenseId.Infinitive, 0, verb);
        Add(TenseId.PresentParticiple, 0, verb + "d");
        Add(TenseId.PastParticiple, 0, "ge" + s + t, s + t);
        Add(TenseId.ZuInfinitive, 0, verb);
        Add(TenseId.Present, 1, s + "e");
        Add(TenseId.Present, 2, e ? [s + "est"] : sibilant ? [s + "t", s + "est"] : [s + "st"]);
        Add(TenseId.Present, 3, s + t);
        Add(TenseId.Present, 4, verb);
        Add(TenseId.Present, 5, s + t);
        Add(TenseId.Present, 6, verb);
        Weak(s, e);
        Add(TenseId.SubjunctiveI, 1, s + "e");
        Add(TenseId.SubjunctiveI, 2, s + "est");
        Add(TenseId.SubjunctiveI, 3, s + "e");
        Add(TenseId.SubjunctiveI, 4, verb);
        Add(TenseId.SubjunctiveI, 5, s + "et");
        Add(TenseId.SubjunctiveI, 6, verb);
        Add(TenseId.Imperative, 2, s, s + "e");
        Add(TenseId.Imperative, 5, s + t);
        return p;

        // Präteritum, and the Konjunktiv II, which for weak verbs is the same.
        void Weak(string stem, bool needsE)
        {
            var te = stem + (needsE ? "ete" : "te");
            string[] endings = ["", "", "st", "", "n", "t", "n"];
            for (var person = 1; person <= 6; person++)
            {
                Add(TenseId.Preterite, person, te + endings[person]);
                Add(TenseId.SubjunctiveII, person, te + endings[person]);
            }
        }
    }

    /// <summary>arbeit-, red-, atm-, rechn-, öffn-: an e goes before -st, -t and -te. Not after l, r, m, n or a lengthening h (lernst, wohnst).</summary>
    private static bool NeedsE(string stem)
    {
        var last = stem[^1];
        if (last is 'd' or 't') return true;
        if (last is not ('m' or 'n') || stem.Length < 2) return false;
        var before = stem[^2];
        if (IsVowel(before) || before is 'l' or 'r' or 'm' or 'n') return false;
        return !(before == 'h' && stem.Length > 2 && IsVowel(stem[^3]));
    }

    private static bool IsVowel(char c) => "aeiouäöüy".Contains(char.ToLowerInvariant(c));
}
