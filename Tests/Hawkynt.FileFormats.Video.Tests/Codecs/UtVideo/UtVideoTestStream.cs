using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Codecs.UtVideo.Tests;

/// <summary>
/// Writes Ut Video stream descriptions and frames, so a test can state exactly which part of the
/// layout it is exercising.
/// </summary>
/// <remarks>
/// Built rather than checked in. Most of these are arrangements ffmpeg's encoder will not produce —
/// a table whose lengths tell the two possible code assignments apart, a gradient-predicted frame,
/// a stream that says it is coded some other way — and the rest are small enough that the expected
/// picture can be written down beside them.
/// <para/>
/// The default table gives every one of the 256 symbols a code eight bits long. Under the format's
/// own rule — longest length first, and within a length the highest symbol first — that makes
/// symbol <c>s</c>'s code <c>255 - s</c>, so a frame is a sequence of bytes that are the
/// differences complemented. <see cref="Symbol"/> hides that, so a test says what it means.
/// </remarks>
internal sealed class UtVideoTestStream {

  private const int _BITMAP_INFO_HEADER_SIZE = 40;

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  // ============================================================================================
  // A slice's bits
  // ============================================================================================

  /// <summary>Appends symbols coded with the flat table.</summary>
  internal UtVideoTestStream Symbols(params int[] values) {
    foreach (var value in values)
      this.Bits(255 - value, 8);

    return this;
  }

  /// <summary>Appends the low <paramref name="count"/> bits of a value, most significant first.</summary>
  internal UtVideoTestStream Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);

    return this;
  }

  /// <summary>Appends a code written as its bits, the way a table would be printed.</summary>
  internal UtVideoTestStream Code(string code) {
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
  /// Finishes one slice, turning every four bytes back to front as the coder left them.
  /// </summary>
  internal byte[] End() {
    while (this._partialBits != 0)
      this._Bit(0);

    var length = (this._bytes.Count + 3) / 4 * 4;
    var slice = new byte[length];
    this._bytes.CopyTo(slice);

    for (var i = 0; i < length; i += 4)
      Array.Reverse(slice, i, 4);

    return slice;
  }

  private void _Bit(int bit) {
    this._partial = (this._partial << 1) | bit;
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }

  // ============================================================================================
  // The tables
  // ============================================================================================

  /// <summary>Lengths giving every symbol a code eight bits long.</summary>
  internal static byte[] FlatLengths() {
    var lengths = new byte[256];
    Array.Fill(lengths, (byte)8);
    return lengths;
  }

  /// <summary>
  /// Lengths stated symbol by symbol, with every symbol not named marked as not occurring.
  /// </summary>
  internal static byte[] LengthsOf(params int[] lengths) {
    var all = new byte[256];
    Array.Fill(all, (byte)0xFF);
    for (var i = 0; i < lengths.Length; ++i)
      all[i] = (byte)lengths[i];

    return all;
  }

  /// <summary>Lengths for a plane in which one symbol occurs and no other, which costs no bits.</summary>
  internal static byte[] OnlySymbol(int symbol) {
    var all = new byte[256];
    Array.Fill(all, (byte)0xFF);
    all[symbol] = 0;
    return all;
  }

  // ============================================================================================
  // A frame
  // ============================================================================================

  /// <summary>One plane of a frame: its code lengths and the slices that follow them.</summary>
  internal readonly record struct Plane(byte[] Lengths, byte[][] Slices) {

    /// <summary>A plane of one slice coded with the flat table.</summary>
    internal static Plane Flat(byte[] slice) => new(FlatLengths(), [slice]);
  }

  /// <summary>
  /// A frame: every plane's table and slices, then the trailer that states the prediction.
  /// </summary>
  internal static byte[] Frame(int predictor, params Plane[] planes) {
    var frame = new List<byte>();
    foreach (var plane in planes) {
      frame.AddRange(plane.Lengths);

      var end = 0;
      foreach (var slice in plane.Slices) {
        end += slice.Length;
        frame.AddRange(BitConverter.GetBytes(end));
      }

      foreach (var slice in plane.Slices)
        frame.AddRange(slice);
    }

    frame.AddRange(BitConverter.GetBytes((uint)predictor << 8));
    return frame.ToArray();
  }

  // ============================================================================================
  // The description
  // ============================================================================================

  /// <summary>The prediction methods, as the frame trailer numbers them.</summary>
  internal const int NONE = 0;
  internal const int LEFT = 1;
  internal const int GRADIENT = 2;
  internal const int MEDIAN = 3;

  /// <summary>The flag saying a frame is Huffman coded, which every stream read here sets.</summary>
  internal const uint HUFFMAN = 0x00000001;

  /// <summary>The flag saying the picture is two interleaved fields.</summary>
  internal const uint INTERLACED = 0x00000800;

  /// <summary>
  /// A stream description: a <c>BITMAPINFOHEADER</c> and the sixteen bytes behind it.
  /// </summary>
  internal static MediaStreamInfo Stream(
    string fourcc, int width, int height, int slices = 1, uint flags = HUFFMAN, int frameInfoSize = 4,
    int extraLength = 16) {
    var description = new List<byte>(new byte[_BITMAP_INFO_HEADER_SIZE]);
    description.AddRange(BitConverter.GetBytes(0x010000F0u));
    description.AddRange(BitConverter.GetBytes(0u));
    description.AddRange(BitConverter.GetBytes((uint)frameInfoSize));
    description.AddRange(BitConverter.GetBytes(flags | ((uint)(slices - 1) << 24)));

    var bytes = description.ToArray();
    if (extraLength < 16)
      Array.Resize(ref bytes, _BITMAP_INFO_HEADER_SIZE + extraLength);

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters(fourcc),
      Width = width,
      Height = height,
      BitsPerPixel = fourcc == "ULRA" ? 32 : 24,
      CodecPrivateData = bytes,
    };
  }
}
