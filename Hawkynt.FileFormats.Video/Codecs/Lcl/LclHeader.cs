using System;

namespace FileFormat.Codecs.Lcl;

/// <summary>
/// The eight bytes the Lossless Codec Library (LCL) appends to a standard <c>BITMAPINFOHEADER</c>,
/// naming the frame's colour space, the compression level the encoder chose, the feature flags in
/// force, and which of the library's two sibling codecs — MSZH or ZLIB — wrote the stream.
/// </summary>
/// <remarks>
/// Recovered from "Description of the LCL codecs (MSZH and ZLIB)" by Roberto Togni
/// (multimedia.cx/lcl.txt, GNU FDL 1.2), the one written description of this format that is not an
/// implementation. Its own first paragraph calls itself "random notes... while building a decoder",
/// and says so again at the end: several fields are left as <c>[add ...]</c> placeholders the document
/// never fills in. FFmpeg's LGPL-2.1-or-later <c>lcl.h</c> supplies the numeric flag constants used by
/// the interoperable decoder, including <c>FLAG_PNGFILTER = 4</c>.
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
