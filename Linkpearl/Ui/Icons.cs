using Dalamud.Interface;

namespace Linkpearl.Ui;

/// <summary>
/// Vocabulaire d'icônes du plugin.
/// </summary>
/// <remarks>
/// Aucun glyphe n'est écrit en dur dans le code : les points de code
/// FontAwesome vivent dans la zone à usage privé (U+F000 et au-delà),
/// invisibles dans un éditeur et silencieusement perdus au moindre accident
/// d'encodage.
///
/// Les nommer par leur sens plutôt que par leur apparence permet aussi de
/// changer le pictogramme d'un concept en un seul endroit. Et c'est
/// <see cref="All"/> qui décide de ce que l'atlas de polices charge : une
/// icône absente de cette liste s'afficherait en carré vide.
/// </remarks>
internal static class Icons
{
    // ─── Navigation ───────────────────────────────────────────────────────────
    public const FontAwesomeIcon Nearby   = FontAwesomeIcon.Users;
    public const FontAwesomeIcon Pairs    = FontAwesomeIcon.UserFriends;
    public const FontAwesomeIcon Requests = FontAwesomeIcon.Envelope;
    public const FontAwesomeIcon Settings = FontAwesomeIcon.Cog;

    // ─── État d'un pair ───────────────────────────────────────────────────────
    public const FontAwesomeIcon Connected  = FontAwesomeIcon.Link;
    public const FontAwesomeIcon Applied    = FontAwesomeIcon.CheckCircle;
    public const FontAwesomeIcon Receiving  = FontAwesomeIcon.CloudDownloadAlt;
    public const FontAwesomeIcon Waiting    = FontAwesomeIcon.Clock;
    public const FontAwesomeIcon Paused     = FontAwesomeIcon.Pause;
    public const FontAwesomeIcon Resume     = FontAwesomeIcon.Play;
    public const FontAwesomeIcon Blocked    = FontAwesomeIcon.Ban;
    public const FontAwesomeIcon Verified   = FontAwesomeIcon.ShieldAlt;
    public const FontAwesomeIcon Unverified = FontAwesomeIcon.QuestionCircle;

    // ─── Contexte en jeu ──────────────────────────────────────────────────────
    public const FontAwesomeIcon Character   = FontAwesomeIcon.User;
    public const FontAwesomeIcon World       = FontAwesomeIcon.Globe;
    public const FontAwesomeIcon Rendezvous  = FontAwesomeIcon.SatelliteDish;
    public const FontAwesomeIcon Cache       = FontAwesomeIcon.Database;
    public const FontAwesomeIcon Appearance  = FontAwesomeIcon.Tshirt;
    public const FontAwesomeIcon Discoverable = FontAwesomeIcon.Eye;
    public const FontAwesomeIcon Hidden      = FontAwesomeIcon.EyeSlash;

    // ─── Actions ──────────────────────────────────────────────────────────────
    public const FontAwesomeIcon Invite  = FontAwesomeIcon.UserPlus;
    public const FontAwesomeIcon Accept  = FontAwesomeIcon.Check;
    public const FontAwesomeIcon Decline = FontAwesomeIcon.Times;
    public const FontAwesomeIcon Remove  = FontAwesomeIcon.TrashAlt;
    public const FontAwesomeIcon Rename  = FontAwesomeIcon.PenAlt;
    public const FontAwesomeIcon Copy    = FontAwesomeIcon.Copy;
    public const FontAwesomeIcon Refresh = FontAwesomeIcon.SyncAlt;
    public const FontAwesomeIcon Close   = FontAwesomeIcon.Times;

    // ─── Messages ─────────────────────────────────────────────────────────────
    public const FontAwesomeIcon Warning = FontAwesomeIcon.ExclamationTriangle;
    public const FontAwesomeIcon Info    = FontAwesomeIcon.InfoCircle;
    public const FontAwesomeIcon Empty   = FontAwesomeIcon.Inbox;

    /// <summary>
    /// Toutes les icônes employées, pour que l'atlas ne charge que celles-là.
    /// </summary>
    /// <remarks>
    /// FontAwesome compte plusieurs milliers de glyphes. Les charger tous
    /// gonflerait l'atlas pour rien, et la mémoire de texture d'un plugin se
    /// paie dans le processus du jeu.
    /// </remarks>
    public static readonly FontAwesomeIcon[] All =
    [
        Nearby, Pairs, Requests, Settings,
        Connected, Applied, Receiving, Waiting, Paused, Blocked, Verified, Unverified,
        Character, World, Rendezvous, Cache, Appearance, Discoverable, Hidden,
        Invite, Accept, Decline, Remove, Rename, Copy, Refresh, Close,
        Warning, Info, Empty,
    ];
}

/// <summary>Le glyphe d'une icône, en une lettre.</summary>
/// <remarks>
/// Appelé partout où une icône est écrite au fil du texte, d'où le nom court :
/// <c>icon.ToIconString()</c> répété quarante fois noierait la ligne utile.
/// </remarks>
internal static class IconExtensions
{
    public static string S(this FontAwesomeIcon icon) => icon.ToIconString();
}
