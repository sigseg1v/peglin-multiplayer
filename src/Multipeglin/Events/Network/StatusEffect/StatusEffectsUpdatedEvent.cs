using System.Collections.Generic;

namespace Multipeglin.Events.Network.StatusEffect;

public class StatusEffectsUpdatedEvent
{
    public List<StatusEffectInfo> Effects { get; set; }

    public class StatusEffectInfo
    {
        public int EffectType { get; set; }
        public int Intensity { get; set; }
    }
}
