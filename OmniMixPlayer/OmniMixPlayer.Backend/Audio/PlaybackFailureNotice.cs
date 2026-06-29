namespace OmniMixPlayer.Backend.Audio
{
    public sealed class PlaybackFailureNotice
    {
        public string InstanceId { get; set; } = "";
        public string ModuleId { get; set; } = "";
        public string ModuleName { get; set; } = "";
        public int Count { get; set; }
        public string Message { get; set; } = "";
        public long CreatedAtUnixMs { get; set; }
    }
}
