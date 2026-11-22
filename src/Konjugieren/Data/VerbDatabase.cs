using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace Konjugieren.Data;

/// <summary>
/// Read-only access to verbs.sqlite, the German database built by KaikkiVerbs from Wiktionary
/// (kaikki.org). The gzipped file is downloaded once per page load, inflated into the in-memory
/// Emscripten file system and opened with Microsoft.Data.Sqlite. Blazor WebAssembly is
/// single-threaded, so one shared connection is enough.
/// </summary>
/// <remarks>
/// KaikkiVerbs stores what Wiktionary has, in a layout shared by all its languages. The German
/// specifics are worked out here: the subordinate-clause word order (anruft beside ruft an) as a flag
/// instead of a note, the separable particle, the Konjunktiv II's "formal, rare" label (which only
/// says that speech prefers the würde form), and the regular/irregular marking from
/// <see cref="RegularConjugator"/>.
/// </remarks>
public sealed class VerbDatabase(HttpClient http)
{
    private const string DataUrl = "data/verbs.sqlite.gz";
    private const string LocalPath = "/verbs.sqlite";
    private const string SubordinateNote = "subordinate-clause";

    private Task? _loading;
    private SqliteConnection? _db;
    private Dictionary<(string Aux, int Tense, int Person), string>? _auxForms;
    private readonly Dictionary<int, Analysis> _analyses = [];

    public int VerbCount { get; private set; }

    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        var bytes = await http.GetByteArrayAsync(DataUrl);

        // GitHub Pages serves the .gz as-is; a dev server may already have decoded it.
        var isGzip = bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b;
        await using (var file = File.Create(LocalPath))
        {
            if (isGzip)
            {
                await using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
                await gz.CopyToAsync(file);
            }
            else
            {
                await file.WriteAsync(bytes);
            }
        }

