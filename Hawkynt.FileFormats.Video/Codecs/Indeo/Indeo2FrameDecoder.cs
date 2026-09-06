using System;
using System.IO;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Decodes one Indeo 2 frame into the three planes the codec codes: luminance at full size and two
/// chrominance planes at a quarter of it in both directions.
/// </summary>
/// <remarks>
/// A frame is three planes coded one after the other out of one bitstream, and a plane is nothing but
/// a run of Huffman codes: a code either names a pair of entries in a delta table or a run of pairs
/// that say nothing. What "says nothing" means is the whole of the frame typing — on the first line of
/// an intra frame it is the neutral sample 0x80, on every line after it the sample directly above, and
/// in an inter frame it is the sample already there from the frame before.
/// <para/>
/// The planes come out in the order luminance, <b>red</b> difference, <b>blue</b> difference. That
/// crossing over is the format's and not a slip: the second plane in the stream is the one a viewer
/// displays as V.
/// <para/>
/// An inter frame's deltas are three quarters of the table's, rounded toward negative infinity. The
/// same table therefore covers both frame types at two strengths, which is why a frame states only
/// which of four tables it uses and never how much of it.
/// </remarks>
internal sealed class Indeo2FrameDecoder {

  /// <summary>Where the coded bits start. Everything before is header, of which three bytes are read.</summary>
  private const int _FRAME_HEADER_LENGTH = 48;

  /// <summary>Non-zero when the frame is intra-coded.</summary>
  private const int _INTRA_FLAG_OFFSET = 18;

  /// <summary>Two bits of luminance table index and two of chrominance table index.</summary>
  private const int _TABLE_SELECTOR_OFFSET = 0x22;

  /// <summary>The neutral sample an intra frame's first line falls back to.</summary>
  private const byte _NEUTRAL = 0x80;

  /// <summary>Symbols at or above this are runs; the run is twice the amount above 0x7F.</summary>
  private const int _RUN_BASE = 0x7F;

  private readonly int _width;
  private readonly int _height;
  private readonly int _chromaWidth;
  private readonly int _chromaHeight;

  private readonly byte[] _luma;
  private readonly byte[] _cb;
  private readonly byte[] _cr;

  internal Indeo2FrameDecoder(int width, int height) {
    this._width = width;
    this._height = height;
    this._chromaWidth = width >> 2;
    this._chromaHeight = height >> 2;

    this._luma = new byte[width * height];
    this._cb = new byte[this._chromaWidth * this._chromaHeight];
    this._cr = new byte[this._chromaWidth * this._chromaHeight];
  }

  internal int Width => this._width;
  internal int Height => this._height;
  internal int ChromaWidth => this._chromaWidth;
  internal int ChromaHeight => this._chromaHeight;

  /// <summary>The luminance plane of the frame decoded last, kept between frames because inter frames predict from it.</summary>
  internal byte[] Luma => this._luma;

  /// <summary>The blue-difference plane, a quarter of the picture's size in both directions.</summary>
  internal byte[] Cb => this._cb;

  /// <summary>The red-difference plane, a quarter of the picture's size in both directions.</summary>
  internal byte[] Cr => this._cr;

  /// <summary>Decodes one packet, which for this codec is always exactly one whole frame.</summary>
  internal void Decode(ReadOnlySpan<byte> data) {
    if (data.Length <= _FRAME_HEADER_LENGTH)
      throw new InvalidDataException(
        $"An Indeo 2 frame is {data.Length} bytes, which leaves nothing after its {_FRAME_HEADER_LENGTH}-byte header.");

    var isIntra = data[_INTRA_FLAG_OFFSET] != 0;
    var selector = data[_TABLE_SELECTOR_OFFSET];
    var lumaTable = selector & 3;
    var chromaTable = selector >> 2;

    if (chromaTable > 3)
      throw new InvalidDataException(
        $"An Indeo 2 frame names chrominance delta table {chromaTable}, where the codec defines four.");

    var bits = new Indeo2BitReader(data[_FRAME_HEADER_LENGTH..]);
    var luma = Indeo2Tables.Deltas[lumaTable];
    var chroma = Indeo2Tables.Deltas[chromaTable];

    if (isIntra) {
      this._DecodeIntraPlane(ref bits, this._luma, this._width, this._height, luma);
      this._DecodeIntraPlane(ref bits, this._cr, this._chromaWidth, this._chromaHeight, chroma);
      this._DecodeIntraPlane(ref bits, this._cb, this._chromaWidth, this._chromaHeight, chroma);
    } else {
      this._DecodeInterPlane(ref bits, this._luma, this._width, this._height, luma);
      this._DecodeInterPlane(ref bits, this._cr, this._chromaWidth, this._chromaHeight, chroma);
      this._DecodeInterPlane(ref bits, this._cb, this._chromaWidth, this._chromaHeight, chroma);
    }
  }

