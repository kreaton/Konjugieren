namespace Konjugieren.Data;

/// <summary>One displayed piece of text; irregular pieces are highlighted.</summary>
public sealed record Piece(string Text, bool Irregular);

public sealed class Row
{
    public string Pronoun { get; init; } = "";
    public List<Piece> Primary { get; } = [];
    public List<VerbForm> Variants { get; } = [];
}

public sealed class TenseCard
{
    /// <summary>The anchor in the tense guide: "konjunktiv-ii-plusquamperfekt".</summary>
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string English { get; init; } = "";
    public bool Compound { get; init; }
    public List<Row> Rows { get; } = [];
    public string? Footnote { get; set; }
}

public sealed class MoodSection
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public List<TenseCard> Cards { get; } = [];
}

public sealed class NonFinite
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Compound { get; init; }
    public List<Piece> Primary { get; } = [];
    public List<VerbForm> Variants { get; } = [];
}

public sealed class ConjugationView
{
    public List<NonFinite> NonFinite { get; } = [];
    public List<MoodSection> Moods { get; } = [];
    public string? AuxiliaryNote { get; set; }
}

/// <summary>
/// Arranges the stored simple tenses into tables and builds the compound ones (not in the source
/// data): Perfekt and Plusquamperfekt from haben/sein + Partizip II, Futur I and II from werden,
/// and the würde form of the Konjunktiv II. With <c>subordinate</c> every form is shown in
/// verb-final word order, as after dass, weil or wenn: dass ich angerufen habe.
/// </summary>
public static class ConjugationBuilder
{
    private static readonly string[] Plain = ["", "ich", "du", "er/sie/es", "wir", "ihr", "sie/Sie"];
    private static readonly string[] Dass = ["", "dass ich", "dass du", "dass er/sie/es", "dass wir", "dass ihr", "dass sie/Sie"];
    private static readonly string[] ReflexivePronoun = ["", "mich", "dich", "sich", "uns", "euch", "sich"];

