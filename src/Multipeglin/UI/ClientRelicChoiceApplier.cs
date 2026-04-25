namespace Multipeglin.UI;

using UnityEngine;

/// <summary>
/// Disabled: post-battle relic choices are now generated independently on each
/// player's BattleUpgradeCanvas using their own RelicManager state. The host no
/// longer replicates its choices, so there is nothing for this poller to apply.
/// Kept as a no-op MonoBehaviour to preserve the AddComponent registration.
/// </summary>
public sealed class ClientRelicChoiceApplier : MonoBehaviour
{
}
