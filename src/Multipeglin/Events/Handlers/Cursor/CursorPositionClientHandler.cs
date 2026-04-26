
using System;
using Multipeglin.Events.Network.Cursor;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;
using Multipeglin.Network;

namespace Multipeglin.Events.Handlers.Cursor;
public sealed class CursorPositionClientHandler : IClientHandler<CursorPositionEvent>
{
    public void Handle(CursorPositionEvent e)
    {
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null)
            {
                return;
            }

            services.TryResolve<IMultiplayerMode>(out var mode);
            services.TryResolve<PlayerRegistry>(out var registry);

            // Ignore echoes of our own broadcast.
            var localSlot = LocalSlotIndex(mode, registry);
            if (e.FromSlot == localSlot)
            {
                return;
            }

            // Render the incoming cursor locally.
            RemoteCursorRenderer.Instance?.SetRemoteCursor(e.FromSlot, e.WorldX, e.WorldY);

            // Host fans out peer cursors to the other clients so everyone sees
            // every cursor in 3+ player lobbies. The self-filter above prevents
            // the sending client from rendering its own echo.
            if (mode != null && mode.IsHosting
                && services.TryResolve<IMessageSender>(out var sender))
            {
                sender.Send(e);
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"CursorPosition handler failed: {ex.Message}");
        }
    }

    private static int LocalSlotIndex(IMultiplayerMode mode, PlayerRegistry registry)
    {
        if (mode != null && mode.IsHosting)
        {
            return 0;
        }

        return registry?.LocalSlot?.SlotIndex ?? -1;
    }
}
