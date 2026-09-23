using Dalamud.Configuration;
using Linkpearl.Core.Sync;

namespace Linkpearl;

/// <summary>Réglages du plugin, conservés par Dalamud.</summary>
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    /// <summary>
    /// Hôte du service de rendez-vous.
    /// </summary>
    /// <remarks>
    /// Pré-rempli, parce qu'un ticket de douze caractères ne peut pas porter le
    /// serveur : les deux personnes doivent donc avoir réglé le même. Un défaut
    /// évite d'avoir à le dire à chaque nouvel arrivant.
    ///
    /// Réglable, et c'est la raison d'être du projet : si ce service tombe ou
    /// reçoit une lettre d'avocat, on en change sans rien reconstruire.
    /// </remarks>
    public string RendezvousHost { get; set; } = "83.228.242.221";

    public int RendezvousPort { get; set; } = 47900;

    /// <summary>
    /// Les services de rendez-vous, par ordre de préférence.
    /// </summary>
    /// <remarks>
    /// Une liste et non un service unique : c'est ce qui permet de voir des
    /// joueurs qui n'ont pas fait le même choix que vous. Chaque entrée activée
    /// apprend que votre personnage est en ligne et qui se tient autour de vous,
    /// donc la liste reste courte par défaut.
    /// </remarks>
    public List<RendezvousEntry> Rendezvous { get; set; } = [];

    /// <summary>Les services activés, ceux que le moteur emploiera.</summary>
    public IReadOnlyList<RendezvousEntry> ActiveRendezvous =>
        Rendezvous.Where(entry => entry.Enabled).ToList();

    /// <summary>
    /// Construit la liste depuis l'ancien réglage, la première fois.
    /// </summary>
    /// <remarks>
    /// <c>RendezvousHost</c> et <c>RendezvousPort</c> restent lus pour cela, et
    /// pour cela seulement : ils ne sont plus la source de vérité.
    /// </remarks>
    public void MigrateIfNeeded()
    {
        var migrated = RendezvousList.Migrate(RendezvousHost, RendezvousPort, Rendezvous);

        if (ReferenceEquals(migrated, Rendezvous))
            return;

        Rendezvous = [.. migrated];
        Version = 2;
        Save();
    }

    /// <summary>
    /// Se signaler aux autres joueurs.
    /// </summary>
    /// <remarks>
    /// Activé par défaut : une fonction désactivée par défaut n'existe pas, et
    /// sans elle personne ne se trouve. Le prix est que l'opérateur du
    /// rendez-vous peut savoir quels personnages sont en ligne, parce qu'une
    /// adresse de boîte dérive du nom. Être découvrable par un inconnu implique
    /// de l'être par le serveur.
    /// </remarks>
    public bool Discoverable { get; set; } = true;

    /// <summary>Répertoire du cache. Vide pour le défaut sous LOCALAPPDATA.</summary>
    public string CacheDirectory { get; set; } = "";

    public long CacheQuotaBytes { get; set; } = 20L * 1024 * 1024 * 1024;

    /// <summary>
    /// Brider l'envoi pour préserver le ping.
    /// </summary>
    /// <remarks>
    /// Désactivé par défaut : les joueurs visés font du jeu de rôle, pas du
    /// donjon, et quelques millisecondes de ping leur coûtent moins qu'une tenue
    /// qui met des minutes à arriver.
    /// </remarks>
    public bool LimitUpload { get; set; }

    /// <summary>Plafond d'émission, en octets par seconde.</summary>
    public long UploadCeilingBytesPerSecond { get; set; } = 8 * 1024 * 1024;

    /// <summary>Le rappel de sauvegarder a été fait, ou une sauvegarde faite.</summary>
    /// <remarks>
    /// Une seule fois : un rappel qui revient à chaque connexion s'apprend à
    /// ignorer, et ne sert alors plus à rien.
    /// </remarks>
    public bool BackupReminded { get; set; }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
