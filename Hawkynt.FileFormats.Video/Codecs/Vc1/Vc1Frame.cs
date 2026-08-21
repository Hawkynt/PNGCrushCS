namespace FileFormat.Codecs.Vc1;

/// <summary>
/// One reconstructed picture, as the three 4:2:0 sample planes it is coded as.
/// </summary>
/// <remarks>
/// The planes are a whole number of macroblocks across and down rather than the size of the picture.
/// SMPTE 421M 8.5 requires overlap smoothing to run over the macroblock-aligned dimensions — a frame
/// of 158x118 is smoothed as 160x128 — so the rows and columns past the picture edge are reconstructed
/// like any others and the crop belongs at the end, where the picture is handed out.
/// <para/>
/// Samples are held as signed integers rather than bytes because overlap smoothing runs on the
/// unclamped ten-bit reconstruction: the filter can push a value outside what a byte holds, and
/// clamping before it would lose exactly the range the filter exists to move.
/// </remarks>
internal sealed class Vc1Frame {

  internal Vc1Frame(int macroblockWidth, int macroblockHeight) {
    this.LumaWidth = macroblockWidth * 16;
    this.LumaHeight = macroblockHeight * 16;
    this.ChromaWidth = macroblockWidth * 8;
    this.ChromaHeight = macroblockHeight * 8;
    this.Luma = new int[this.LumaWidth * this.LumaHeight];
    this.Cb = new int[this.ChromaWidth * this.ChromaHeight];
    this.Cr = new int[this.ChromaWidth * this.ChromaHeight];
  }

  internal int LumaWidth { get; }

  internal int LumaHeight { get; }

  internal int ChromaWidth { get; }

  internal int ChromaHeight { get; }

  internal int[] Luma { get; }

  internal int[] Cb { get; }

  internal int[] Cr { get; }

  /// <summary>The plane one of a macroblock's six blocks belongs to, and how wide that plane is.</summary>
  internal (int[] Samples, int Stride) PlaneOf(int block) => block switch {
    < 4 => (this.Luma, this.LumaWidth),
    4 => (this.Cb, this.ChromaWidth),
    _ => (this.Cr, this.ChromaWidth),
  };
}
