using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using System.Numerics;
using System.Reflection;
using System.Text.Unicode;

namespace Linkpearl.Ui;

/// <summary>
/// Polices du plugin : Fredoka pour les titres, Nunito pour le texte, les
/// polices du site. Inter est fusionné derrière chacune, et FontAwesome dans le
/// corps de texte pour que les icônes s'écrivent au fil du texte, sans bascule.
/// </summary>
/// <remarks>
/// L'atlas est isolé : une reconstruction ne touche ni celui de Dalamud ni
/// celui des autres plugins.
///
/// Toute la classe est tolérante à l'échec. Si un fichier de police est
/// corrompu ou si l'atlas n'est pas encore construit, les <c>Push</c> ne font
/// rien et l'interface reste lisible avec la police par défaut de Dalamud :
/// une fenêtre laide vaut mieux qu'une fenêtre vide.
/// </remarks>
internal static class Fonts
{
    private static readonly Assembly Asm = typeof(Fonts).Assembly;

    private static IFontAtlas? _atlas;
    private static ushort[]? _textRanges;
    private static ushort[]? _iconRanges;
    private static bool _loggedFailure;

    /// <summary>Titres de page.</summary>
    public static IFontHandle? Title { get; private set; }

    /// <summary>En-têtes de section et nom de pair.</summary>
    public static IFontHandle? H2 { get; private set; }

    /// <summary>Corps de texte, FontAwesome fusionné.</summary>
    public static IFontHandle? Body { get; private set; }

    /// <summary>Métadonnées, puces, barre d'état.</summary>
    public static IFontHandle? Small { get; private set; }

    public static bool Ready => Body is { Available: true };

    // ─── Construction ─────────────────────────────────────────────────────────

    public static void Build(IDalamudPluginInterface pi)
    {
        try
        {
            // Ces plages doivent couvrir tout ce que le fichier Inter contient :
            // il sert de secours derrière Fredoka et Nunito, et un glyphe
            // absent d'ici n'est pas chargé dans l'atlas, il s'affiche en
            // caractère de remplacement.
            _textRanges = new FluentGlyphRangeBuilder()
                .With(UnicodeRanges.BasicLatin)
                .With(UnicodeRanges.Latin1Supplement)
                .With(UnicodeRanges.LatinExtendedA)
                .With(UnicodeRanges.LatinExtendedB)
                .With(UnicodeRanges.CombiningDiacriticalMarks)
                .With(UnicodeRanges.GeneralPunctuation)
                .With(UnicodeRanges.CurrencySymbols)
                .With(UnicodeRanges.LetterlikeSymbols)
                .With(UnicodeRanges.NumberForms)
                .With(UnicodeRanges.Arrows)
                .With(UnicodeRanges.MathematicalOperators)
                .With(UnicodeRanges.MiscellaneousTechnical)
                .With(UnicodeRanges.BoxDrawing)
                .With(UnicodeRanges.GeometricShapes)
                .With(UnicodeRanges.MiscellaneousSymbols)
                .With(UnicodeRanges.Dingbats)
                .Build();

            _iconRanges = BuildIconRanges();

            _atlas = pi.UiBuilder.CreateFontAtlas(
                FontAtlasAutoRebuildMode.Async, isGlobalScaled: true, "Linkpearl");

            // Nunito a un œil plus petit qu'Inter : à taille égale, son texte
            // paraît d'un point plus petit, et Small devenait flou en jeu. Le
            // corps et les métadonnées prennent donc un pixel de plus.
            //
            // FontAwesome est fusionné dans les quatre niveaux : les pastilles
            // emploient Small et les en-têtes emploient H2, ils ont donc besoin
            // des icônes autant que le corps de texte.
            Body  = _atlas.NewDelegateFontHandle(tk => tk.OnPreBuild(
                p => Compose(p, "Fonts.Nunito-Regular.ttf", "Fonts.Inter-Regular.ttf", 16f)));
            Small = _atlas.NewDelegateFontHandle(tk => tk.OnPreBuild(
                p => Compose(p, "Fonts.Nunito-Regular.ttf", "Fonts.Inter-Regular.ttf", 13f)));
            H2    = _atlas.NewDelegateFontHandle(tk => tk.OnPreBuild(
                p => Compose(p, "Fonts.Fredoka-SemiBold.ttf", "Fonts.Inter-SemiBold.ttf", 17f)));
            Title = _atlas.NewDelegateFontHandle(tk => tk.OnPreBuild(
                p => Compose(p, "Fonts.Fredoka-SemiBold.ttf", "Fonts.Inter-SemiBold.ttf", 22f)));
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Chargement des polices impossible, repli sur celle de Dalamud.");

            Title = H2 = Body = Small = null;
            _atlas?.Dispose();
            _atlas = null;
        }
    }

