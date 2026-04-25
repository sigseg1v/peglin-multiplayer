namespace Multipeglin.Events.Network.Battle;

/// <summary>
/// Host→client signal that the Act 4 boss (Spirit Of Radia) is starting one of
/// its two phase-2 visual transitions. The client mirrors the host visuals by
/// invoking the corresponding static delegate on SpiritOfRadiaBoss, which
/// triggers Act4BossPegBoardFrameManager and SpiritOfRadiaPhaseTransitionController
/// coroutines (they're already subscribed on the client via BattleController.Start).
///
/// Step values:
///   1 = PreTransitionStarted (cracks walls, hides floor + crystal walls, fades, moves boss)
///   2 = OnSpiritOfRadiaPhaseTransitionStarted (clears roof/floor, shows void walls, moves UI)
/// </summary>
public class SpiritOfRadiaPhaseTransitionEvent
{
    public int Step;
}
