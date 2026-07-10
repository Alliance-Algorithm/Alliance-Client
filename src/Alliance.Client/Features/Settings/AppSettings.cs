namespace Alliance.Client.Features.Settings;

public sealed class AppSettings
{
    public string ApplicationName { get; set; } = "Alliance Client";

    public bool EnableDebugMode { get; set; } = true;

    public MqttSettings Mqtt { get; set; } = new();

    public VideoSettings Video { get; set; } = new();

    public sealed class MqttSettings
    {
        public string Host { get; set; } = "192.168.12.1";

        public int Port { get; set; } = 3333;

        public string ClientId { get; set; } = "101";
    }

    public sealed class VideoSettings
    {
        public bool Enabled { get; set; } = true;

        public int UdpPort { get; set; } = 3334;

        public int FrameWidth { get; set; } = 1920;

        public int FrameHeight { get; set; } = 1080;

        public int ExpectedFps { get; set; } = 60;

        public int PresentFps { get; set; } = 60;

        public int FrameAssemblyTimeoutMs { get; set; } = 50;

        public int HeartbeatIntervalMs { get; set; } = 250;

        public int HeartbeatTimeoutMs { get; set; } = 1000;

        public int SignalLostAfterMs { get; set; } = 500;

        public int ClearFrameAfterMs { get; set; } = 2000;

        public int RestartInitialDelayMs { get; set; } = 1000;

        public int RestartMaxDelayMs { get; set; } = 30000;
    }
}
