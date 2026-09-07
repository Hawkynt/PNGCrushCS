namespace FileFormat.Pes;

/// <summary>A run of stitches sewn in one thread.</summary>
public sealed class PesStitchBlock {

  /// <summary>The entry in the thread chart this block names.</summary>
  public int ThreadIndex { get; init; }

  /// <summary>That entry's colour, packed 0xRRGGBB.</summary>
  public int Color { get; init; }

  /// <summary>Where the needle went, in the file's own units.</summary>
  public required (int X, int Y)[] Points { get; init; }

  /// <summary>
  /// Indices into <see cref="Points"/> reached by a jump rather than by a sewing stitch.
  /// </summary>
  /// <remarks>
  /// A jump changes the current needle position without drawing thread between the previous point
  /// and this one. Index zero is useful for positioning the first stitch of a block.
  /// </remarks>
  public int[] JumpIndices { get; init; } = [];
}
