using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XAU.Services
{
    public class CapturedEventModel
    {
        public int Index { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string EventName { get; set; } = "Unknown";
        public string TitleId { get; set; } = "Unknown";
        public string IKey { get; set; } = "";
        public string RawPayload { get; set; } = "";
        public string AnonymizedPayload { get; set; } = "";
        public int PayloadSize => RawPayload.Length;
    }

    public class SnifferAnalysisResult
    {
        public string? EventsToken { get; set; }
        public List<CapturedEventModel> Events { get; set; } = new List<CapturedEventModel>();
        public string Summary { get; set; } = "";
    }

    public interface IEventSnifferService
    {
        bool IsSniffing { get; }
        Task<bool> StartSniffingAsync();
        Task<SnifferAnalysisResult> StopAndAnalyzeAsync(string? currentXuid = null);
        string AnonymizeEventPayload(string rawJson, string? currentXuid = null);
    }
}
