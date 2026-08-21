namespace FileFormat.Codecs.Roq;

/// <summary>
/// One reconstructed RoQ picture, as three sample planes all at the picture's own full resolution.
/// </summary>
/// <remarks>
/// A RoQ codebook cell states one Cb and one Cr value for a 2x2 area of luma, which reads as 4:2:0 —
/// but that is only true of a cell at the moment it is painted. Motion compensation moves whatever a
/// block already holds, chroma included, at full pixel precision and not at half; a block copied from
/// an odd offset carries chroma that no longer lines up with any 2x2 grid at all. Measured against
/// ffmpeg's own decode: its native output for RoQ is <c>yuvj444p</c>, full-resolution chroma throughout,
/// not <c>yuvj420p</c>, and keeping <see cref="Cb"/> and <see cref="Cr"/> here at the picture's own
/// width and height rather than at half of it is what reproduces that bit for bit. There is no
/// chroma-siting convention to get wrong here, unlike a genuinely 4:2:0 codec, because after the first
/// frame nothing about this format's chroma is subsampled in fact, whatever its codebook states in
/// theory.
/// </remarks>
internal sealed class RoqFrame {

  internal RoqFrame(int width, int height) {
    this.Width = width;
    this.Height = height;
    this.Y = new byte[width * height];
    this.Cb = new byte[width * height];
    this.Cr = new byte[width * height];
  }

  internal int Width { get; }

  internal int Height { get; }

  internal byte[] Y { get; }

  internal byte[] Cb { get; }

  internal byte[] Cr { get; }

  /// <summary>Overwrites every sample of this frame with another's, without allocating.</summary>
  internal void CopyFrom(RoqFrame other) {
    System.Array.Copy(other.Y, this.Y, this.Y.Length);
    System.Array.Copy(other.Cb, this.Cb, this.Cb.Length);
    System.Array.Copy(other.Cr, this.Cr, this.Cr.Length);
  }
}
