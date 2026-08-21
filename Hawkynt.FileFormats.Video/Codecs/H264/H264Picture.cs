namespace FileFormat.Codecs.H264;

/// <summary>
/// One reconstructed picture, as the three 4:2:0 sample planes it is coded as, together with the two
/// numbers the reference machinery names it by.
/// </summary>
/// <remarks>
/// The planes are the coded size — a whole number of macroblocks — and not the displayed size. A
/// stream of 66x50 is coded as five macroblocks by four, and the fourteen columns and rows that reach
/// past the picture are coded, transmitted and reconstructed like any others; a later picture's motion
/// vector may point into them. Cropping belongs at the moment a picture is handed out, not before.
/// <para/>
/// There is no picture order count here, and its absence is deliberate. A picture order count exists
/// to put pictures back into display order after bidirectional prediction has coded them out of it —
/// and this decoder refuses B slices, so for every stream it accepts the decoding order and the
/// display order are the same. Carrying a field that nothing reads would be carrying the appearance
/// of reordering without the fact of it.
/// </remarks>
internal sealed class H264Picture {

  internal H264Picture(int lumaWidth, int lumaHeight, long serial) {
    this.Serial = serial;
    this.LumaWidth = lumaWidth;
    this.LumaHeight = lumaHeight;
    this.ChromaWidth = lumaWidth >> 1;
    this.ChromaHeight = lumaHeight >> 1;
    this.Luma = new byte[lumaWidth * lumaHeight];
    this.Cb = new byte[this.ChromaWidth * this.ChromaHeight];
    this.Cr = new byte[this.ChromaWidth * this.ChromaHeight];
  }

  internal int LumaWidth { get; }

  internal int LumaHeight { get; }

  internal int ChromaWidth { get; }

  internal int ChromaHeight { get; }

  internal byte[] Luma { get; }

  internal byte[] Cb { get; }

  internal byte[] Cr { get; }

  /// <summary>The plane a chroma component index names: 0 is Cb, 1 is Cr.</summary>
  internal byte[] Chroma(int component) => component == 0 ? this.Cb : this.Cr;

  /// <summary><c>frame_num</c> as the slices of this picture stated it.</summary>
  internal int FrameNum { get; set; }

  /// <summary>
  /// <c>PicNum</c> — clause 8.2.4.1: <see cref="FrameNum"/> shifted below the current picture's so
  /// that the wrap-around of a counter of a few bits does not reorder the reference list.
  /// </summary>
  /// <remarks>
  /// Recomputed for every reference picture at the start of every slice, because it is relative to
  /// the picture being decoded now: the same reference picture has a different <c>PicNum</c> in two
  /// consecutive pictures, and caching it would order the list by where the references were rather
  /// than by where they are.
  /// </remarks>
  internal int PicNum { get; set; }

  /// <summary>
  /// A number unique to this picture for as long as it exists, so that two reference indices can be
  /// asked whether they name the same picture.
  /// </summary>
  /// <remarks>
  /// The deblocking filter's boundary strength turns on exactly that question (clause 8.7.2.1,
  /// Note 1), and it has to be answered by picture and not by index: two macroblocks may reach the
  /// same reference through different entries of a list a slice reordered, and a filter comparing
  /// indices would put a strength of one on an edge with no discontinuity across it.
  /// </remarks>
  internal long Serial { get; }
}
