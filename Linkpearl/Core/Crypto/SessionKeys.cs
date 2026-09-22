namespace Linkpearl.Core.Crypto;

/// <summary>Ce qu'un handshake réussi laisse derrière lui.</summary>
/// <remarks>
/// <paramref name="PeerPublicKey"/> est l'identité <em>prouvée</em> du pair, et
/// non celle qu'il a annoncée : elle a été signée sur le transcript et liée au
/// secret partagé. C'est elle, et non ce que le rendez-vous a pu raconter, qui
/// doit servir à décider de ce qu'on accepte.
/// </remarks>
public sealed record SessionKeys(
    byte[] SendKey, byte[] ReceiveKey, byte[] SessionId, byte[] PeerPublicKey);
