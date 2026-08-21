using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Codecs.HuffYuv.Tests;

/// <summary>
/// Writes HuffYUV stream descriptions and frames, so a test can state exactly which part of the
/// layout it is exercising.
/// </summary>
/// <remarks>
/// Built rather than checked in. Most of these are arrangements ffmpeg's two encoders will not
/// produce — a table whose lengths tell the two possible code assignments apart, a frame that runs
/// out of bits, a description that states neither interlaced nor progressive — and the rest are
/// small enough that the expected picture can be written down beside them.
/// <para/>
/// The default table gives every one of the 256 symbols a code eight bits long, which under the
/// format's own assignment rule makes each symbol's code its own value. A frame is then a sequence
/// of bytes that are the differences themselves, and a test says what it means without a Huffman
/// table standing in the way.
/// </remarks>
internal sealed class HuffYuvTestStream {

  private const int _BITMAP_INFO_HEADER_SIZE = 40;

  private readonly List<byte> _bits = [];
  private int _partial;
  private int _partialBits;

  // ============================================================================================
  // The frame
  // ============================================================================================

  /// <summary>Appends symbols coded with the default table, where a symbol is its own byte.</summary>
  internal HuffYuvTestStream Symbols(params int[] values) {
    foreach (var value in values)
      this.Bits(value, 8);

    return this;
  }

  /// <summary>Appends the low <paramref name="count"/> bits of a value, most significant first.</summary>
  internal HuffYuvTestStream Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);

    return this;
  }

  /// <summary>Appends a code written as its bits, the way a table would be printed.</summary>
  internal HuffYuvTestStream Code(string code) {
    foreach (var character in code)
      switch (character) {
        case '0': this._Bit(0); break;
        case '1': this._Bit(1); break;
        case ' ': break;
        default: throw new ArgumentException($"'{character}' is not a bit.", nameof(code));
      }

    return this;
  }

  /// <summary>
  /// Finishes the frame, turning every four bytes back to front as the coder left them.
  /// </summary>
  internal byte[] End() {
    while (this._partialBits != 0)
      this._Bit(0);

    var length = (this._bits.Count + 3) / 4 * 4;
    var frame = new byte[length];
    this._bits.CopyTo(frame);

    for (var i = 0; i < length; i += 4)
      Array.Reverse(frame, i, 4);

    return frame;
  }

  private void _Bit(int bit) {
    this._partial = (this._partial << 1) | bit;
    if (++this._partialBits != 8)
      return;

    this._bits.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }

  // ============================================================================================
  // The description
  // ============================================================================================

  /// <summary>The run-length coded lengths of a table where every symbol's code is eight bits.</summary>
  internal static byte[] FlatTable() => [0x08, 0x80, 0x08, 0x80];

  /// <summary>
  /// The run-length coded lengths of a table stated symbol by symbol.
  /// </summary>
  /// <remarks>
  /// One run a symbol, which is wasteful and exactly what a test wants: the point is to say a
  /// particular set of lengths, not to say it briefly.
  /// </remarks>
  internal static byte[] TableOfLengths(params int[] lengths) {
    var bytes = new List<byte>();
    for (var i = 0; i < 256; ++i) {
      bytes.Add((byte)(i < lengths.Length ? lengths[i] : 0));
      bytes.Add(1);
    }

    return bytes.ToArray();
  }

  /// <summary>
  /// A stream description: a <c>BITMAPINFOHEADER</c>, the codec's four bytes, and its tables.
  /// </summary>
  internal static byte[] Description(byte method, byte depthAndSubsampling, byte flags, byte form, int tableCount, byte[]? table = null) {
    var bytes = new List<byte>(new byte[_BITMAP_INFO_HEADER_SIZE]);
    bytes.Add(method);
    bytes.Add(depthAndSubsampling);
    bytes.Add(flags);
    bytes.Add(form);

    for (var i = 0; i < tableCount; ++i)
      bytes.AddRange(table ?? FlatTable());

    return bytes.ToArray();
  }

  /// <summary>A stream coded in the planar form, whose description states its subsampling.</summary>
  internal static MediaStreamInfo PlanarStream(
    int width, int height, byte method, byte flags, int tableCount, int chromaHorizontal = 0, int chromaVertical = 0, byte[]? table = null) {
    var depth = (byte)(0x70 | chromaHorizontal | (chromaVertical << 2));
    return _Stream(width, height, 24, Description(method, depth, flags, 1, tableCount, table));
  }

  /// <summary>A stream coded in the interleaved form, whose description states a bitstream depth.</summary>
  internal static MediaStreamInfo InterleavedStream(int width, int height, byte method, byte bitstreamDepth, byte flags, byte[]? table = null)
    => _Stream(width, height, bitstreamDepth, Description(method, bitstreamDepth, flags, 0, 3, table));

  /// <summary>A stream whose description is only its <c>BITMAPINFOHEADER</c>, as the first version's is.</summary>
  internal static MediaStreamInfo UndescribedStream(int width, int height, int bitsPerPixel)
    => _Stream(width, height, bitsPerPixel, new byte[_BITMAP_INFO_HEADER_SIZE]);

  private static MediaStreamInfo _Stream(int width, int height, int bitsPerPixel, byte[] description) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("HFYU"),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    CodecPrivateData = description,
  };
}