  /// <summary>
  /// Decodes one plane of an intra frame: absolute samples on the first line, differences from the line
  /// above on every line after it.
  /// </summary>
  private void _DecodeIntraPlane(ref Indeo2BitReader bits, byte[] plane, int width, int height, byte[] table) {
    // Two samples per code at the very least, so a plane cannot possibly be shorter than this and a
    // stream that says it is has been truncated rather than coded tightly.
    if (width * height / 32 > bits.BitsLeft)
      throw new InvalidDataException(
        $"An Indeo 2 intra plane of {width}x{height} cannot be coded in the {bits.BitsLeft} bits left in the frame.");

    var at = 0;
    while (at < width) {
      var code = bits.ReadSymbol();
      if (code >= Indeo2Tables.FIRST_RUN_SYMBOL) {
        var run = (code - _RUN_BASE) * 2;
        if (at + run > width)
          throw new InvalidDataException($"An Indeo 2 run of {run} samples overruns the {width}-sample line it starts on.");

        for (var i = 0; i < run; ++i)
          plane[at++] = _NEUTRAL;
      } else {
        plane[at++] = table[code * 2];
        plane[at++] = table[code * 2 + 1];
      }
    }

    var row = width;
    for (var y = 1; y < height; ++y, row += width) {
      at = 0;
      while (at < width) {
        if (bits.BitsLeft <= 0)
          throw new InvalidDataException($"An Indeo 2 intra plane runs out of bits on line {y} of {height}.");

        var code = bits.ReadSymbol();
        if (code >= Indeo2Tables.FIRST_RUN_SYMBOL) {
          var run = (code - _RUN_BASE) * 2;
          if (at + run > width)
            throw new InvalidDataException($"An Indeo 2 run of {run} samples overruns the {width}-sample line it starts on.");

          for (var i = 0; i < run; ++i, ++at)
            plane[row + at] = plane[row + at - width];
        } else {
          plane[row + at] = _Clamp(plane[row + at - width] + table[code * 2] - 128);
          ++at;
          plane[row + at] = _Clamp(plane[row + at - width] + table[code * 2 + 1] - 128);
          ++at;
        }
      }
    }
  }

  /// <summary>
  /// Decodes one plane of an inter frame: differences from the same plane of the frame before, at three
  /// quarters of the table's strength, with runs meaning nothing changed at all.
  /// </summary>
  private void _DecodeInterPlane(ref Indeo2BitReader bits, byte[] plane, int width, int height, byte[] table) {
    var row = 0;
    for (var y = 0; y < height; ++y, row += width) {
      var at = 0;
      while (at < width) {
        if (bits.BitsLeft <= 0)
          throw new InvalidDataException($"An Indeo 2 inter plane runs out of bits on line {y} of {height}.");

        var code = bits.ReadSymbol();
        if (code >= Indeo2Tables.FIRST_RUN_SYMBOL) {
          at += (code - _RUN_BASE) * 2;
        } else {
          plane[row + at] = _Clamp(plane[row + at] + (((table[code * 2] - 128) * 3) >> 2));
          ++at;
          plane[row + at] = _Clamp(plane[row + at] + (((table[code * 2 + 1] - 128) * 3) >> 2));
          ++at;
        }
      }
    }
  }

  private static byte _Clamp(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
}