        _db = new SqliteConnection($"Data Source={LocalPath};Mode=ReadOnly;Pooling=False");
        _db.Open();
        VerbCount = Scalar<long>("SELECT count(*) FROM verb WHERE has_conjugation = 1") is var n ? (int)n : 0;
    }

    private SqliteConnection Db => _db ?? throw new InvalidOperationException("Database not loaded; call EnsureLoadedAsync first.");

    /// <summary>Infinitives starting with the prefix; "sputen" also finds "sich sputen".</summary>
    public List<VerbSummary> SearchInfinitives(string prefix, int limit = 12)
    {
        using var cmd = Db.CreateCommand();
        cmd.CommandText = """
            SELECT id, infinitive, has_conjugation FROM verb
            WHERE infinitive LIKE $p || '%' ESCAPE '\' OR infinitive LIKE 'sich ' || $p || '%' ESCAPE '\'
            ORDER BY infinitive = $exact DESC, infinitive LIKE $p || '%' ESCAPE '\' DESC, has_conjugation DESC,
                     length(infinitive), infinitive
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$p", EscapeLike(prefix));
        cmd.Parameters.AddWithValue("$exact", prefix);
        cmd.Parameters.AddWithValue("$limit", limit);
        var rows = new List<(int Id, string Infinitive, bool HasConjugation)>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) rows.Add((r.GetInt32(0), r.GetString(1), r.GetBoolean(2)));
        return rows.Select(x => new VerbSummary(x.Infinitive, x.HasConjugation,
            x.HasConjugation ? Analyze(x.Id, x.Infinitive).IsIrregular : null)).ToList();
    }

    /// <summary>
    /// Reverse lookup: which verbs have this conjugated form ("ging" → gehen). A full scan. A form also
    /// matches the first word of a separable or reflexive form, so "rief" finds "rief an" (anrufen).
    /// </summary>
    public List<FormHit> FindForm(string value, int limit = 8)
    {
        using var cmd = Db.CreateCommand();
        cmd.CommandText = """
            SELECT v.infinitive, f.tense_id, f.person
            FROM form f JOIN verb v ON v.id = f.verb_id
            WHERE (f.value = $v OR f.value LIKE $p || ' %' ESCAPE '\') AND f.tense_id <> 1
            GROUP BY v.id, f.tense_id, f.person
            ORDER BY min(f.value = $v) DESC, min(f.variant), length(v.infinitive), v.infinitive, f.tense_id, f.person
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$v", value);
        cmd.Parameters.AddWithValue("$p", EscapeLike(value));
        cmd.Parameters.AddWithValue("$limit", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<FormHit>();
        while (r.Read())
            if (TenseId.Names.TryGetValue(r.GetInt32(1), out var name))
                list.Add(new(r.GetString(0), name, r.GetInt32(2)));
        return list;
    }

    public string? RandomInfinitive()
    {
        // Skip tables that are probably broken in the source (a weak verb where almost nothing matches the model).
        for (var attempt = 0; attempt < 20; attempt++)
        {
            int id;
            string infinitive;
            string? cls;
            using (var cmd = Db.CreateCommand())
            {
                cmd.CommandText = "SELECT id, infinitive, conjugation_class FROM verb WHERE has_conjugation = 1 ORDER BY random() LIMIT 1";
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                (id, infinitive, cls) = (r.GetInt32(0), r.GetString(1), Str(r, 2));
            }
            if (cls != "weak" || (Analyze(id, infinitive).IrregularForms ?? 0) < 25) return infinitive;
        }
        return null;
    }

    public VerbDetail? GetVerb(string infinitive)
    {
        int id;
        string? cls, auxiliary, etymology, url, lemma;
        bool hasConjugation;
        using (var cmd = Db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT v.id, v.infinitive, v.conjugation_class, v.auxiliary, v.has_conjugation, l.infinitive, v.etymology, v.url
                FROM verb v LEFT JOIN verb l ON l.id = v.lemma_id
                WHERE v.infinitive = $inf
                """;
            cmd.Parameters.AddWithValue("$inf", infinitive);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            id = r.GetInt32(0);
            infinitive = r.GetString(1);
            cls = Str(r, 2);
            auxiliary = Str(r, 3);
            hasConjugation = r.GetBoolean(4);
            lemma = Str(r, 5);
            etymology = Str(r, 6);
            url = Str(r, 7);
        }

        var reflexive = infinitive.StartsWith("sich ", StringComparison.Ordinal);
        var analysis = hasConjugation ? Analyze(id, infinitive) : null;
        var core = reflexive ? infinitive[5..] : infinitive;
        var baseVerb = reflexive ? core
            : analysis?.Particle is { } particle ? core[particle.Replace(" ", "").Length..]
            : null;
        var verb = new VerbDetail
        {
            Id = id,
            Infinitive = infinitive,
            ConjugationClass = cls,
            Reflexive = reflexive,
            Particle = analysis?.Particle,
            Auxiliary = auxiliary,
            HasConjugation = hasConjugation,
            IsIrregular = analysis?.IsIrregular,
            IrregularForms = analysis?.IrregularForms,
            BaseInfinitive = baseVerb is not null && baseVerb != infinitive && Exists(baseVerb) ? baseVerb : null,
            LemmaInfinitive = lemma,
            Etymology = etymology,
            Url = url,
            SubjunctiveIIRare = analysis?.SubjunctiveIIRare ?? false,
        };
        if (analysis is not null) verb.Forms.AddRange(analysis.Forms);

        ReadTexts("SELECT text FROM definition WHERE verb_id = $id ORDER BY seq", id, verb.Definitions);
        for (var i = 0; i < verb.Definitions.Count; i++) verb.Definitions[i] = WithoutClassLabels(verb.Definitions[i]);
        ReadTexts("SELECT ipa FROM pronunciation WHERE verb_id = $id ORDER BY seq", id, verb.Pronunciations);
        return verb;
    }

    /// <summary>Primary main-clause forms of haben, sein and werden, used to build the compound tenses.</summary>
    public IReadOnlyDictionary<(string Aux, int Tense, int Person), string> AuxiliaryForms()
    {
        if (_auxForms is not null) return _auxForms;
        _auxForms = [];
        using var cmd = Db.CreateCommand();
        cmd.CommandText = """
            SELECT v.infinitive, f.tense_id, f.person, f.value FROM form f JOIN verb v ON v.id = f.verb_id
            WHERE v.infinitive IN ('haben', 'sein', 'werden') AND f.variant = 0
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            _auxForms[(r.GetString(0), r.GetInt32(1), r.GetInt32(2))] = r.GetString(3);
        return _auxForms;
    }

    private sealed record Analysis(List<VerbForm> Forms, string? Particle, bool SubjunctiveIIRare, bool? IsIrregular, int? IrregularForms);

    /// <summary>A verb's forms in Konjugieren's terms, each compared with the weak paradigm (cached per verb).</summary>
    private Analysis Analyze(int id, string infinitive)
    {
        if (_analyses.TryGetValue(id, out var cached)) return cached;

        var rows = new List<(int Tense, int Person, string Value, string? Note, bool Subordinate)>();
        var subjunctiveIIRare = false;
        using (var cmd = Db.CreateCommand())
        {
            cmd.CommandText = "SELECT tense_id, person, value, note FROM form WHERE verb_id = $id ORDER BY tense_id, person, variant";
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var (tense, person, value, note) = (r.GetInt32(0), r.GetInt32(1), r.GetString(2), Str(r, 3));
                var labels = note?.Split(", ").ToList() ?? [];
                var subordinate = labels.Remove(SubordinateNote);
                // Wiktionary labels the Konjunktiv II of most verbs "formal, rare": in speech it is replaced by
                // würde + infinitive. The forms themselves are standard, so they are shown as main forms.
                if (tense == TenseId.SubjunctiveII && labels.Contains("formal") && labels.Contains("rare"))
                {
                    labels.Remove("formal");
                    labels.Remove("rare");
                    subjunctiveIIRare = true;
                }
                rows.Add((tense, person, value, labels.Count == 0 ? null : string.Join(", ", labels), subordinate));
            }
        }

        // The separable particle: what follows the verb in the main-clause present (ruft an, macht wieder gut),
        // when the infinitive starts with it.
        var core = infinitive.StartsWith("sich ", StringComparison.Ordinal) ? infinitive[5..] : infinitive;
        string? particle = null;
        foreach (var row in rows.Where(x => x.Tense == TenseId.Present && !x.Subordinate && x.Note is null))
        {
            var words = row.Value.Split(' ').Skip(1).Where(w => w is not ("mich" or "dich" or "sich" or "uns" or "euch")).ToList();
            var joined = string.Concat(words);
            if (words.Count > 0 && core.StartsWith(joined, StringComparison.Ordinal) && core.Length > joined.Length)
            {
                particle = string.Join(' ', words);
                break;
            }
        }

        var model = RegularConjugator.Generate(infinitive, particle);
        var forms = new List<VerbForm>();
        var irregularPrimary = 0;
        foreach (var cell in rows.GroupBy(x => (x.Tense, x.Person, x.Subordinate)))
        {
            var n = 0;
            foreach (var (tense, person, value, note, subordinate) in cell)
            {
                RegularConjugator.Key key = new(tense, person, subordinate);
                bool? irregular = model is not null && model.ContainsKey(key) ? !RegularConjugator.IsRegular(model, key, value) : null;
                // Count each slot once: the main-clause primary form.
                if (n == 0 && !subordinate && irregular == true && tense != TenseId.Infinitive) irregularPrimary++;
                forms.Add(new VerbForm(tense, person, value, note, subordinate, irregular));
                n++;
            }
        }

        return _analyses[id] = new Analysis(forms, particle, subjunctiveIIRare,
            model is null ? null : irregularPrimary > 0, model is null ? null : irregularPrimary);
    }

    /// <summary>
    /// "(class-7, intransitive, strong) to call" → "(intransitive) to call": the conjugation class is
    /// already a badge on the page.
    /// </summary>
    private static string WithoutClassLabels(string definition)
    {
        var end = definition.IndexOf(')');
        if (!definition.StartsWith('(') || end < 0) return definition;
        var labels = definition[1..end].Split(", ")
            .Where(l => l is not ("strong" or "weak" or "irregular") && !l.StartsWith("class-", StringComparison.Ordinal))
            .ToList();
        var rest = definition[(end + 1)..].TrimStart();
        return labels.Count == 0 ? rest : $"({string.Join(", ", labels)}) {rest}";
    }

    private bool Exists(string infinitive)
    {
        using var cmd = Db.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM verb WHERE infinitive = $w AND has_conjugation = 1";
        cmd.Parameters.AddWithValue("$w", infinitive);
        return cmd.ExecuteScalar() is not null;
    }

    private void ReadTexts(string sql, int verbId, List<string> target)
    {
        using var cmd = Db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", verbId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) target.Add(r.GetString(0));
    }

    private T? Scalar<T>(string sql)
    {
        using var cmd = Db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() is T t ? t : default;
    }

    private static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
