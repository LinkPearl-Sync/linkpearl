using System.Net;

namespace Linkpearl.Core.Transport;

/// <summary>Un lien établi avec un pair.</summary>
/// <remarks>
/// Abstrait pour que la machine à états de synchronisation se teste sans
/// réseau, et pour qu'un lien direct et un lien relayé soient interchangeables
/// sans que le reste du code sache lequel il utilise.
/// </remarks>
public interface IPeerLink : IAsyncDisposable
{
    /// <summary>Vrai tant que le lien porte.</summary>
    bool IsOpen { get; }

    /// <summary>
    /// Vrai quand le lien passe par le relais d'un rendez-vous.
    /// </summary>
    /// <remarks>
    /// Le reste du code n'a pas à le savoir. L'interface, si : un lien relayé
    /// cache l'adresse de chacun à l'autre, et c'est une information que le
    /// joueur doit pouvoir vérifier.
    /// </remarks>
    bool IsRelayed { get; }

    /// <summary>Temps d'aller-retour observé, en millisecondes.</summary>
    int RoundTripMs { get; }

    /// <summary>Perte observée, en pourcentage.</summary>
    float PacketLossPercent { get; }

    /// <summary>Octets en attente d'émission sur un canal.</summary>
    int PendingOn(byte channel);

    ValueTask SendAsync(byte channel, ReadOnlyMemory<byte> payload, CancellationToken ct);

    /// <summary>
    /// Une trame reçue.
    /// </summary>
    /// <remarks>
    /// Levé depuis le fil du transport, jamais depuis celui du jeu. Ce qui en
    /// découle doit être remis au thread du framework par l'appelant.
    /// </remarks>
    event Action<byte, byte[]>? Received;

    event Action<string>? Closed;

    /// <summary>L'adresse par laquelle on joint le pair, pour le journal.</summary>
    EndPoint? Remote { get; }
}
