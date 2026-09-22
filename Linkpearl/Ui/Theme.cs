using Dalamud.Interface.Utility;
using System.Numerics;

namespace Linkpearl.Ui;

/// <summary>
/// Jetons de design du plugin.
/// </summary>
/// <remarks>
/// La palette est celle de l'objet : une perle sur de l'ardoise. Fonds neutres
/// froids, accent nacré. Rien de vif, parce que rien ici n'est une alerte : le
/// plugin montre des gens, pas des notifications.
///
/// Toute dimension en pixels passe par <see cref="S(float)"/>. Sans cela
/// l'interface devient illisible dès que l'utilisateur monte l'échelle Dalamud,
/// et c'est le genre de défaut qu'on ne voit jamais soi-même.
/// </remarks>
internal static class Theme
{
    // ─── Conversion ───────────────────────────────────────────────────────────

    /// <summary>Convertit un RGB hexadécimal (0xRRGGBB) en couleur ImGui.</summary>
    public static Vector4 Hex(uint rgb, float a = 1f) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >>  8) & 0xFF) / 255f,
        ( rgb        & 0xFF) / 255f,
        a);

    // ─── Fonds ────────────────────────────────────────────────────────────────
    //
    // Échelle de profondeur, du plus enfoncé au plus surélevé. La règle qui rend
    // une interface sombre lisible : chaque niveau doit être distinct du
    // précédent, sinon les cartes disparaissent dans le fond et tout paraît plat.
    //
    //   BgSunken  <  BgBase  <  BgSidebar  <  BgSurface  <  BgRaised  <  BgHover

    public static readonly Vector4 BgSunken  = Hex(0x1A1C22); // champs de saisie
    public static readonly Vector4 BgBase    = Hex(0x1F222A); // fond de fenêtre
    public static readonly Vector4 BgSidebar = Hex(0x24272F); // barre latérale, barre d'état
    public static readonly Vector4 BgSurface = Hex(0x2A2E38); // cartes, panneaux
    public static readonly Vector4 BgRaised  = Hex(0x343945); // carte survolée, boutons neutres
    public static readonly Vector4 BgHover   = Hex(0x404654); // survol d'un élément surélevé

    /// <summary>Ombre portée sous les surfaces surélevées.</summary>
    public static readonly Vector4 Shadow = Hex(0x000000, 0.50f);

    // ─── Accents ──────────────────────────────────────────────────────────────
    //
    // Nacre : un bleu très désaturé qui tire sur le lavande. Il se détache du
    // gris sans crier, et se distingue de l'or de l'interface native du jeu,
    // avec laquelle il ne faut pas que l'on confonde la nôtre.

    public static readonly Vector4 Accent       = Hex(0xA8C0E8);
    public static readonly Vector4 AccentHover  = Hex(0xC8D8F5);
    public static readonly Vector4 AccentActive = Hex(0x50658C);

    /// <summary>Version assourdie, pour les fonds et les voiles.</summary>
    public static readonly Vector4 AccentMuted = Hex(0x3E4E6B);

    // ─── Texte ────────────────────────────────────────────────────────────────

    public static readonly Vector4 Text      = Hex(0xE9EAEF);
    public static readonly Vector4 TextMuted = Hex(0xAFB4C0);
    public static readonly Vector4 TextFaint = Hex(0x7E8492);
    public static readonly Vector4 Link      = Hex(0xA8C0E8);

    /// <summary>Texte posé sur une surface claire, l'accent nacré par exemple.</summary>
    public static readonly Vector4 TextOnLight = Hex(0x14161C);

    // ─── Statuts ──────────────────────────────────────────────────────────────

    /// <summary>Pair joint et apparence posée.</summary>
    public static readonly Vector4 Online = Hex(0x5BCF9A);

    /// <summary>Transfert en cours, ou pair en pause.</summary>
    public static readonly Vector4 Idle = Hex(0xE8C46A);

    public static readonly Vector4 Danger      = Hex(0xE8666B);
    public static readonly Vector4 DangerHover = Hex(0xF48287);

    // ─── Bordures ─────────────────────────────────────────────────────────────

    public static readonly Vector4 Border      = Hex(0x3C414E);
    public static readonly Vector4 BorderSoft  = Hex(0x2F3440);
    public static readonly Vector4 BorderLight = Hex(0x505869);

    /// <summary>
    /// Liseré clair posé sur l'arête haute d'une surface. C'est ce qui donne
    /// l'impression que la carte capte la lumière et se détache du fond.
    /// </summary>
    public static readonly Vector4 Highlight = Hex(0xFFFFFF, 0.055f);

    // ─── Métriques (en pixels non scalés : toujours passer par S()) ───────────

    public const float RadiusWindow = 10f;
    public const float RadiusCard   =  8f;
    public const float RadiusFrame  =  6f;

    public const float SidebarWidth    = 168f;
    public const float SidebarItem     = 40f;
    public const float TitleBarHeight  = 40f;
    public const float StatusBarHeight = 26f;

    public const float PadWindowX = 16f;
    public const float PadWindowY = 14f;
    public const float CardPadX   = 14f;
    public const float CardPadY   = 11f;

    public const float GapXs =  3f;
    public const float GapS  =  5f;
    public const float GapM  =  8f;
    public const float GapL  = 12f;
    public const float GapXl = 22f;

    // ─── Échelle ──────────────────────────────────────────────────────────────

    /// <summary>Met une dimension à l'échelle de l'interface Dalamud.</summary>
    public static float S(float px) => px * ImGuiHelpers.GlobalScale;

    /// <summary>Met un couple de dimensions à l'échelle de l'interface Dalamud.</summary>
    public static Vector2 S(float x, float y) =>
        new(x * ImGuiHelpers.GlobalScale, y * ImGuiHelpers.GlobalScale);

    // ─── Utilitaires couleur ──────────────────────────────────────────────────

    public static Vector4 Alpha(Vector4 c, float a) => c with { W = a };

    public static Vector4 Mix(Vector4 a, Vector4 b, float t) => Vector4.Lerp(a, b, t);

    /// <summary>
    /// Luminance relative perçue (coefficients ITU-R BT.709). Le vert pèse dix
    /// fois plus que le bleu dans la perception, d'où l'écart des poids.
    /// </summary>
    public static float Luminance(Vector4 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;

    /// <summary>
    /// Couleur de texte lisible sur le fond donné. L'accent étant clair, du
    /// texte blanc dessus serait illisible.
    /// </summary>
    public static Vector4 TextOn(Vector4 background) =>
        Luminance(background) > 0.55f ? TextOnLight : Text;

    /// <summary>
    /// Couleur stable dérivée d'un nom, pour les pastilles d'initiales.
    /// </summary>
    /// <remarks>
    /// Hash FNV-1a sur la teinte, saturation et valeur fixées pour rester
    /// lisible sur fond sombre. Stable d'une session à l'autre : un pair garde
    /// sa couleur, ce qui aide à le reconnaître avant même d'avoir lu son nom.
    /// </remarks>
    public static Vector4 FromName(string name)
    {
        var hash = 2166136261u;

        foreach (var ch in name)
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return FromHsv(hash % 360u / 360f, 0.32f, 0.62f);
    }

    /// <summary>Couleur à partir d'une teinte, d'une saturation et d'une valeur, chacune dans [0, 1].</summary>
    public static Vector4 FromHsv(float h, float s, float v)
    {
        var i = (int)MathF.Floor(h * 6f);
        var f = h * 6f - i;
        var p = v * (1f - s);
        var q = v * (1f - f * s);
        var t = v * (1f - (1f - f) * s);

        return (i % 6) switch
        {
            0 => new Vector4(v, t, p, 1f),
            1 => new Vector4(q, v, p, 1f),
            2 => new Vector4(p, v, t, 1f),
            3 => new Vector4(p, q, v, 1f),
            4 => new Vector4(t, p, v, 1f),
            _ => new Vector4(v, p, q, 1f),
        };
    }
}