    private static void Compose(IFontAtlasBuildToolkitPreBuild p, string resource, string fallback, float sizePx)
    {
        var cfg = new SafeFontConfig { SizePx = sizePx, GlyphRanges = _textRanges };

        using var stream = Asm.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException($"ressource de police introuvable : {resource}");

        var font = p.AddFontFromStream(stream, in cfg, leaveOpen: false, resource);

        // Inter derrière, sur les mêmes plages. ImGui garde le premier glyphe
        // venu : Fredoka et Nunito dessinent tout ce qu'elles ont, Inter ne
        // comble que leurs trous. Mesuré le 25 septembre : Nunito n'a ni →, ni
        // ◆, ni ◇, et Fredoka ne couvre que 10 caractères sur 128 du latin
        // étendu A, qu'un nom de groupe peut porter.
        using var fallbackStream = Asm.GetManifestResourceStream(fallback)
            ?? throw new FileNotFoundException($"ressource de police introuvable : {fallback}");

        var secours = new SafeFontConfig { SizePx = sizePx, GlyphRanges = _textRanges, MergeFont = font };
        p.AddFontFromStream(fallbackStream, in secours, leaveOpen: false, fallback);

        // Accents et alphabets supplémentaires selon la langue réglée dans Dalamud.
        var extra = new SafeFontConfig { SizePx = sizePx, MergeFont = font };
        p.AttachExtraGlyphsForDalamudLanguage(in extra);

        var icons = new SafeFontConfig
        {
            SizePx      = sizePx * 0.86f,
            MergeFont   = font,
            GlyphRanges = _iconRanges,
            GlyphOffset = new Vector2(0f, Theme.S(1f)),
        };

        p.AddFontAwesomeIconFont(in icons);

        // Les symboles du jeu, dont le HQ qui sert de marque au plugin. Ils
        // vivent dans la zone d'usage privé et ne chevauchent donc ni les polices de
        // texte ni FontAwesome.
        var symbols = new SafeFontConfig { SizePx = sizePx, MergeFont = font };
        p.AddGameSymbol(in symbols);
        p.SetFontScaleMode(font, FontScaleMode.Default);
    }

    private static ushort[] BuildIconRanges()
    {
        var builder = new FluentGlyphRangeBuilder();

        foreach (var icon in Icons.All)
            builder = builder.With((uint)icon);

        return builder.BuildExact();
    }

    // ─── Utilisation ──────────────────────────────────────────────────────────

    public static IDisposable PushTitle() => Use(Title);

    public static IDisposable PushH2() => Use(H2);

    public static IDisposable PushBody() => Use(Body);

    public static IDisposable PushSmall() => Use(Small);

    private static IDisposable Use(IFontHandle? handle)
    {
        if (handle is not { Available: true })
        {
            LogFailureOnce(handle);
            return NullScope.Instance;
        }

        return handle.Push();
    }

    private static void LogFailureOnce(IFontHandle? handle)
    {
        if (_loggedFailure || handle?.LoadException is not { } e)
            return;

        _loggedFailure = true;
        Plugin.Log.Warning(e, "Une police du plugin n'a pas pu être construite.");
    }

    public static void Dispose()
    {
        Title = H2 = Body = Small = null;
        _atlas?.Dispose();
        _atlas = null;
    }

    /// <summary>Portée vide, rendue quand la police n'est pas disponible.</summary>
    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
