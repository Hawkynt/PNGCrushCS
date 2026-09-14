using System;

namespace FileFormat.Codecs.Lcl;

/// <summary>
/// The eight bytes the Lossless Codec Library (LCL) appends to a standard <c>BITMAPINFOHEADER</c>,
/// naming the frame's colour space, the compression level the encoder chose, the feature flags in
/// force, and which of the library's two sibling codecs — MSZH or ZLIB — wrote the stream.
/// </summary>
/// <remarks>
/// The field layout comes from Roberto Togni's published LCL notes. The flag values use the compatible
/// FFmpeg implementation because the prose calls the PNG predictor "bit 3" while that implementation —
/// and therefore interoperable bitstreams — assigns it numeric value <c>4</c> (zero-based bit 2).
/// FFmpeg's <c>libavcodec/lcl.h</c> is copyright (c) 2002-2004 Roberto Togni and LGPL-2.1-or-later;
/// these factual constants are used here by the LGPL-3.0-or-later project.
/// </remarks>
internal readonly record struct LclHeader(byte ImageType, sbyte Compression, byte Flags, byte Codec) {

  /// <summary>How many bytes of this trailer follow a standard 40-byte <c>BITMAPINFOHEADER</c>.</summary>
  public const int ExtraBytes = 8;

  public const byte MultithreadFlag = 0x01;
  public const byte NullFrameFlag = 0x02;
  public const byte PngFilterFlag = 0x04;
  public const byte KnownFlags = MultithreadFlag | NullFrameFlag | PngFilterFlag;

  /// <summary>Whether one frame packet contains two independently compressed sections.</summary>
  public bool Multithreaded => (this.Flags & MultithreadFlag) != 0;

  /// <summary>
  /// Whether the encoder may replace an unchanged AVI frame with a null chunk. The AVI layer owns
  /// that representation; a non-empty packet presented to a codec still describes an ordinary intra frame.
  /// </summary>
  public bool NullFramesUsed => (this.Flags & NullFrameFlag) != 0;

  /// <summary>Whether the decompressed bytes still carry LCL's per-row delta predictor.</summary>
  public bool PngFiltered => (this.Flags & PngFilterFlag) != 0;

  /// <summary>Flag bits no compatible LCL decoder assigns a meaning to.</summary>
  public byte UnknownFlags => (byte)(this.Flags & ~KnownFlags);

  /// <summary>
  /// Reads the trailer from the eight bytes immediately following a standard <c>BITMAPINFOHEADER</c>.
  /// The first four bytes are the format's historical <c>[4, 0, 0, 0]</c> field and carry no decoding state.
  /// </summary>
  public static LclHeader Read(ReadOnlySpan<byte> extra) => new(extra[4], unchecked((sbyte)extra[5]), extra[6], extra[7]);
}
