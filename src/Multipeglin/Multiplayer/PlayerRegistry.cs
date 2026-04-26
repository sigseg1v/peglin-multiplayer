using System.Collections.Generic;
using System.Linq;

namespace Multipeglin.Multiplayer;

public class PlayerRegistry
{
    private readonly Dictionary<int, PlayerSlot> _slotsByPeerId = new Dictionary<int, PlayerSlot>();
    private readonly List<PlayerSlot> _allSlots = new List<PlayerSlot>();
    private int _nextSlotIndex;

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
        _nextSlotIndex = 1;
        return slot;
    }

    /// <summary>Register a newly connected client, returns the assigned slot.</summary>
    public PlayerSlot RegisterClient(int peerId, string playerName, string gameVersion, string modVersion)
    {
        var slot = new PlayerSlot
        {
            SlotIndex = _nextSlotIndex++,
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

    public PlayerSlot GetSlotByPeerId(int peerId)
    {
        if (peerId == -1)
            return GetHostSlot();
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
        _nextSlotIndex = 0;
    }
}
