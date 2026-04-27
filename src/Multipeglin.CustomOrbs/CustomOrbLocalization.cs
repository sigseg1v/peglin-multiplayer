using System.Linq;
using I2.Loc;

namespace Multipeglin.CustomOrbs;

/// <summary>
/// Adds localization terms for our custom orbs. Same string for every
/// language — these are easter-egg orbs and don't get translated.
/// </summary>
internal static class CustomOrbLocalization
{
    private const string D9000_NAME = "Big Boss D9000";
    private const string BEAST_NAME = "Beast Warb";

    private static readonly (string term, string value)[] Terms =
    {
        ("Orbs/bigbossd9000_name", D9000_NAME),
        ("Orbs/bigbossd9000_desc", "0/0 damage. 1/100 chance per bounce to deal 9000/9000 instead."),
        ("Orbs/bigbossd9000_desc_locked", D9000_NAME),
        ("Orbs/beastwarb_name", BEAST_NAME),
        ("Orbs/beastwarb_desc", "Deals normal damage to regular enemies, hugely amplified vs. (mini)bosses."),
        ("Orbs/beastwarb_desc_locked", BEAST_NAME),
    };

    private static bool _injected;

    public static void EnsureRegistered()
    {
        if (_injected)
        {
            return;
        }

        if (LocalizationManager.Sources == null || LocalizationManager.Sources.Count == 0)
        {
            LocalizationManager.UpdateSources();
        }

        var source = LocalizationManager.Sources?.FirstOrDefault();
        if (source == null)
        {
            return;
        }

        var langCount = source.mLanguages?.Count ?? 0;
        if (langCount == 0)
        {
            return;
        }

        foreach (var (term, value) in Terms)
        {
            var data = source.GetTermData(term) ?? source.AddTerm(term, eTermType.Text);
            if (data.Languages == null || data.Languages.Length != langCount)
            {
                data.Languages = new string[langCount];
                data.Flags = new byte[langCount];
            }

            for (var i = 0; i < langCount; i++)
            {
                data.Languages[i] = value;
            }
        }

        _injected = true;
        Plugin.Logger?.LogInfo($"[CustomOrbs] registered {Terms.Length} localization terms across {langCount} language(s)");
    }
}
