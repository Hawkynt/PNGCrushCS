namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// One reconstructed picture, as the three sample planes it is coded as.
/// </summary>
/// <remarks>
/// The planes are the size of a whole number of macroblocks and not the size of the picture. A
/// stream of 66 by 50 is coded as five macroblocks by four, and the fourteen columns and fourteen
/// rows that reach past the picture are coded, transmitted and reconstructed like any others —
/// motion vectors of later pictures may point into them. Storing the planes cropped would mean
/// either refusing those vectors or inventing what they point at; the crop belongs at the end, where
/// the picture is handed out.
/// <para/>
/// The chrominance planes are as large as <see cref="MpegChromaFormat"/> says and not always a
/// quarter of the luminance one. MPEG-1 only ever has 4:2:0, so a frame built for it is the same
/// frame it always was; MPEG-2 may hand over 4:2:2, where the chrominance planes are half as wide
/// and full height.
/// </remarks>
internal sealed class MpegFrame {

  internal MpegFrame(int lumaWidth, int lumaHeight, MpegChromaFormat chromaFormat) {
    this.LumaWidth = lumaWidth;
    this.LumaHeight = lumaHeight;
    this.ChromaFormat = chromaFormat;
    this.ChromaWidth = chromaFormat == MpegChromaFormat.Yuv444 ? lumaWidth : lumaWidth >> 1;
    this.ChromaHeight = chromaFormat == MpegChromaFormat.Yuv420 ? lumaHeight >> 1 : lumaHeight;
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

  internal MpegChromaFormat ChromaFormat { get; }

  internal byte[] Luma { get; }

  internal byte[] Cb { get; }

  internal byte[] Cr { get; }

  /// <summary>One of the three planes by component number, and how wide and tall it is.</summary>
  internal (byte[] Plane, int Width, int Height) PlaneOf(int component) => component switch {
    0 => (this.Luma, this.LumaWidth, this.LumaHeight),
    1 => (this.Cb, this.ChromaWidth, this.ChromaHeight),
    _ => (this.Cr, this.ChromaWidth, this.ChromaHeight),
  };
}
