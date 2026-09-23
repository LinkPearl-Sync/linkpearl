using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using System.Numerics;
using System.Reflection;
using System.Text.Unicode;

namespace Linkpearl.Ui;

/// <summary>
/// Polices du plugin : Inter embarqué, avec FontAwesome fusionné dans le corps
/// de texte pour que les icônes s'écrivent au fil du texte, sans bascule.
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
            // un glyphe présent dans le fichier mais absent d'ici n'est pas
            // chargé dans l'atlas et s'affiche en caractère de remplacement.
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

            // FontAwesome est fusionné dans les quatre niveaux : les pastilles
            // emploient Small et les en-têtes emploient H2, ils ont donc besoin
            // des icônes autant que le corps de texte.
            Body  = _atlas.NewDelegateFontHandle(tk => tk.OnPreBuild(
                p => Compose(p, "Fonts.Inter-Regular.ttf", 15f)));
            Small = _atlas.NewDelegateFontHandle(tk => tk.OnPreBuild(
                p => Compose(p, "Fonts.Inter-Regular.ttf", 12f)));
            H2    = _atlas.NewDelegateFontHandle(tk => tk.OnPreBuild(
                p => Compose(p, "Fonts.Inter-SemiBold.ttf", 17f)));
            Title = _atlas.NewDelegateFontHandle(tk => tk.OnPreBuild(
                p => Compose(p, "Fonts.Inter-SemiBold.ttf", 22f)));
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Chargement des polices impossible, repli sur celle de Dalamud.");

            Title = H2 = Body = Small = null;
            _atlas?.Dispose();
            _atlas = null;
        }
    }

    private static void Compose(IFontAtlasBuildToolkitPreBuild p, string resource, float sizePx)
    {
        var cfg = new SafeFontConfig { SizePx = sizePx, GlyphRanges = _textRanges };

        using var stream = Asm.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException($"ressource de police introuvable : {resource}");

        var font = p.AddFontFromStream(stream, in cfg, leaveOpen: false, resource);

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
        // vivent dans la zone d'usage privé et ne chevauchent donc ni Inter ni
        // FontAwesome.
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
