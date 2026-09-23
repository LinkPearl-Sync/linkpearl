using System.Text.Json;
using Linkpearl.Core.Manifest;

namespace Linkpearl.Core.Safety;

/// <summary>
/// Les extras d'un manifeste reçu : forme et taille, jamais le sens.
/// </summary>
/// <remarks>
/// Ces chaînes partent telles quelles dans d'autres plugins, qui les parsent
/// sans l'hypothèse qu'un pair les a écrites. On borne donc ce qui peut
/// l'être, et l'on vérifie que ce que l'émetteur devait nettoyer l'a bien été :
/// un client modifié pourrait sinon nous faire poser un nom, un ContentId ou un
/// VFX.
/// </remarks>
public static class ExtrasValidator
{
    public static bool TryAccept(CharacterExtras extras, Quotas quotas, out string? rejection)
    {
        rejection = Check(extras, quotas);
        return rejection is null;
    }

    private static string? Check(CharacterExtras extras, Quotas q)
    {
        if (extras.CustomizePlus is { } customize)
        {
            if (customize.Length > q.MaxCustomizePlusChars)
                return $"Customize+ : plafond dépassé (plafond {q.MaxCustomizePlusChars})";

            if (JsonShape.IsObject(customize, q.MaxExtrasJsonDepth) is false)
                return "Customize+ : JSON invalide ou trop profond";
        }

        if (extras.Heels is { } heels)
        {
            if (heels.Length > q.MaxHeelsChars)
                return $"SimpleHeels : plafond dépassé (plafond {q.MaxHeelsChars})";

            if (JsonShape.IsObject(heels, q.MaxExtrasJsonDepth) is false)
                return "SimpleHeels : JSON invalide ou trop profond";

            using var doc = JsonDocument.Parse(heels);

            if (HeelsSanitizer.Removed.Any(name => doc.RootElement.TryGetProperty(name, out _)))
                return "SimpleHeels : champs qui auraient dû être retirés à l'envoi";
        }

        if (extras.Honorific is { } honorific)
        {
            if (honorific.Length > q.MaxHonorificChars)
                return $"Honorific : plafond dépassé (plafond {q.MaxHonorificChars})";

            if (JsonShape.IsObject(honorific, q.MaxExtrasJsonDepth) is false)
                return "Honorific : JSON invalide ou trop profond";

            using var doc = JsonDocument.Parse(honorific);

            if (doc.RootElement.TryGetProperty("Title", out var title))
            {
                if (title.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    return "Honorific : titre non textuel";

                var text = title.GetString() ?? "";

                if (text.Length > q.MaxHonorificTitleLength || text.Any(char.IsControl))
                    return "Honorific : titre trop long ou avec un caractère de contrôle";
            }
        }

        if (extras.Moodles is { } moodles)
        {
            if (moodles.Length > q.MaxMoodlesChars)
                return $"Moodles : plafond dépassé (plafond {q.MaxMoodlesChars})";

            if (MoodlesSanitizer.IsSanitizedAndBounded(moodles) is false)
                return "Moodles : format invalide ou données non nettoyées";
        }

        if (extras.PetNicknames is { } pets)
        {
            if (pets.Length > q.MaxPetNicknamesChars)
                return $"PetNicknames : plafond dépassé (plafond {q.MaxPetNicknamesChars})";

            if (PetNicknamesData.IsNeutralAndBounded(pets) is false)
                return "PetNicknames : format invalide ou identité non neutralisée";
        }

        return null;
    }
}
