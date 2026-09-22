using Dalamud.Configuration;

namespace Linkpearl;

/// <summary>Réglages du plugin, conservés par Dalamud.</summary>
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

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

    /// <summary>Répertoire du cache. Vide pour le défaut sous LOCALAPPDATA.</summary>
    public string CacheDirectory { get; set; } = "";

    public long CacheQuotaBytes { get; set; } = 20L * 1024 * 1024 * 1024;

    /// <summary>Plafond d'émission, en octets par seconde.</summary>
    public long UploadCeilingBytesPerSecond { get; set; } = 8 * 1024 * 1024;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
