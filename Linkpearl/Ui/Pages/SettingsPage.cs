using Dalamud.Bindings.ImGui;
using Linkpearl.Ui.Components;

namespace Linkpearl.Ui.Pages;

/// <summary>
/// Les réglages, et surtout ce qu'ils coûtent.
/// </summary>
/// <remarks>
/// Chaque réglage est suivi de sa conséquence, pas de sa description : ce qui
/// se paie en vie privée doit se lire avant d'être coché, pas après.
/// </remarks>
internal sealed class SettingsPage(Configuration configuration)
{
    public void Draw()
    {
        Text.Title("Réglages");
        ImGui.Dummy(Theme.S(0f, Theme.GapL));

        using (Card.Begin("settings_discoverable"))
        {
            Text.WithIcon(Icons.Discoverable, "Se signaler aux autres joueurs", Theme.Accent);
            ImGui.Dummy(Theme.S(0f, Theme.GapS));

            var discoverable = configuration.Discoverable;

            if (ImGui.Checkbox("Me signaler##discoverable", ref discoverable))
            {
                configuration.Discoverable = discoverable;
                configuration.Save();
            }

            ImGui.Dummy(Theme.S(0f, Theme.GapS));

            Text.Wrapped(
                "Sans cela, personne ne peut vous reconnaître ni vous adresser une demande. "
              + "Avec, l'opérateur du service de rendez-vous peut savoir que votre personnage est "
              + "en ligne : pour qu'un inconnu puisse vous reconnaître, il faut bien que quelque "
              + "chose soit calculable à partir de votre nom.");
        }

        using (Card.Begin("settings_rendezvous"))
        {
            Text.WithIcon(Icons.Rendezvous, "Service de rendez-vous", Theme.Accent);
            ImGui.Dummy(Theme.S(0f, Theme.GapS));

            var host = configuration.RendezvousHost;

            ImGui.SetNextItemWidth(Card.FullWidth);

            if (ImGui.InputText("##rendezvous", ref host, 128))
            {
                configuration.RendezvousHost = host;
                configuration.Save();
            }

            ImGui.Dummy(Theme.S(0f, Theme.GapS));

            Text.Wrapped(
                "Il aide deux joueurs à se trouver et relaie quand la connexion directe échoue. "
              + "Il ne voit ni vos fichiers, ni vos apparences, ni vos clés. Vous et vos pairs "
              + "devez régler le même.");
        }
    }
}
