namespace Multipeglin.Events.Handlers.Cursor;

using Multipeglin.Events.Network.Cursor;

/// <summary>
/// CursorPosition events are point-to-point via IMessageSender.Send (never
/// dispatched through the server broadcast path), so this handler is a no-op.
/// Kept to satisfy the registry's paired-handler contract.
/// </summary>
public sealed class CursorPositionServerHandler : IServerHandler<CursorPositionEvent>
{
    public CursorPositionEvent Handle(CursorPositionEvent networkEvent) => null;
}
