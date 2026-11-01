namespace CyberpunkModTranslator.Core;

/// <summary>
/// Base des lecteurs de fichiers en clair (<see cref="LuaScanner"/>, <see cref="RedsScanner"/>, <see cref="YamlScanner"/>,
/// <see cref="JsonScanner"/>). Chaque lecteur parcourt le texte a sa facon et appelle <see cref="Add"/> pour chaque chaine ;
/// la base garde la liste, retrouve les numeros de ligne et note les zones deja lues (chaines, commentaires).
/// Pour gerer un nouveau type de fichier : une classe derivee, et une ligne dans <see cref="PlainTextFile"/>.
/// </summary>
internal abstract class SourceScanner
{
    /// <summary>Le texte du fichier.</summary>
    protected readonly string S;

    protected readonly List<StringLiteral> Literals = new();

    /// <summary>Positions deja lues comme chaine ou commentaire : une parenthese dedans ne compte pas pour <see cref="CallAt"/>.</summary>
    protected readonly bool[] Skip;

    /// <summary>Debut de chaque ligne (index 0 = ligne 1), pour trouver une ligne sans recompter tout le fichier.</summary>
    private readonly List<int> _lineStarts = new() { 0 };

    protected SourceScanner(string source)
    {
        S = source;
        Skip = new bool[source.Length];
        for (var i = 0; i < source.Length; i++)
            if (source[i] == '\n') _lineStarts.Add(i + 1);
    }

    /// <summary>Lit le fichier et renvoie ses chaines, dans l'ordre.</summary>
    public abstract List<StringLiteral> Scan();

    /// <summary>Note une zone deja lue (chaine, commentaire).</summary>
    protected void MarkRead(int from, int to)
    {
        if (to > from) Array.Fill(Skip, true, from, to - from);
    }

    /// <summary>Appel de fonction qui entoure la position (nom complet et index de l'argument).</summary>
    protected (string? Name, int ArgIndex) CallAt(int pos) => CodeContext.CallAt(S, pos, Skip);

    /// <summary>Numero de ligne (a partir de 1) et texte de la ligne, coupe a 140 caracteres.</summary>
    protected (int Line, string Text) LineAt(int pos)
    {
        var idx = _lineStarts.BinarySearch(pos);
        if (idx < 0) idx = ~idx - 1;
        var start = _lineStarts[idx];
        var end = idx + 1 < _lineStarts.Count ? _lineStarts[idx + 1] - 1 : S.Length;
        var text = S[start..end].Trim();
        return (idx + 1, text.Length > 140 ? text[..137] + "..." : text);
    }

    /// <summary>Ajoute une chaine trouvee entre <paramref name="start"/> et <paramref name="end"/> (guillemets compris).</summary>
    protected StringLiteral Add(int start, int end, string value, StringKind kind, Func<string, string> encode,
        bool comparison = false, Func<string, string>? normalize = null)
    {
        var (line, text) = LineAt(start);
        var literal = new StringLiteral
        {
            Index = Literals.Count,
            Start = start,
            Length = end - start,
            Value = value,
            Line = line,
            LineText = text,
            Kind = kind,
            IsComparison = comparison,
            Encode = encode,
            Normalize = normalize ?? (v => v),
        };
        Literals.Add(literal);
        return literal;
    }
}
