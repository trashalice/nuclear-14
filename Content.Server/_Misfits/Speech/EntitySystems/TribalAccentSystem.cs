using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._Misfits.Speech.Components;
using Content.Server.Speech;
using Content.Server.Speech.EntitySystems;
using Robust.Shared.Random;


namespace Content.Server._Misfits.Speech.EntitySystems;


public sealed class TribalAccentSystem : EntitySystem
{
    private static readonly Regex RegexNg = new(@"(?<=\w(i|e)n)g(?!\w)", RegexOptions.IgnoreCase);
    private static readonly Regex RegexTh = new(@"(?<=\wt)h", RegexOptions.IgnoreCase);
    private static readonly Regex RegexAnd = new(@"(?<=an)d\b", RegexOptions.IgnoreCase);
    private static readonly Regex RegexErLower = new(@"(?<=\w)[eE]r(?=(?i)[^aeiou])");
    private static readonly Regex RegexErUpper = new(@"(?<=\w)[eE]R(?=(?i)[^aeiou])");
    private static readonly Regex RegexNtContractions = new(@"(?<=\wn)(\'t|t)\b", RegexOptions.IgnoreCase);
    private static readonly Regex RegexTsLower = new(@"(?<=\w)([tT]\'|[tT])s\b");
    private static readonly Regex RegexTsUpper = new(@"(?<=\w)([tT]\'|[tT])S\b");
    private static readonly Regex RegexVeContractionsLower = new(@"(?<=\w)\'[vV]e\b");
    private static readonly Regex RegexVeContractionsUpper = new(@"(?<=\w)\'[vV]E\b");
    private static readonly Regex RegexToLower = new(@"(?<=\w)((?i)e(?-i)\W|\W)[tT]o\b");
    private static readonly Regex RegexToUpper = new(@"(?<=\w)((?i)e(?-i)\W|\W)[tT]O\b");
    private static readonly Regex RegexOfLower = new(@"(?<=\w)((?i)e(?-i)\W|\W)[oO]f\b");
    private static readonly Regex RegexOfUpper = new(@"(?<=\w)((?i)e(?-i)\W|\W)[oO]F\b");
    private static readonly Regex RegexIndefiniteArticle = new(@"(?i)(?<= )(a|an) ");
    private static readonly Regex RegexVowelShiftLower = new(@"([oO]u|u)(?=[nN])");
    private static readonly Regex RegexVowelShiftUpper = new(@"([oO]U|U)(?=[nN])");

    private static readonly Regex RegexFirstWord = new(@"^(\S+)");

    /// <summary>
    /// The number of tribal prefix utterances in tribal.ftl. Update this as necessary.
    /// </summary>
    private const int TribalPrefixes = 8;

    /// <summary>
    /// The chance for a prefix to be prepended to the statement.
    /// </summary>
    private const float PrefixChance = 0.05f;


    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ReplacementAccentSystem _replacement = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TribalAccentComponent, AccentGetEvent>(OnAccentGet);
    }

    public string Accentuate(string message, TribalAccentComponent component)
    {

        // direct word replacements first, always
        var msg = _replacement.ApplyReplacements(message, "tribal");

        // n't -> n
        msg = RegexNtContractions.Replace(msg, "");
        //X've -> Xa
        msg = RegexVeContractionsLower.Replace(msg, "a");
        msg = RegexVeContractionsUpper.Replace(msg, "A");
        //t's -> ss
        msg = RegexTsLower.Replace(msg, "ss");
        msg = RegexTsUpper.Replace(msg, "SS");

        // ing -> in, eng -> en
        msg = RegexNg.Replace(msg, "");

        // th -> t
        msg = RegexTh.Replace(msg, "");

        // Xer -> Xa
        msg = RegexErLower.Replace(msg, "a");
        msg = RegexErUpper.Replace(msg, "A");

        // remove indefinite articles
        msg = RegexIndefiniteArticle.Replace(msg, "");

        // Xand -> Xan
        msg = RegexAnd.Replace(msg, "");

        // vowel shift, on -> un, oun -> un
        msg = RegexVowelShiftLower.Replace(msg, "o");
        msg = RegexVowelShiftUpper.Replace(msg, "O");

        // X to -> Xta
        msg = RegexToLower.Replace(msg, "ta");
        msg = RegexToUpper.Replace(msg, "TA");

        // X of -> Xa
        msg = RegexOfLower.Replace(msg, "a");
        msg = RegexOfUpper.Replace(msg, "A");

        // random prefix
        if (_random.Prob(PrefixChance))
        {
            var firstWord = RegexFirstWord.Match(msg).Value;
            var firstWordAllCaps = (firstWord != "I" && !firstWord.Any(char.IsLower));
            var pick = _random.Next(1, TribalPrefixes);

            // Reverse sanitize capital
            var prefix = Loc.GetString($"accent-tribal-prefix-{pick}");
            if (firstWordAllCaps)
                prefix = prefix.ToUpper();
            msg = prefix + " " + msg;
        }

        return msg;
    }

    private void OnAccentGet(Entity<TribalAccentComponent> ent, ref AccentGetEvent args)
    {
        args.Message = Accentuate(args.Message, ent.Comp);
    }
}
