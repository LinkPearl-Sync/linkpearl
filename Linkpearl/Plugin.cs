using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Linkpearl.Integration;

namespace Linkpearl;

/// <summary>
/// Point d'entrée du plugin.
/// </summary>
/// <remarks>
/// Le plugin n'est qu'une coquille autour de <c>Core</c> : il fournit les
/// adaptateurs Dalamud et l'interface, et ne porte aucune logique. Tout ce qui
/// décide se teste sous Linux, sans le jeu.
///
/// À ce stade (jalon 1) il n'y a ni réseau, ni pair, ni interface : seulement la
/// boucle locale qui lève les inconnues côté jeu.
/// </remarks>
public sealed class Plugin : IDalamudPlugin
{
    /// <summary>
    /// Le jeu intercepte « /linkpearl » avant Dalamud : c'est une commande de
    /// chat native. Toute commande choisie ici doit être vérifiée en jeu.
    /// </summary>
    private const string Command = "/lpearl";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager         Commands        { get; private set; } = null!;
    [PluginService] internal static IFramework              Framework       { get; private set; } = null!;
    [PluginService] internal static IChatGui                Chat            { get; private set; } = null!;
    [PluginService] internal static IPluginLog              Log             { get; private set; } = null!;

    private readonly SelfLoop _selfLoop;
    private readonly CancellationTokenSource _shutdown = new();

    public Plugin()
    {
        var penumbra  = new PenumbraIpc(PluginInterface);
        var glamourer = new GlamourerIpc(PluginInterface);
        _selfLoop = new SelfLoop(penumbra, glamourer, Framework, Log);

        Commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "capture | apply | revert",
        });

        Report($"chargé. Penumbra : {Describe(penumbra.TryGetVersion())}, Glamourer : {Describe(glamourer.TryGetVersion())}.");

        // Rattrapage d'une session précédente : un rechargement ou un plantage a
        // pu laisser une collection affectée au personnage, que plus rien en
        // mémoire ne sait retirer.
        if (_selfLoop.HasLeftovers())
        {
            Framework.RunOnFrameworkThread(_selfLoop.Revert);
            Report("une collection d'une session précédente a été retirée.");
        }
    }

    private static string Describe((int Major, int Minor)? version)
        => version is { } v ? $"{v.Major}.{v.Minor}" : "absent";

    private void OnCommand(string _, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "capture":   RunSafely(CaptureAsync);                      break;
            case "apply":     RunSafely(ApplyAsync);                        break;
            case "revert":    _selfLoop.Revert(); Report("personnage rendu à son état normal."); break;
            default:
                Report("capture : relève l'apparence. apply : la réapplique depuis le cache. revert : nettoie.");
                break;
        }
    }

    private async Task CaptureAsync()
    {
        var report = await _selfLoop.CaptureAsync(_shutdown.Token).ConfigureAwait(false);

        Report($"capture : {report.FileCount} fichiers, {Megabytes(report.TotalBytes)}, "
             + $"{report.GamePathCount} chemins de jeu.");
        Report($"manifeste : {report.ManifestBytes / 1024} Ko bruts, "
             + $"{report.CompressedManifestBytes / 1024} Ko compressés.");
        Report($"durées : hachage {report.HashMilliseconds} ms, copie vers le cache {report.CopyMilliseconds} ms.");
        Report($"échanges de fichier non synchronisés en v1 : {report.SwapCount}. "
             + $"Ressources écartées : {report.SkippedCount}.");

        foreach (var skipped in report.Skipped.Take(5))
            Log.Information($"écarté : {skipped.GamePath} ({skipped.Reason})");
    }

    private async Task ApplyAsync()
    {
        var applied = await _selfLoop.ApplyAsync(_shutdown.Token).ConfigureAwait(false);
        Report($"appliqué {applied} chemins de jeu depuis le cache.");
    }

    /// <summary>
    /// Exécute une tâche déclenchée par une commande, sans jamais laisser une
    /// exception se perdre dans un Task oublié.
    /// </summary>
    private void RunSafely(Func<Task> work)
        => _ = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Commande Linkpearl en échec.");
                Report($"échec : {e.Message}");
            }
        }, _shutdown.Token);

    private static string Megabytes(long bytes) => $"{bytes / 1024.0 / 1024.0:F1} Mo";

    /// <summary>
    /// Écrit dans le journal local du joueur, jamais dans le chat du jeu : rien
    /// de ce que fait ce plugin ne doit partir vers le serveur.
    /// </summary>
    private static void Report(string message) => Chat.Print($"[Linkpearl] {message}");

    public void Dispose()
    {
        // Le déchargement de Dalamud est coopératif : une tâche encore vivante
        // ici ferait fuir l'AssemblyLoadContext, et le rechargement suivant en
        // créerait un second.
        _shutdown.Cancel();
        Commands.RemoveHandler(Command);
        _selfLoop.Dispose();
        _shutdown.Dispose();
    }
}
