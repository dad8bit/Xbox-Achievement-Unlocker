using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XAU.Util.Etw;

namespace XAU.Services
{
    public class EventSnifferService : IEventSnifferService
    {
        private readonly ILogger<EventSnifferService> _logger;
        private string? _activeTraceMethod;
        private DateTime _sniffStartTime;

        private static readonly string EtwTempDir = Path.Combine(Path.GetTempPath(), "XAU_ETW");
        private static readonly string EtwEtlPath = Path.Combine(EtwTempDir, "capture.etl");

        public bool IsSniffing => _activeTraceMethod != null;

        public EventSnifferService(ILogger<EventSnifferService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> StartSniffingAsync()
        {
            if (IsSniffing) return true;

            return await Task.Run(() =>
            {
                try
                {
                    EtwTokenCapture.Cleanup();
                    _activeTraceMethod = EtwTokenCapture.Start();
                    if (_activeTraceMethod != null)
                    {
                        _sniffStartTime = DateTime.UtcNow;
                        _logger.LogInformation("ETW Sniffer started using method: {Method}", _activeTraceMethod);
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("Failed to start ETW Sniffer trace. Administrator rights may be required.");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error starting ETW Sniffer trace");
                    return false;
                }
            });
        }

        public async Task<SnifferAnalysisResult> StopAndAnalyzeAsync(string? currentXuid = null)
        {
            return await Task.Run(() =>
            {
                var result = new SnifferAnalysisResult();

                if (!IsSniffing)
                {
                    result.Summary = "Sniffer was not actively running.";
                    return result;
                }

                try
                {
                    EtwTokenCapture.Stop(_activeTraceMethod);
                    _activeTraceMethod = null;

                    // Allow OS to flush ETL buffer to disk
                    Thread.Sleep(1500);

                    if (!File.Exists(EtwEtlPath))
                    {
                        result.Summary = "Trace stopped but ETL output file was not found.";
                        return result;
                    }

                    // Extract token if present
                    result.EventsToken = EtwTokenCapture.ExtractTokens();

                    // Parse telemetry & game events from ETL
                    var capturedEvents = ExtractTelemetryEventsFromEtl(EtwEtlPath, currentXuid);
                    result.Events = capturedEvents;
                    result.Summary = $"Analysis complete. Captured {capturedEvents.Count} event(s). Token: {(string.IsNullOrEmpty(result.EventsToken) ? "Not captured" : "Extracted")}.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping or analyzing ETW trace");
                    result.Summary = $"Analysis error: {ex.Message}";
                }
                finally
                {
                    EtwTokenCapture.CleanupFiles();
                }

                return result;
            });
        }

        private List<CapturedEventModel> ExtractTelemetryEventsFromEtl(string etlPath, string? currentXuid)
        {
            var list = new List<CapturedEventModel>();
            var seenPayloads = new HashSet<string>();

            try
            {
                byte[] allBytes = File.ReadAllBytes(etlPath);
                string text = Encoding.ASCII.GetString(allBytes);

                // Look for OneCollector / telemetry JSON structures: {"name": ... }
                // Pattern matches OneCollector records or typical Xbox live event schemas
                var jsonMatches = Regex.Matches(text, @"\{""name""\s*:\s*""[^""]+""[^{}]*\}|\{\s*""(?:ver|iKey|name|time|data)""\s*:[^{}]*(?:\{[^{}]*\}[^{}]*)*\}", RegexOptions.Compiled);

                int index = 1;
                foreach (Match m in jsonMatches)
                {
                    var raw = m.Value.Trim();
                    if (raw.Length < 20 || seenPayloads.Contains(raw)) continue;

                    try
                    {
                        var jobj = JObject.Parse(raw);
                        seenPayloads.Add(raw);

                        string eventName = jobj["name"]?.ToString() ?? "Telemetry.Event";
                        string iKey = jobj["iKey"]?.ToString() ?? "";
                        string titleId = "Unknown";

                        // Check for titleId in data / baseData / ext
                        var dataNode = jobj["data"] as JObject;
                        var baseData = dataNode?["baseData"] as JObject;
                        var extNode = jobj["ext"] as JObject;

                        if (baseData?["titleId"] != null) titleId = baseData["titleId"]!.ToString();
                        else if (dataNode?["titleId"] != null) titleId = dataNode["titleId"]!.ToString();
                        else if (extNode?["user"]?["localId"] != null) titleId = extNode["user"]!["localId"]!.ToString();

                        string formattedJson = jobj.ToString(Formatting.Indented);
                        string anonymized = AnonymizeEventPayload(formattedJson, currentXuid);

                        list.Add(new CapturedEventModel
                        {
                            Index = index++,
                            Timestamp = DateTime.Now,
                            EventName = eventName,
                            TitleId = titleId,
                            IKey = iKey,
                            RawPayload = formattedJson,
                            AnonymizedPayload = anonymized
                        });
                    }
                    catch
                    {
                        // Ignore malformed JSON chunks
                    }
                }

                // If regex found few matches, scan for lines containing v20.events.data.microsoft.com
                if (list.Count == 0)
                {
                    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.Contains("v20.events.data.microsoft.com") || line.Contains("OneCollector"))
                        {
                            list.Add(new CapturedEventModel
                            {
                                Index = index++,
                                Timestamp = DateTime.Now,
                                EventName = "OneCollector.NetworkTrace",
                                TitleId = "Unknown",
                                RawPayload = line,
                                AnonymizedPayload = AnonymizeEventPayload(line, currentXuid)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading telemetry events from ETL file");
            }

            return list;
        }

        public string AnonymizeEventPayload(string rawJson, string? currentXuid = null)
        {
            if (string.IsNullOrWhiteSpace(rawJson)) return rawJson;

            string result = rawJson;

            // Anonymize user XUID
            if (!string.IsNullOrEmpty(currentXuid))
            {
                result = result.Replace(currentXuid, "REPLACEXUID");
            }
            result = Regex.Replace(result, @"\b2533[0-9]{12}\b", "REPLACEXUID");

            // Anonymize timestamps: e.g. 2026-08-21T11:50:23.1234567Z
            result = Regex.Replace(result, @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z?", "REPLACETIME");

            // Anonymize sequence numbers if present
            result = Regex.Replace(result, @"(""seq""\s*:\s*)\d+", "$1REPLACESEQ");

            return result;
        }
    }
}
