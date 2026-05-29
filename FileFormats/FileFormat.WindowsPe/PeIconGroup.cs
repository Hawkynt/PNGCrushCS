namespace FileFormat.WindowsPe;

/// <summary>A single icon or cursor group extracted from PE resources.</summary>
public sealed class PeIconGroup {

  /// <summary>The resource ID of this group.</summary>
  public int GroupId { get; init; }

  /// <summary>Whether this group represents cursors (true) or icons (false).</summary>
  public bool IsCursor { get; init; }

  /// <summary>Complete ICO/CUR file bytes assembled from the PE resource entries.</summary>
  public byte[] IcoData { get; init; } = [];
}
