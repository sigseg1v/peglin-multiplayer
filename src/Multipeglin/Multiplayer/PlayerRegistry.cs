using System.Collections.Generic;
using System.Linq;

namespace Multipeglin.Multiplayer;

public class PlayerRegistry
{
    private readonly Dictionary<int, PlayerSlot> _slotsByPeerId = new Dictionary<int, PlayerSlot>();
    private readonly List<PlayerSlot> _allSlots = new List<PlayerSlot>();

    /// <summary>Register the host as slot 0.</summary>
    public PlayerSlot RegisterHost(string playerName, string gameVersion, string modVersion)
    {
        var slot = new PlayerSlot
        {
            SlotIndex = 0,
            PeerId = -1,
            PlayerName = playerName,
            IsHost = true,
            ChosenClass = 0,
            IsReady = true,
            GameVersion = gameVersion,
            ModVersion = modVersion,
        };
        _allSlots.Add(slot);
        return slot;
    }

    /// <summary>Register a newly connected client, returns the assigned slot.</summary>
    public PlayerSlot RegisterClient(int peerId, string playerName, string gameVersion, string modVersion)
    {
        var slot = new PlayerSlot
        {
            SlotIndex = AllocateFreeSlotIndex(),
            PeerId = peerId,
            PlayerName = playerName,
            IsHost = false,
            ChosenClass = 0,
            IsReady = false,
            GameVersion = gameVersion,
            ModVersion = modVersion,
        };
        _slotsByPeerId[peerId] = slot;
        _allSlots.Add(slot);
        return slot;
    }

    /// <summary>
    /// Register a client into a specific (Continue-restored) slot. STRICT: the
    /// caller must supply a non-negative slot that is not already occupied; if
    /// either check fails, returns null. Continue mode requires every player to
    /// land on their saved slot — silently re-allocating to a free index would
    /// scramble per-slot game state (decks, classes, relics) across players.
    /// <paramref name="chosenClass"/> is the saved class the player was using;
    /// it is locked at the slot level so the continue-mode lobby cannot
    /// accidentally diverge from the saved roster.
    /// </summary>
    public PlayerSlot RegisterClientWithSlot(int peerId, string playerName, int desiredSlotIndex, int chosenClass, string gameVersion, string modVersion)
    {
        if (desiredSlotIndex < 0)
        {
            return null;
        }

        var occupied = new HashSet<int>(_allSlots.Select(s => s.SlotIndex));
        if (occupied.Contains(desiredSlotIndex))
        {
            return null;
        }

        var slot = new PlayerSlot
        {
            SlotIndex = desiredSlotIndex,
            PeerId = peerId,
            PlayerName = playerName,
            IsHost = false,
            ChosenClass = chosenClass,
            IsReady = false,
            GameVersion = gameVersion,
            ModVersion = modVersion,
        };
        _slotsByPeerId[peerId] = slot;
        _allSlots.Add(slot);
        return slot;
    }

    // Smallest non-host index not currently occupied. Reusing freed slots keeps the
    // visible roster compact when a client disconnects + reconnects in the lobby —
    // otherwise the per-slot UI offsets push later joiners off-screen.
    private int AllocateFreeSlotIndex()
    {
        var occupied = new HashSet<int>(_allSlots.Select(s => s.SlotIndex));
        for (var i = 1; ; i++)
        {
            if (!occupied.Contains(i))
            {
                return i;
            }
        }
    }

    public PlayerSlot GetSlotByPeerId(int peerId)
    {
        if (peerId == -1)
        {
            return GetHostSlot();
        }

        return _slotsByPeerId.TryGetValue(peerId, out var slot) ? slot : null;
    }

    public PlayerSlot GetSlotByIndex(int index) => _allSlots.FirstOrDefault(s => s.SlotIndex == index);

    public PlayerSlot GetHostSlot() => _allSlots.FirstOrDefault(s => s.IsHost);

    /// <summary>
    /// The local player's slot. On host this is the host slot (slot 0).
    /// On client this is the first non-host slot (set during handshake).
    /// </summary>
    public PlayerSlot LocalSlot { get; set; }

    public IReadOnlyList<PlayerSlot> GetAllSlots() => _allSlots;

    public int SlotCount => _allSlots.Count;

    public bool AllClientsReady => _allSlots.Where(s => !s.IsHost).All(s => s.IsReady);

    public void RemoveByPeerId(int peerId)
    {
        if (_slotsByPeerId.TryGetValue(peerId, out var slot))
        {
            _slotsByPeerId.Remove(peerId);
            _allSlots.Remove(slot);
        }
    }

    public void Clear()
    {
        _slotsByPeerId.Clear();
        _allSlots.Clear();
    }
}