    public static ConjugationView Build(VerbDetail verb, IReadOnlyDictionary<(string Aux, int Tense, int Person), string> auxForms, bool subordinate)
    {
        var view = new ConjugationView();
        var cells = verb.Forms
            .GroupBy(f => (f.TenseId, f.Person))
            .ToDictionary(g => g.Key, g => g.ToList());

        // The forms in the chosen word order. Verbs that are neither separable nor reflexive have one order only.
        List<VerbForm> Cell(int tense, int person)
        {
            var all = cells.GetValueOrDefault((tense, person)) ?? [];
            var sub = all.Where(f => f.Subordinate).ToList();
            return subordinate && sub.Count > 0 ? sub : all.Where(f => !f.Subordinate).ToList();
        }

        var pronouns = subordinate ? Dass : Plain;

        // haben or sein. Verbs that take both (fahren: hat/ist gefahren) show both.
        var both = !verb.Reflexive && verb.Auxiliary == "haben|sein";
        var aux = verb.Reflexive ? "haben" : verb.Auxiliary == "sein" ? "sein" : "haben";
        view.AuxiliaryNote = both
            ? "This verb takes haben or sein, depending on the meaning: sein for movement from one place to another, haben otherwise (ich bin nach Berlin gefahren, ich habe das Auto gefahren)."
            : verb.Auxiliary is null && !verb.Reflexive ? "The source gives no auxiliary; compound tenses are shown with haben." : null;

        string? AuxForm(int tense, int person) =>
            both
                ? auxForms.TryGetValue(("haben", tense, person), out var h) && auxForms.TryGetValue(("sein", tense, person), out var s) ? $"{h}/{s}" : null
                : auxForms.TryGetValue((aux, tense, person), out var a) ? a : null;
        var auxInfinitive = both ? "haben/sein" : aux;

        // Partizip II without "sich" (sich gesputet → gesputet), and the infinitive without it.
        var participleForm = Cell(TenseId.PastParticiple, 0).FirstOrDefault(f => !f.Subordinate);
        var participle = participleForm is null ? null : StripSich(participleForm.Value);
        var ppIrregular = participleForm?.Irregular == true;
        var infinitive = StripSich(verb.Infinitive);

        // Non-finite forms.
        view.NonFinite.Add(Simple("infinitiv", "Infinitiv", Cell(TenseId.Infinitive, 0)));
        if (participle is not null)
        {
            var nf = new NonFinite { Id = "infinitiv", Name = "Infinitiv Perfekt", Compound = true };
            if (verb.Reflexive) nf.Primary.Add(new("sich ", false));
            nf.Primary.Add(new(participle, ppIrregular));
            nf.Primary.Add(new(" " + auxInfinitive, false));
            view.NonFinite.Add(nf);
        }
        view.NonFinite.Add(Simple("zu-infinitiv", "zu-Infinitiv", Cell(TenseId.ZuInfinitive, 0)));
        view.NonFinite.Add(Simple("partizip-i", "Partizip I", Cell(TenseId.PresentParticiple, 0)));
        view.NonFinite.Add(Simple("partizip-ii", "Partizip II", Cell(TenseId.PastParticiple, 0)));
        view.NonFinite.RemoveAll(n => n.Primary.Count == 0);

        TenseCard SimpleCard(string mood, int tense, string name, string english)
        {
            var card = new TenseCard { Id = Anchor(mood, name), Name = name, English = english };
            for (var p = 1; p <= 6; p++)
            {
                var forms = Cell(tense, p);
                var row = new Row { Pronoun = pronouns[p] };
                if (forms.Count > 0)
                {
                    row.Primary.Add(new(forms[0].Value, forms[0].Irregular == true));
                    row.Variants.AddRange(forms.Skip(1));
                }
                card.Rows.Add(row);
            }
            return card;
        }

        // One compound row: the finite auxiliary, the reflexive pronoun and the non-finite part(s),
        // in main-clause order (habe mich gesputet) or verb-final order (mich gesputet habe).
        TenseCard? CompoundCard(string mood, string name, string english, Func<int, string?> finite, IReadOnlyList<Piece> rest)
        {
            var card = new TenseCard { Id = Anchor(mood, name), Name = name, English = english, Compound = true };
            for (var p = 1; p <= 6; p++)
            {
                var row = new Row { Pronoun = pronouns[p] };
                // Defective verbs (regnen: only es regnet) get compounds only where the present exists.
                if (Cell(TenseId.Present, p).Count > 0 && finite(p) is { } f)
                {
                    var pronoun = verb.Reflexive ? ReflexivePronoun[p] + " " : "";
                    if (subordinate)
                    {
                        row.Primary.Add(new(pronoun, false));
                        row.Primary.AddRange(rest);
                        row.Primary.Add(new(" " + f, false));
                    }
                    else
                    {
                        row.Primary.Add(new(f + " " + pronoun, false));
                        row.Primary.AddRange(rest);
                    }
                }
                card.Rows.Add(row);
            }
            return card.Rows.Any(r => r.Primary.Count > 0) ? card : null;
        }

        Piece[] Perfect() => participle is null ? [] : [new(participle, ppIrregular)];
        Piece[] FuturePerfect() => participle is null ? [] : [new(participle, ppIrregular), new(" " + auxInfinitive, false)];
        Piece[] Future() => [new(infinitive, false)];
        string? Werden(int tense, int p) => auxForms.TryGetValue(("werden", tense, p), out var w) ? w : null;

        var indicative = new MoodSection { Id = "indikativ", Name = "Indikativ" };
        indicative.Cards.Add(SimpleCard("indikativ", TenseId.Present, "Präsens", "present"));
        if (participle is not null)
            indicative.Cards.AddIfNotNull(CompoundCard("indikativ", "Perfekt", "present perfect", p => AuxForm(TenseId.Present, p), Perfect()));
        indicative.Cards.Add(SimpleCard("indikativ", TenseId.Preterite, "Präteritum", "simple past"));
        if (participle is not null)
            indicative.Cards.AddIfNotNull(CompoundCard("indikativ", "Plusquamperfekt", "past perfect", p => AuxForm(TenseId.Preterite, p), Perfect()));
        indicative.Cards.AddIfNotNull(CompoundCard("indikativ", "Futur I", "future", p => Werden(TenseId.Present, p), Future()));
        if (participle is not null)
            indicative.Cards.AddIfNotNull(CompoundCard("indikativ", "Futur II", "future perfect", p => Werden(TenseId.Present, p), FuturePerfect()));

        var subjunctiveI = new MoodSection { Id = "konjunktiv-i", Name = "Konjunktiv I" };
        subjunctiveI.Cards.Add(SimpleCard("konjunktiv-i", TenseId.SubjunctiveI, "Präsens", "present"));
        if (participle is not null)
            subjunctiveI.Cards.AddIfNotNull(CompoundCard("konjunktiv-i", "Perfekt", "perfect", p => AuxForm(TenseId.SubjunctiveI, p), Perfect()));
        subjunctiveI.Cards.AddIfNotNull(CompoundCard("konjunktiv-i", "Futur I", "future", p => Werden(TenseId.SubjunctiveI, p), Future()));

        var subjunctiveII = new MoodSection { Id = "konjunktiv-ii", Name = "Konjunktiv II" };
        var k2 = SimpleCard("konjunktiv-ii", TenseId.SubjunctiveII, "Präteritum", "past subjunctive");
        if (verb.SubjunctiveIIRare)
            k2.Footnote = "Mostly written. In speech the würde form is used instead (ich würde " + infinitive + ").";
        subjunctiveII.Cards.Add(k2);
        if (participle is not null)
            subjunctiveII.Cards.AddIfNotNull(CompoundCard("konjunktiv-ii", "Plusquamperfekt", "past perfect", p => AuxForm(TenseId.SubjunctiveII, p), Perfect()));
        subjunctiveII.Cards.AddIfNotNull(CompoundCard("konjunktiv-ii", "würde + Infinitiv", "would", p => Werden(TenseId.SubjunctiveII, p), Future()));
        if (participle is not null)
            subjunctiveII.Cards.AddIfNotNull(CompoundCard("konjunktiv-ii", "würde + Infinitiv Perfekt", "would have", p => Werden(TenseId.SubjunctiveII, p), FuturePerfect()));

        var imperative = new MoodSection { Id = "imperativ", Name = "Imperativ" };
        imperative.Cards.Add(ImperativeCard(Cell(TenseId.Imperative, 2), Cell(TenseId.Imperative, 5),
            cells.GetValueOrDefault((TenseId.SubjunctiveI, 6)), subordinate));

        view.Moods.AddRange([indicative, subjunctiveI, subjunctiveII, imperative]);
        foreach (var mood in view.Moods)
            mood.Cards.RemoveAll(c => c.Rows.All(r => r.Primary.Count == 0));
        view.Moods.RemoveAll(m => m.Cards.Count == 0);
        return view;
    }

