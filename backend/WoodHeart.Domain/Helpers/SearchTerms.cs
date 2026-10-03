namespace WoodHeart.Domain.Helpers;

/// <summary>
/// Turns what a customer typed into the words worth looking for.
/// </summary>
/// <remarks>
/// <para>
/// Two jobs, both of which a plain <c>ILIKE '%term%'</c> cannot do.
/// </para>
/// <para>
/// <b>Words, not a phrase.</b> One substring match means "dining chair" only
/// finds a product whose text contains those two words adjacent and in that
/// order. Splitting into words and requiring all of them finds it whether the
/// product is called a "chair, dining" or a "dining room chair".
/// </para>
/// <para>
/// <b>Romanised Bangla.</b> Most customers here type Bangla in English
/// letters — <i>almari</i>, <i>khat</i>, <i>ayna</i> — and a catalogue written
/// in English and Bangla script contains neither spelling. That is not a
/// near miss that fuzzy matching rescues: <i>almari</i> and <i>wardrobe</i>
/// share no letters. It needs a vocabulary, and this is it.
/// </para>
/// <para>
/// The groups are deliberately data, not cleverness. A shop assistant can read
/// this list and say what is missing from it, which is not true of an
/// embedding.
/// </para>
/// </remarks>
public static class SearchTerms
{
    /// <summary>How many words of a long query are used. Beyond this is noise.</summary>
    private const int MaxTokens = 6;

    /// <summary>Single letters match nearly everything, so they are dropped.</summary>
    private const int MinTokenLength = 2;

    /// <summary>
    /// Words that mean the same thing to a customer, in English, in romanised
    /// Bangla, and in Bangla script. Membership is mutual: any word in a group
    /// finds a product described by any other.
    /// </summary>
    private static readonly string[][] Groups =
    [
        ["wardrobe", "almari", "almirah", "alna", "cupboard", "closet", "আলমারি"],
        ["bed", "khat", "bedstead", "খাট", "বেড"],
        ["sofa", "couch", "settee", "সোফা"],
        ["chair", "cheyar", "চেয়ার"],
        ["table", "tebil", "টেবিল"],
        ["dining", "khabar", "খাবার", "ডাইনিং"],
        ["mirror", "ayna", "আয়না", "আয়নাসহ"],
        ["dressing", "dresing", "ড্রেসিং"],
        ["showcase", "show case", "display cabinet", "শোকেস"],
        ["cabinet", "cabinat", "ক্যাবিনেট"],
        ["basin", "besin", "wash basin", "বেসিন"],
        ["light", "lamp", "bati", "বাতি", "লাইট"],
        ["pendant", "hanging light", "ঝাড়বাতি", "পেন্ডেন্ট"],
        ["centre", "center", "coffee", "সেন্টার"],
        ["side", "bedside", "bed side", "সাইড"],
        ["wagon", "trolley", "ওয়াগন"],
        ["planter", "tub", "pot", "টব"],
        ["art", "painting", "nakshi", "নকশি"],
        ["handicraft", "handicrafts", "craft", "হস্তশিল্প"],
        ["teak", "segun", "সেগুন"],
        ["mahogany", "mehogani", "মেহগনি"],
        ["rattan", "cane", "bet", "বেত"],
        ["wood", "wooden", "kath", "কাঠ", "কাঠের"],
        ["sheesham", "shisham", "শিশু কাঠ"]
    ];

