using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Codecs.MagicYuv.Tests;

/// <summary>
/// Writes MagicYUV frames, so a test can state exactly which part of the layout it is exercising.
/// </summary>
/// <remarks>
/// Built rather than checked in. The format is published nowhere, so every rule the decoder follows
/// was measured; these state the same rules from the other side, as frames whose expected picture
/// can be written down beside them.
/// <para/>
/// The default table gives every one of the 256 symbols a code eight bits long. Under the format's
/// own rule — longest length first, ascending symbols within a length — that makes symbol
/// <c>s</c>'s code <c>s</c> itself, so a coded slice is a sequence of bytes that are the differences
/// themselves and a test says what it means without a table standing in the way.
/// </remarks>
internal sealed class MagicYuvTestStream {

  internal const int HEADER_SIZE = 32;
  internal const byte VERSION = 7;

  /// <summary>The two values a slice's first byte takes.</summary>
  internal const byte CODED = 0;
  internal const byte UNCOMPRESSED = 1;

  /// <summary>The prediction methods, as a slice numbers them.</summary>
  internal const byte LEFT = 1;
  internal const byte GRADIENT = 2;
  internal const byte MEDIAN = 3;

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  // ============================================================================================
  // A slice's bits
  // ============================================================================================

  /// <summary>Appends symbols coded with the flat table, where a symbol's code is its own value.</summary>
  internal MagicYuvTestStream Symbols(params int[] values) {
    foreach (var value in values)
      this.Bits(value, 8);

    return this;
  }

  /// <summary>Appends the low <paramref name="count"/> bits of a value, most significant first.</summary>
  internal MagicYuvTestStream Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);

    return this;
  }

  /// <summary>Appends a code written as its bits, the way a table would be printed.</summary>
  internal MagicYuvTestStream Code(string code) {
    foreach (var character in code)
      switch (character) {
        case '0': this._Bit(0); break;
        case '1': this._Bit(1); break;
        case ' ': break;
        default: throw new ArgumentException($"'{character}' is not a bit.", nameof(code));
      }

    return this;
  }

  /// <summary>Finishes one slice's bits, which are plain bytes with no word swapping.</summary>
  internal byte[] End() {
    while (this._partialBits != 0)
      this._Bit(0);

    return this._bytes.ToArray();
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

  /// <summary>Lengths stated symbol by symbol, every symbol not named given none.</summary>
  internal static byte[] LengthsOf(params int[] lengths) {
    var all = new byte[256];
    for (var i = 0; i < lengths.Length; ++i)
      all[i] = (byte)lengths[i];

    return all;
  }

  // ============================================================================================
  // A frame
  // ============================================================================================

  /// <summary>One piece of a frame: the bytes of one plane's one slice, and how they are stored.</summary>
  internal readonly record struct Piece(byte Flag, byte Predictor, byte[] Body) {

    internal static Piece Coded(byte predictor, byte[] body) => new(CODED, predictor, body);
    internal static Piece Plain(byte predictor, byte[] body) => new(UNCOMPRESSED, predictor, body);
  }

  /// <summary>
  /// A whole frame: header, offsets, the slice map, the tables, and the pieces.
  /// </summary>
  /// <remarks>
  /// <paramref name="pieces"/> is indexed plane first, then slice. The data is laid out the other
  /// way round — every plane of slice nought, then every plane of slice one — and the offsets are
  /// written plane first with the map naming which piece each one belongs to, which is exactly the
  /// arrangement measured in real frames.
  /// </remarks>
  internal static byte[] Frame(
    int width, int height, int sliceHeight, int planes, int slices, byte[][] tables,
    Piece[,] pieces, byte version = VERSION, int headerSize = HEADER_SIZE,
    byte[]? signature = null, int? tableCount = null, byte[]? map = null) {
    var count = planes * slices;
    var frame = new List<byte>();

    frame.AddRange(signature ?? [(byte)'M', (byte)'A', (byte)'G', (byte)'Y']);
    frame.AddRange(BitConverter.GetBytes(headerSize));
    frame.Add(version);
    frame.Add(0x65);
    frame.Add(0x0C);
    frame.Add(0x00);
    frame.AddRange(BitConverter.GetBytes(0x00200000));
    frame.AddRange(BitConverter.GetBytes(width));
    frame.AddRange(BitConverter.GetBytes(height));
    frame.AddRange(BitConverter.GetBytes(width));
    frame.AddRange(BitConverter.GetBytes(sliceHeight));

    // where the pieces will sit once the tables are behind them
    var tablesEnd = HEADER_SIZE + 4 * (count + 1) + 1 + count + tables.Length * 256;
    var starts = new int[planes, slices];
    var at = tablesEnd;
    for (var s = 0; s < slices; ++s)
      for (var p = 0; p < planes; ++p) {
        starts[p, s] = at;
        at += 2 + pieces[p, s].Body.Length;
      }

    frame.AddRange(BitConverter.GetBytes(tablesEnd - HEADER_SIZE));
    for (var p = 0; p < planes; ++p)
      for (var s = 0; s < slices; ++s)
        frame.AddRange(BitConverter.GetBytes(starts[p, s] - HEADER_SIZE));

    frame.Add((byte)(tableCount ?? tables.Length));
    if (map != null)
      frame.AddRange(map);
    else
      for (var p = 0; p < planes; ++p)
        for (var s = 0; s < slices; ++s)
          frame.Add((byte)(s * planes + p));

    foreach (var table in tables)
      frame.AddRange(table);

    for (var s = 0; s < slices; ++s)
      for (var p = 0; p < planes; ++p) {
        frame.Add(pieces[p, s].Flag);
        frame.Add(pieces[p, s].Predictor);
        frame.AddRange(pieces[p, s].Body);
      }

    return frame.ToArray();
  }

  /// <summary>A frame of one plane and one slice, which is what most of these tests want.</summary>
  internal static byte[] SinglePlane(int width, int height, byte predictor, byte[] body,
    byte[]? lengths = null, byte flag = CODED) {
    var pieces = new Piece[1, 1];
    pieces[0, 0] = new(flag, predictor, body);
    return Frame(width, height, height, 1, 1, [lengths ?? FlatLengths()], pieces);
  }

  // ============================================================================================
  // The description
  // ============================================================================================

  /// <summary>A stream description, which for this codec says only the code and the size.</summary>
  internal static MediaStreamInfo Stream(string fourcc, int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(fourcc),
    Width = width,
    Height = height,
    BitsPerPixel = 24,
  };
}
