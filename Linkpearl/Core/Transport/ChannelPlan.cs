namespace Linkpearl.Core.Transport;

/// <summary>
/// Répartit les blocs sur les canaux de données.
/// </summary>
/// <remarks>
/// La fenêtre fiable de LiteNetLib est une constante de soixante-quatre
/// paquets, soit environ quatre-vingt-onze kibioctets en vol par canal. Mesuré :
/// un canal unique plafonne à 0,41 Mo/s à soixante millisecondes de latence,
/// huit canaux à 3,19, vingt-quatre à 7,82. Le multi-canal n'est donc pas une
/// optimisation, c'est la condition d'existence du transfert.
///
/// Le canal zéro est réservé au plan de contrôle : un message de présence ou
/// une annulation ne doit jamais attendre derrière des mégaoctets de texture.
/// </remarks>
public sealed class ChannelPlan
{
    public const byte ControlChannel = 0;

    private readonly long[] _pending;

    public ChannelPlan(int dataChannels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dataChannels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dataChannels, 63);

        _pending = new long[dataChannels];
    }

    public int DataChannels => _pending.Length;

    /// <summary>Choisit le canal le moins chargé et lui impute le bloc.</summary>
    public byte Next(int blockSize)
    {
        var chosen = 0;

        for (var i = 1; i < _pending.Length; i++)
        {
            if (_pending[i] < _pending[chosen])
                chosen = i;
        }

        _pending[chosen] += blockSize;
        return (byte)(chosen + 1);   // le canal 0 reste au contrôle
    }

    /// <summary>Signale qu'un bloc a quitté la file d'un canal.</summary>
    public void Completed(byte channel, int blockSize)
    {
        var index = channel - 1;

        if (index < 0 || index >= _pending.Length)
            return;

        _pending[index] = Math.Max(0, _pending[index] - blockSize);
    }

    public long PendingOn(byte channel)
    {
        var index = channel - 1;
        return index >= 0 && index < _pending.Length ? _pending[index] : 0;
    }
}
