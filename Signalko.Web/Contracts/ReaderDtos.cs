namespace Signalko.Core.DTOs
{
    // 🇸🇮 Bralec DTO – uporablja se pri API-ju (GET/POST/PUT)
    public class ReaderDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public string? Hostname { get; set; }
        public bool Enabled { get; set; }
    }
}
