using Relics;
using UnityEngine;

namespace Multipeglin.Utility;

/// <summary>
/// Cached lookups for the game's ScriptableObject "singletons".
///
/// <c>Resources.FindObjectsOfTypeAll&lt;T&gt;()</c> is the only way to reach these
/// (they are assets wired via [SerializeField], so FindObjectOfType returns
/// null), but it walks *every loaded object in memory* — every asset the
/// Addressables catalog has paged in, not just the scene — and allocates a
/// fresh array each call. The client's 2 s state heartbeat was calling it half
/// a dozen times per apply.
///
/// These assets live for the process lifetime, so the result is cached and only
/// re-resolved when the cached reference goes (Unity) null.
/// </summary>
public static class ScriptableSingletons
{
    private static RelicManager _relicManager;
    private static DeckManager _deckManager;
    private static Cruciball.CruciballManager _cruciballManager;
    private static Relic[] _relicAssets;

    public static RelicManager Relics => Resolve(ref _relicManager);

    public static DeckManager Deck => Resolve(ref _deckManager);

    public static Cruciball.CruciballManager Cruciball => Resolve(ref _cruciballManager);

    /// <summary>
    /// Every loaded <see cref="Relic"/> asset. Relics are static content, so the
    /// array is resolved once. Callers must treat it as read-only.
    /// </summary>
    public static Relic[] RelicAssets
    {
        get
        {
            if (_relicAssets == null || _relicAssets.Length == 0)
            {
                _relicAssets = Resources.FindObjectsOfTypeAll<Relic>();
            }

            return _relicAssets;
        }
    }

    /// <summary>Drop every cached reference. Call on scene teardown if assets get unloaded.</summary>
    public static void Invalidate()
    {
        _relicManager = null;
        _deckManager = null;
        _cruciballManager = null;
        _relicAssets = null;
    }

    private static T Resolve<T>(ref T slot)
        where T : Object
    {
        // Unity fake-null: an unloaded asset compares equal to null.
        if (slot != null)
        {
            return slot;
        }

        var found = Resources.FindObjectsOfTypeAll<T>();
        slot = found.Length > 0 ? found[0] : null;
        return slot;
    }
}