    /// <summary>du and ihr from the source; the Sie form is the Konjunktiv I plural with Sie after the verb (rufen Sie an!).</summary>
    private static TenseCard ImperativeCard(List<VerbForm> du, List<VerbForm> ihr, List<VerbForm>? subjunctive, bool subordinate)
    {
        var card = new TenseCard { Id = "imperativ-imperativ", Name = "Imperativ", English = "imperative" };
        foreach (var (label, forms) in new[] { ("(du)", du), ("(ihr)", ihr) })
        {
            var row = new Row { Pronoun = label };
            if (forms.Count > 0)
            {
                row.Primary.Add(new(forms[0].Value, forms[0].Irregular == true));
                row.Variants.AddRange(forms.Skip(1));
            }
            card.Rows.Add(row);
        }
        // Always main-clause order: an imperative has no subordinate form.
        var sie = subjunctive?.FirstOrDefault(f => !f.Subordinate);
        if (sie is not null && card.Rows.Any(r => r.Primary.Count > 0))
        {
            // rufen an → rufen Sie an; sputen sich → sputen Sie sich
            var words = sie.Value.Split(' ').ToList();
            words.Insert(1, "Sie");
            var row = new Row { Pronoun = "(Sie)" };
            row.Primary.Add(new(string.Join(' ', words), sie.Irregular == true));
            card.Rows.Add(row);
        }
        if (subordinate) card.Footnote = "The imperative has no subordinate-clause form.";
        return card;
    }

    private static string StripSich(string s) => s.StartsWith("sich ", StringComparison.Ordinal) ? s[5..] : s;

    private static string Anchor(string mood, string name) =>
        mood + "-" + name.ToLowerInvariant().Replace("ä", "ae").Replace("ü", "ue").Replace("+ ", "").Replace(' ', '-');

    private static NonFinite Simple(string id, string name, List<VerbForm> forms)
    {
        var n = new NonFinite { Id = id, Name = name };
        if (forms.Count > 0)
        {
            n.Primary.Add(new(forms[0].Value, forms[0].Irregular == true));
            n.Variants.AddRange(forms.Skip(1));
        }
        return n;
    }

    private static void AddIfNotNull<T>(this List<T> list, T? item) where T : class
    {
        if (item is not null) list.Add(item);
    }
}
