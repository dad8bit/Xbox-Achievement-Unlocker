using Memory;
using Microsoft.Extensions.Logging;

namespace XAU.Services;

public class XboxMemoryScanner : IXboxMemoryScanner
{
    private const string XAuthScanPattern = "58 42 4C 33 2E 30 20 78 3D";
    private readonly Mem _mem = new();
    private readonly ILogger<XboxMemoryScanner> _logger;

    public XboxMemoryScanner(ILogger<XboxMemoryScanner> logger)
    {
        _logger = logger;
    }

    public bool OpenXboxAppProcess()
    {
        return _mem.OpenProcess(ProcessNames.XboxPcApp);
    }

    public int GetXboxAppProcessId()
    {
        return _mem.GetProcIdFromName(ProcessNames.XboxPcApp);
    }

    public async Task<string?> ScanXauthFromXboxAppAsync()
    {
        try
        {
            var mem = new Mem();
            if (!mem.OpenProcess(ProcessNames.XboxPcApp))
            {
                return null;
            }

            var xauthScanList = await mem.AoBScan(XAuthScanPattern, true);
            var addressList = xauthScanList.ToList();
            if (addressList.Count == 0)
            {
                return null;
            }

            var xauthStrings = new string[addressList.Count];
            for (var i = 0; i < addressList.Count; i++)
            {
                xauthStrings[i] = mem.ReadString(addressList[i].ToString("X"), length: 10000);
            }

            var frequency = new Dictionary<string, int>();
            foreach (var str in xauthStrings)
            {
                if (string.IsNullOrWhiteSpace(str)) continue;

                if (!frequency.TryAdd(str, 1))
                {
                    frequency[str]++;
                }
            }

            if (frequency.Count == 0)
                return null;

            var mostCommon = string.Empty;
            var highestFrequency = 0;
            foreach (var pair in frequency)
            {
                if (pair.Value > highestFrequency)
                {
                    mostCommon = pair.Key;
                    highestFrequency = pair.Value;
                }
            }

            if (highestFrequency <= 3)
            {
                return null;
            }

            return XboxRestAPI.SanitizeXauthPublic(mostCommon);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred during Xbox app memory scan");
            return null;
        }
    }
}
