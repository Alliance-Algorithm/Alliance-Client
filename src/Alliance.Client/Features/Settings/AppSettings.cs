namespace Alliance.Client.Features.Settings;

public sealed class AppSettings
{
    public string ApplicationName { get; set; } = "Alliance Client";

    public bool EnableDebugMode { get; set; } = true;

    public MqttSettings Mqtt { get; set; } = new();

    public UdpVideoSettings UdpVideo { get; set; } = new();

    public sealed class MqttSettings
    {
        public string Host { get; set; } = "192.168.12.1";

        public int Port { get; set; } = 3333;

        public string ClientId { get; set; } = "101";
    }

    public sealed class UdpVideoSettings
    {
        public int ListenPort { get; set; } = 3334;

        public string Codec { get; set; } = "hevc";
    }
}
