namespace Crush.Core;

/// <summary>Shared file size formatting utilities.</summary>
public static class FileFormatting {
  public static string FormatFileSize(long bytes) => bytes switch {
    < 1024 => $"{bytes} B",
    < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
    _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
  };
}
