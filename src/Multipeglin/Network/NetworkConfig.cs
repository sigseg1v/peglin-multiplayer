namespace Multipeglin.Network;

public static class NetworkConfig
{
    public const int DefaultPort = 7777;
    public const int MaxClients = 3;

    public static string ConnectionKey => $"Multipeglin_{MultiplayerPluginInfo.VERSION}";
}
