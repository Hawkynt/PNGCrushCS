namespace FileFormat.Codecs.Indeo;

/// <summary>
/// What a picture is divided into, which is the one thing an Indeo decoder has to be told before it
/// can read a frame at all.
/// </summary>
/// <remarks>
/// Indeo 4 states this in every picture header; Indeo 5 states it once per group of pictures, in the
/// header a key frame carries. Either way a change to any field of it means every buffer, tile and
/// macroblock descriptor is rebuilt, which is why the two are compared as a whole rather than field
/// by field at the places that use them.
/// </remarks>
internal readonly record struct IviPictureConfiguration(
  int PictureWidth,
  int PictureHeight,
  int ChromaWidth,
  int ChromaHeight,
  int TileWidth,
  int TileHeight,
  int LumaBands,
  int ChromaBands);

/// <summary>
/// One macroblock's coding decisions: its type, which of its blocks are coded, its quantiser offset
/// and its motion vectors.
/// </summary>
/// <remarks>
/// The narrow types are the format's and not a saving. A motion vector component is one signed byte,
/// so a run of deltas that accumulates past 127 wraps rather than saturating, and the quantiser delta
/// is likewise one signed byte. Widening either would decode a stream that relies on the wrap into a
/// different picture.
/// <para/>
/// These descriptors outlive the tile they were read for: a chrominance band and a higher wavelet
/// band inherit their types, quantisers and motion vectors from the first luminance band's
/// macroblocks, which is what <see cref="IviTile.ReferenceMacroblocks"/> points at.
/// </remarks>
internal sealed class IviMacroblock {

  /// <summary>The macroblock's position within its band, in samples.</summary>
  internal int X;

  internal int Y;

  /// <summary>Where the macroblock's first sample sits in the band buffer.</summary>
  internal int BufferOffset;

  /// <summary>
  /// 0 intra, 1 forward-predicted, 2 backward-predicted, 3 bidirectional. Only Indeo 4 uses the last
  /// two.
  /// </summary>
  internal byte Type;

  /// <summary>One bit per block of the macroblock, set where the block carries coefficients.</summary>
  internal byte CodedBlockPattern;

  /// <summary>What this macroblock adds to its band's quantiser.</summary>
  internal sbyte QuantiserDelta;

  /// <summary>The forward motion vector, in half-samples where the band says so.</summary>
  internal sbyte MotionX;

  internal sbyte MotionY;

  /// <summary>The backward motion vector of a bidirectional macroblock.</summary>
  internal sbyte BackwardMotionX;

  internal sbyte BackwardMotionY;
}

/// <summary>
/// One tile: the rectangle of a band that a stream may code, and skip, on its own.
/// </summary>
/// <remarks>
/// A tile that is coded states its own size in bytes, which is what lets a decoder skip one it does
/// not want and what makes a mis-read tile detectable — the position after decoding it has to land
/// exactly where the size said. A tile that is empty carries nothing at all and means "the same
/// rectangle of the reference frame", possibly moved by motion vectors inherited from another band.
/// </remarks>
internal sealed class IviTile {

  internal int X;

  internal int Y;

  internal int Width;

  internal int Height;

  internal int MacroblockSize;

  /// <summary>Whether the tile carries no data, which is the copy-or-move case.</summary>
  internal bool IsEmpty;

  /// <summary>The tile's coded size in bytes, as the tile itself states it.</summary>
  internal int DataSize;

  internal IviMacroblock[] Macroblocks = [];

  /// <summary>
  /// The macroblocks of the matching tile of the first luminance band, from which this tile's
  /// macroblocks may inherit their motion vectors and quantisers.
  /// </summary>
  internal IviMacroblock[]? ReferenceMacroblocks;
}

/// <summary>
/// One wavelet band of one colour plane, with the buffers it decodes into and everything its header
/// selected.
/// </summary>
/// <remarks>
/// A band is the unit a picture is actually coded in. A plane has either one band, which is the whole
/// picture, or four, which are the four quadrants of one wavelet decomposition and are recomposed on
/// the way out. Each band has its own transform, scan pattern, quantisation matrix, block codebook
/// and run-value map, and each carries its own tiles.
/// <para/>
/// Four buffers rather than one because a band is its own reference: two hold the current and
/// previous pictures, a third serves the scalable mode, and a fourth is the second reference of
/// Indeo 4's bidirectional frames.
/// </remarks>
internal sealed class IviBand {

  internal int Plane;

  internal int BandNumber;

  internal int Width;

  internal int Height;

  /// <summary>The band's height rounded up to whole macroblocks, which is what the buffers hold.</summary>
  internal int AlignedHeight;

  /// <summary>How far apart two rows of a band buffer are, which is the width rounded up likewise.</summary>
  internal int Pitch;

  /// <summary>How many samples a band buffer holds.</summary>
  internal int BufferSize;

  internal readonly short[]?[] Buffers = new short[4][];

  /// <summary>The buffer being decoded into.</summary>
  internal short[] Buffer = [];

  /// <summary>The buffer predicted from.</summary>
  internal short[]? Reference;

  /// <summary>The second buffer a bidirectional macroblock predicts from.</summary>
  internal short[]? BackwardReference;

  internal bool IsEmpty;

  internal int MacroblockSize;

  internal int BlockSize;

  /// <summary>Whether motion vectors are in half-samples rather than whole ones.</summary>
  internal int IsHalfPel;

  /// <summary>Whether macroblock types and motion vectors come from the first luminance band.</summary>
  internal bool InheritMotionVectors;

  internal bool InheritQuantiserDelta;

  /// <summary>Whether a quantiser delta is coded at all, which only Indeo 5 says.</summary>
  internal bool QuantiserDeltaPresent;

  /// <summary>Which dequantisation matrix the band's header selected. Indeo 4 only.</summary>
  internal int QuantiserMatrix;

  /// <summary>The band's own quantiser, to which each macroblock's delta is added.</summary>
  internal int GlobalQuantiser;

  internal byte[] Scan = [];

  internal int ScanSize;

  /// <summary>The codebook this band's blocks are coded with, which may be its own or the picture's.</summary>
  internal readonly IviCodebookSlot Codebook = new();

  /// <summary>The symbol pairs whose meanings this band swaps in its run-value map.</summary>
  internal readonly byte[] Corrections = new byte[61 * 2];

  internal int CorrectionCount;

  internal int RunValueMapSelector;

  internal IviRunValueMap RunValueMap = IviRunValueMap.Defaults[0];

  internal IviTile[] Tiles = [];

  internal IviInverseTransform? InverseTransform;

  internal IviDcTransform? DcTransform;

  internal int TransformSize;

  /// <summary>
  /// Whether the transform runs in both dimensions, which is what decides whether the DC coefficient
  /// is predicted from the block before.
  /// </summary>
  internal bool IsTwoDimensional;

  internal bool ChecksumPresent;

  internal int Checksum;

  internal ushort[] IntraBase = [];

  internal ushort[] InterBase = [];

  /// <summary>The quantiser scale tables, which Indeo 5 has and Indeo 4 does not.</summary>
  internal byte[]? IntraScale;

  internal byte[]? InterScale;
}

/// <summary>One colour plane and the bands it is divided into.</summary>
internal sealed class IviPlane {

  internal int Width;

  internal int Height;

  internal IviBand[] Bands = [];
}
