namespace Multipeglin.Network;

public static class NetworkConfig
{
    public const int DefaultPort = 7777;
    public static string ConnectionKey => $"Multipeglin_{MultiplayerPluginInfo.VERSION}";
}
