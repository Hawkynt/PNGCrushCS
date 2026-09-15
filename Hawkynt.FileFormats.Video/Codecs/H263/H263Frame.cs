namespace FileFormat.Codecs.H263;

/// <summary>
/// One reconstructed picture, as the three 4:2:0 sample planes it is coded as.
/// </summary>
/// <remarks>
/// The planes are a whole number of macroblocks across and down rather than the size of the picture.
/// The five standard H.263 formats are all whole macroblocks anyway, but the custom sizes a Sorenson
/// Spark stream may state are not, and the rows and columns past the picture edge are coded,
/// transmitted and reconstructed like any others — a later picture's motion vector may point into
/// them. Storing the planes cropped would mean either refusing those vectors or inventing what they
/// point at; the crop belongs at the end, where the picture is handed out.
/// <para/>
/// Annex O direct-mode B-pictures also need the motion vector of the co-located macroblock in their
/// temporally subsequent reference picture. Those vectors live beside the reconstructed samples for
/// exactly the same lifetime as the reference picture rather than in the picture decoder that produced
/// them, whose state disappears at the next picture.
/// </remarks>
internal sealed class H263Frame {

  internal H263Frame(int macroblockWidth, int macroblockHeight) {
    this.LumaWidth = macroblockWidth * 16;
    this.LumaHeight = macroblockHeight * 16;
    this.ChromaWidth = macroblockWidth * 8;
    this.ChromaHeight = macroblockHeight * 8;
    this.Luma = new byte[this.LumaWidth * this.LumaHeight];
    this.Cb = new byte[this.ChromaWidth * this.ChromaHeight];
    this.Cr = new byte[this.ChromaWidth * this.ChromaHeight];

    var macroblocks = macroblockWidth * macroblockHeight;
    this.MotionX = new short[macroblocks];
    this.MotionY = new short[macroblocks];
    this.HasMotion = new bool[macroblocks];
  }

  internal int LumaWidth { get; }

  internal int LumaHeight { get; }

  internal int ChromaWidth { get; }

  internal int ChromaHeight { get; }

  internal byte[] Luma { get; }

  internal byte[] Cb { get; }

  internal byte[] Cr { get; }

  /// <summary>The eight-bit temporal reference carried by the picture header.</summary>
  internal int TemporalReference { get; set; }

  /// <summary>
  /// The reconstructed single-vector motion field, in half-pixel units, for Annex O direct mode.
  /// </summary>
  /// <remarks>
  /// <see cref="HasMotion"/> is false for an intra-coded macroblock. A skipped predicted macroblock
  /// has a real zero vector and therefore sets it to true. Advanced Prediction's four-vector mode is
  /// not stored here because the decoder refuses that mode before a reference can be produced.
  /// </remarks>
  internal short[] MotionX { get; }

  internal short[] MotionY { get; }

  internal bool[] HasMotion { get; }

  /// <summary>The plane one of a macroblock's six blocks belongs to, and how wide it is.</summary>
  /// <remarks>
  /// Blocks one to four of ITU-T H.263 Figure 5 are the luminance quadrants in reading order, block
  /// five is Cb and block six is Cr; they are numbered from nought here.
  /// </remarks>
  internal (byte[] Plane, int Width, int Height) PlaneOf(int blockIndex) => blockIndex switch {
    < 4 => (this.Luma, this.LumaWidth, this.LumaHeight),
    4 => (this.Cb, this.ChromaWidth, this.ChromaHeight),
    _ => (this.Cr, this.ChromaWidth, this.ChromaHeight),
  };
}