using System;

namespace FileFormat.Codecs.H265;

/// <summary>How the decoded picture buffer is holding a picture — ITU-T H.265, clause 8.3.2.</summary>
internal enum H265ReferenceMarking {

  /// <summary>Not a reference: kept only until it has been output.</summary>
  Unused = 0,

  /// <summary>A reference named by an offset from the current picture's count.</summary>
  ShortTerm = 1,

  /// <summary>A reference named by an absolute count, which may be arbitrarily far back.</summary>
  LongTerm = 2,
}

/// <summary>
/// One decoded picture: its three sample planes, its count, and the motion it was coded with.
/// </summary>
/// <remarks>
/// The motion field is carried with the picture rather than discarded with the slice that produced
/// it, because a later picture may predict its own vectors from it — HEVC's temporal candidate reads
/// the motion of whatever was at the same position in a picture the slice header names. It is stored
/// at the finest granularity a prediction block can have rather than at the sixteen-sample grid the
/// standard permits a decoder to compress it to: compression there is an allowance for memory, not a
/// change to the result, and the uncompressed field is the one a spatial neighbour also needs.
/// <para/>
/// The planes are the coded size and not the displayed one. A stream whose width is not a whole
/// number of minimum coding blocks is coded larger and cropped on output, and the samples past the
/// crop are reconstructed like any others — a later picture's motion vector may point into them.
/// </remarks>
internal sealed class H265Picture {

  internal H265Picture(int width, int height, int minBlockLog2Size) {
    this.Width = width;
    this.Height = height;
    this.ChromaWidth = width >> 1;
    this.ChromaHeight = height >> 1;
    this.Luma = new ushort[width * height];
    this.Cb = new ushort[this.ChromaWidth * this.ChromaHeight];
    this.Cr = new ushort[this.ChromaWidth * this.ChromaHeight];

    this.MinBlockLog2Size = minBlockLog2Size;
    this.BlocksAcross = (width + (1 << minBlockLog2Size) - 1) >> minBlockLog2Size;
    this.BlocksDown = (height + (1 << minBlockLog2Size) - 1) >> minBlockLog2Size;

    var blocks = this.BlocksAcross * this.BlocksDown;
    this.Motion = new H265MotionField(blocks);
    this.IsIntraBlock = new bool[blocks];
  }

  internal int Width { get; }

  internal int Height { get; }

  internal int ChromaWidth { get; }

  internal int ChromaHeight { get; }

  /// <summary>
  /// The luminance samples. Sixteen bits wide because Main 10 codes ten of them; an eight-bit
  /// stream stores its samples here unshifted, so the plane's values are always in the sequence's
  /// own depth and every consumer has to know that depth to read them.
  /// </summary>
  internal ushort[] Luma { get; }

  internal ushort[] Cb { get; }

  internal ushort[] Cr { get; }

  /// <summary>The plane a chroma component index names: 0 is Cb, 1 is Cr.</summary>
  internal ushort[] Chroma(int component) => component == 0 ? this.Cb : this.Cr;

  internal int MinBlockLog2Size { get; }

  internal int BlocksAcross { get; }

  internal int BlocksDown { get; }

  /// <summary>The motion each smallest prediction block was coded with.</summary>
  internal H265MotionField Motion { get; }

  /// <summary>Whether each smallest block was intra coded, which has no motion to predict from.</summary>
  internal bool[] IsIntraBlock { get; }

  /// <summary>The picture order count: what puts pictures back into the order they are shown in.</summary>
  internal int PictureOrderCount { get; set; }

  internal H265ReferenceMarking Marking { get; set; }

  /// <summary>Whether this picture is to be shown, rather than only held for others to predict from.</summary>
  internal bool IsOutput { get; set; } = true;

  /// <summary>Whether this picture has been handed to the caller.</summary>
  internal bool WasOutput { get; set; }

  /// <summary>
  /// The picture order counts each of this picture's own references had, by list and index.
  /// </summary>
  /// <remarks>
  /// Kept because the temporal motion vector candidate has to scale the motion it borrows by the
  /// ratio of two distances: how far the borrowed vector reached in the picture it came from, and
  /// how far the current picture's own reference is. The second is known now; the first is only
  /// knowable if the picture remembers what its own references were, so it does.
  /// </remarks>
  internal int[][] ReferencePictureOrderCounts { get; set; } = [[], []];

  internal bool[][] ReferenceIsLongTerm { get; set; } = [[], []];
}

/// <summary>
/// The motion of every smallest prediction block of one picture, stored as parallel arrays.
/// </summary>
/// <remarks>
/// Parallel arrays rather than an array of a motion struct because the two reference lists are read
/// independently far more often than together — a spatial candidate asks for list zero's vector
/// alone, and a bidirectional block's two halves are predicted one after the other.
/// </remarks>
internal sealed class H265MotionField {

  internal H265MotionField(int blocks) {
    this.PredictionFlagL0 = new bool[blocks];
    this.PredictionFlagL1 = new bool[blocks];
    this.RefIdxL0 = new sbyte[blocks];
    this.RefIdxL1 = new sbyte[blocks];
    this.MvL0X = new short[blocks];
    this.MvL0Y = new short[blocks];
    this.MvL1X = new short[blocks];
    this.MvL1Y = new short[blocks];

    Array.Fill(this.RefIdxL0, (sbyte)-1);
    Array.Fill(this.RefIdxL1, (sbyte)-1);
  }

  internal bool[] PredictionFlagL0 { get; }

  internal bool[] PredictionFlagL1 { get; }

  internal sbyte[] RefIdxL0 { get; }

  internal sbyte[] RefIdxL1 { get; }

  internal short[] MvL0X { get; }

  internal short[] MvL0Y { get; }

  internal short[] MvL1X { get; }

  internal short[] MvL1Y { get; }

  internal bool PredictionFlag(int list, int block)
    => list == 0 ? this.PredictionFlagL0[block] : this.PredictionFlagL1[block];

  internal sbyte RefIdx(int list, int block) => list == 0 ? this.RefIdxL0[block] : this.RefIdxL1[block];

  internal short MvX(int list, int block) => list == 0 ? this.MvL0X[block] : this.MvL1X[block];

  internal short MvY(int list, int block) => list == 0 ? this.MvL0Y[block] : this.MvL1Y[block];

  internal void Set(int list, int block, bool predict, int refIdx, int mvX, int mvY) {
    if (list == 0) {
      this.PredictionFlagL0[block] = predict;
      this.RefIdxL0[block] = (sbyte)refIdx;
      this.MvL0X[block] = (short)mvX;
      this.MvL0Y[block] = (short)mvY;
      return;
    }

    this.PredictionFlagL1[block] = predict;
    this.RefIdxL1[block] = (sbyte)refIdx;
    this.MvL1X[block] = (short)mvX;
    this.MvL1Y[block] = (short)mvY;
  }
}