    /// <summary>
    /// Words people add that no product is described by.
    /// </summary>
    /// <remarks>
    /// Every word of a query has to match, which is what makes "dining chair"
    /// precise — and what made "sofa set" find nothing, because no product text
    /// contains "set". Dropping the filler keeps the precision where it earns
    /// its keep and stops it rejecting a perfectly ordinary way to ask.
    /// </remarks>
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "set", "sets", "piece", "pieces", "item", "items", "product", "products",
        "the", "and", "for", "with", "from", "new", "best", "good", "cheap",
        "price", "prices", "buy", "online", "bd", "bangladesh", "dhaka",
        "my", "our", "your", "me", "we", "us", "in", "on", "at", "of", "to", "by",
        "room", "rooms", "want", "need", "looking", "show", "please", "size"
    };

    private static readonly Dictionary<string, string[]> Index = BuildIndex();

    /// <summary>
    /// The query as groups of alternatives: every group must match somewhere in
    /// a product's text, and any one word inside a group will do.
    /// </summary>
    /// <returns>Empty when there is nothing worth searching for.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> Expand(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return [];
        }

        var expanded = new List<IReadOnlyList<string>>();

        foreach (var token in Tokenize(search))
        {
            expanded.Add(Index.TryGetValue(token, out var group) ? group : [token]);
        }

        return expanded;
    }

    /// <summary>
    /// The query rewritten into the words the catalogue actually uses, for
    /// suggesting something when an exact search found nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Synonyms are applied at query time, not stored, so the text in the
    /// database says "wardrobe" and never "almari". Trigram similarity scores
    /// against what is stored — which is why <i>almri</i>, a typo of a synonym,
    /// matched nothing at all while <i>wardorbe</i>, a typo of a word in the
    /// catalogue, matched fine.
    /// </para>
    /// <para>
    /// So each word is pulled to the nearest one this shop knows, within an
    /// edit or two, and replaced by the first word of its group — the English
    /// one the catalogue is written in. Only suggestions use this. The search
    /// itself stays literal: silently answering a different question than the
    /// one that was typed is how a search loses a customer's trust.
    /// </para>
    /// </remarks>
    public static string Correct(string? search)
    {
        var tokens = Tokenize(search);

        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(' ', tokens.Select(Canonical));
    }

    private static string Canonical(string token)
    {
        if (Index.TryGetValue(token, out var exact))
        {
            return exact[0];
        }

        // Nothing under four letters is corrected at all. One edit is the whole
        // distance between "set" and "bet", and "bet" is cane furniture — which
        // is exactly what "sofa set" used to suggest. Four and five letters get
        // one edit, longer words two.
        if (token.Length <= 3)
        {
            return token;
        }

        var budget = token.Length <= 5 ? 1 : 2;
        var best = token;
        var bestDistance = budget + 1;

        foreach (var (word, group) in Index)
        {
            // Length alone rules most of the vocabulary out before the
            // expensive part, which keeps this linear scan cheap enough to run
            // on every miss.
            if (Math.Abs(word.Length - token.Length) > budget)
            {
                continue;
            }

            var distance = Distance(token, word, bestDistance);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = group[0];
            }
        }

        return best;
    }

    /// <summary>
    /// Levenshtein distance, abandoned once it cannot beat <paramref name="ceiling"/>.
    /// </summary>
    private static int Distance(string left, string right, int ceiling)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            var rowBest = current[0];

            for (var j = 1; j <= right.Length; j++)
            {
                var substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);

                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
                rowBest = Math.Min(rowBest, current[j]);
            }

            if (rowBest >= ceiling)
            {
                return ceiling;
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    /// <summary>
    /// The words of a query, lower-cased, punctuation dropped, capped.
    /// </summary>
    /// <remarks>
    /// Splitting on anything that is not a letter or a digit keeps Bangla
    /// intact — those code points are letters — while discarding the hyphens,
    /// commas and quotes people paste in from a listing.
    /// </remarks>
    public static IReadOnlyList<string> Tokenize(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return [];
        }

        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var character in search)
        {
            if (IsWordCharacter(character))
            {
                current.Append(char.ToLowerInvariant(character));
                continue;
            }

            Flush(tokens, current);

            if (tokens.Count == MaxTokens)
            {
                return tokens;
            }
        }

        Flush(tokens, current);

        return tokens.Count > MaxTokens ? tokens[..MaxTokens] : tokens;
    }

    /// <summary>
    /// Whether a character belongs to the word being read.
    /// </summary>
    /// <remarks>
    /// The combining marks matter, and nothing else here does. Bangla writes
    /// its vowels as marks attached to a consonant, and those marks are not
    /// letters to <see cref="char.IsLetterOrDigit"/> — so a plain letter test
    /// cuts আলমারি into আলম and রি, and a customer typing Bangla searches for
    /// half a word. It happened to still match here, because the half is a
    /// substring of the whole; it would stop matching the moment a word's mark
    /// fell anywhere but the end.
    /// </remarks>
    private static bool IsWordCharacter(char character) =>
        char.IsLetterOrDigit(character)
        || char.GetUnicodeCategory(character) is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.SpacingCombiningMark;

    private static void Flush(List<string> tokens, System.Text.StringBuilder current)
    {
        if (current.Length >= MinTokenLength)
        {
            var token = current.ToString();

            if (!Ignored.Contains(token))
            {
                tokens.Add(token);
            }
        }

        current.Clear();
    }

    private static Dictionary<string, string[]> BuildIndex()
    {
        var index = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in Groups)
        {
            foreach (var word in group)
            {
                // A word in two groups keeps the first. Silently merging them
                // would quietly widen both — "table" joining "dining" would
                // make every search for one return the other.
                index.TryAdd(word, group);
            }
        }

        return index;
    }
}
