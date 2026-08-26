namespace FileFormat.Codecs.Escape130;

/// <summary>
/// One 2x2 block's persistent colour state: a six-bit Y', a five-bit Pb' and a five-bit Pr', plus,
/// for a "four brightness" block, the per-pixel sign pattern and difference strength that give its
/// four pixels different luma around that shared base.
/// </summary>
/// <remarks>
/// This is what a block code reads relative to and what a "reuse previous block" code clones whole —
/// pattern included, not merely the scalar colour, which is what cloning means for a four-brightness
/// block's own four different pixels. A single-colour block always carries <see cref="IsFourBrightness"/>
/// false and paints its four pixels identically.
/// </remarks>
internal struct Escape130Block {

  /// <summary>The block's base luma, six bits (0-63) for a single-colour block or a four-brightness
  /// block's <c>ccccc</c> field already doubled to the same six-bit scale.</summary>
  internal int Y;

  /// <summary>Five-bit Pb'.</summary>
  internal int Pb;

  /// <summary>Five-bit Pr'.</summary>
  internal int Pr;

  /// <summary>Whether this block's four pixels vary around <see cref="Y"/> by <see cref="Sign"/> and
  /// <see cref="Diff"/> rather than sharing it outright.</summary>
  internal bool IsFourBrightness;

  /// <summary>The six-bit sign selector choosing each pixel's +1/0/-1 offset from the base.</summary>
  internal int Sign;

  /// <summary>The two-bit difference selector choosing how far that offset reaches.</summary>
  internal int Diff;

  internal readonly void CopyTo(ref Escape130Block target) {
    target.Y = this.Y;
    target.Pb = this.Pb;
    target.Pr = this.Pr;
    target.IsFourBrightness = this.IsFourBrightness;
    target.Sign = this.Sign;
    target.Diff = this.Diff;
  }
}
