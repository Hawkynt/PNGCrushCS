namespace FileFormat.Pes;

/// <summary>A run of stitches sewn in one thread.</summary>
public sealed class PesStitchBlock {

  /// <summary>The entry in the thread chart this block names.</summary>
  public int ThreadIndex { get; init; }

  /// <summary>That entry's colour, packed 0xRRGGBB.</summary>
  public int Color { get; init; }

  /// <summary>Where the needle went, in the file's own units.</summary>
  public required (int X, int Y)[] Points { get; init; }
}
