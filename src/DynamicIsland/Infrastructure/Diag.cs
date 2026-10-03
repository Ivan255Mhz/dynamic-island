namespace DynamicIsland.Infrastructure;

/// <summary>Lightweight counters for diagnosing background work (dev only).</summary>
public static class Diag
{
    public static int ClipboardPolls;
    public static int ClipboardReads;
    public static int ClipboardChanges;
    public static int MediaRefreshes;
    public static int MediaPropertyFetches;
    public static int MediaArtworkDecodes;
    public static int MediaManagerReady;
    public static int ScreenshotRefreshes;
}
