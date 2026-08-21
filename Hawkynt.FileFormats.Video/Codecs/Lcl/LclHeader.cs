using System;

namespace FileFormat.Codecs.Lcl;

/// <summary>
/// The eight bytes the Lossless Codec Library (LCL) appends to a standard <c>BITMAPINFOHEADER</c>,
/// naming the frame's colour space, the compression level the encoder chose, the feature flags in
/// force, and which of the library's two sibling codecs — MSZH or ZLIB — wrote the stream. Shared
/// between both, since the trailer is one layout regardless of which of them is reading it.
/// </summary>
/// <remarks>
/// Recovered from "Description of the LCL codecs (MSZH and ZLIB)" by Roberto Togni
/// (multimedia.cx/lcl.txt, GNU FDL 1.2), the one written description of this format that is not an
/// implementation. Its own first paragraph calls itself "random notes... while building a decoder",
/// and says so again at the end: several fields are left as <c>[add ...]</c> placeholders the document
/// never fills in.
/// </remarks>
internal readonly record struct LclHeader(byte ImageType, sbyte Compression, byte Flags, byte Codec) {

  /// <summary>How many bytes of this trailer follow a standard 40-byte <c>BITMAPINFOHEADER</c>.</summary>
  public const int ExtraBytes = 8;

  /// <summary>
  /// Bit 0 of <see cref="Flags"/>: the coded picture is split into two independently decodable
  /// sections. The document says so and states nothing else about it — not the length or offset
  /// fields that would say where one section ends and the other begins — so nothing here reads one.
  /// </summary>
  public bool Multithreaded => (this.Flags & 0x01) != 0;

  /// <summary>
  /// Bit 1 of <see cref="Flags"/>: the encoder may replace an unchanged frame with a null one. Not
  /// something a decoder has to act on — the document says the container takes care of it, and this
  /// package's AVI reader already drops a zero-length chunk before any codec sees it — so nothing
  /// here reads this bit either.
  /// </summary>
  public bool NullFramesUsed => (this.Flags & 0x02) != 0;

  /// <summary>
  /// Bit 3 of <see cref="Flags"/>: a per-line prediction applied between decompression and colour
  /// conversion, ZLIB only. Its structure is one of the document's unfilled placeholders, and its own
  /// author states outright that his RGB24 implementation of it "doesn't work ok" — so there is
  /// nothing here to decode it against even if the byte layout were known.
  /// </summary>
  public bool PngFiltered => (this.Flags & 0x08) != 0;

  /// <summary>
  /// Reads the trailer from the eight bytes immediately following a standard <c>BITMAPINFOHEADER</c>.
  /// The first four of those are a field the document calls "unknown" and states is always
  /// <c>[4, 0, 0, 0]</c> — not interpreted here, since nothing reads it.
  /// </summary>
  public static LclHeader Read(ReadOnlySpan<byte> extra) => new(extra[4], unchecked((sbyte)extra[5]), extra[6], extra[7]);
}
