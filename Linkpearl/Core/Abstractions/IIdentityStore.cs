namespace Linkpearl.Core.Abstractions;

/// <summary>
/// Rangement de la clé privée d'identité.
/// </summary>
/// <remarks>
/// Abstrait parce que la protection réelle est propre au système : DPAPI sous
/// Windows, rien d'équivalent sous Linux où tournent les tests. Le noyau ne doit
/// pas dépendre de l'un ni de l'autre.
/// </remarks>
public interface IIdentityStore
{
    byte[]? Load();

    void Save(byte[] blob);
}
