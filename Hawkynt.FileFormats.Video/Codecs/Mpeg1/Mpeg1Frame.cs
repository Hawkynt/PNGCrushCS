namespace FileFormat.Codecs.Mpeg1;

/// <summary>
/// One reconstructed picture, as the three 4:2:0 sample planes it is coded as.
/// </summary>
/// <remarks>
/// The planes are the size of a whole number of macroblocks and not the size of the picture. A
/// stream of 66 by 50 is coded as five macroblocks by four, and the fourteen columns and fourteen
/// rows that reach past the picture are coded, transmitted and reconstructed like any others —
/// motion vectors of later pictures may point into them. Storing the planes cropped would mean
/// either refusing those vectors or inventing what they point at; the crop belongs at the end, where
/// the picture is handed out.
/// </remarks>
internal sealed class Mpeg1Frame {

  internal Mpeg1Frame(int lumaWidth, int lumaHeight) {
    this.LumaWidth = lumaWidth;
    this.LumaHeight = lumaHeight;
    this.ChromaWidth = lumaWidth >> 1;
    this.ChromaHeight = lumaHeight >> 1;
    this.Luma = new byte[lumaWidth * lumaHeight];
    this.Cb = new byte[this.ChromaWidth * this.ChromaHeight];
    this.Cr = new byte[this.ChromaWidth * this.ChromaHeight];
  }

  /// <summary>Width of the luminance plane: the picture's width rounded up to a whole macroblock.</summary>
  internal int LumaWidth { get; }

  /// <summary>Height of the luminance plane, likewise rounded up.</summary>
  internal int LumaHeight { get; }

  internal int ChromaWidth { get; }

  internal int ChromaHeight { get; }

  internal byte[] Luma { get; }

  internal byte[] Cb { get; }

  internal byte[] Cr { get; }

  /// <summary>The plane a block index of a macroblock belongs to, and how wide it is.</summary>
  /// <remarks>
  /// Blocks nought to three are the four luminance quadrants of the macroblock, block four is Cb and
  /// block five is Cr — 11172-2, Figure 2-9.
  /// </remarks>
  internal (byte[] Plane, int Width, int Height) PlaneOf(int blockIndex) => blockIndex switch {
    < 4 => (this.Luma, this.LumaWidth, this.LumaHeight),
    4 => (this.Cb, this.ChromaWidth, this.ChromaHeight),
    _ => (this.Cr, this.ChromaWidth, this.ChromaHeight),
  };

}
