using System;
using System.IO;
using FileFormat.Core;
using static FileFormat.Codecs.TrueMotion1Format;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Duck TrueMotion 1 (<c>DUCK</c>, <c>PVEZ</c>): horizontal and vertical delta prediction over
/// pixel pairs, the deltas drawn from fixed vector tables by a byte-wide index stream, in both the
/// sixteen-bit RGB555 and the twenty-four-bit form.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/truemotion1.c</c>, copyright (C) 2003 Alex Beregszaszi and
/// Mike Melanson, LGPL-2.1-or-later; this adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later. The tables are FFmpeg's <c>truemotion1data.h</c> copied exactly — the stream
/// carries none of them, which is why this codec stood in <c>codec-investigations.md</c> until the
/// LGPL source was accepted as the first rung of the sourcing ladder.
/// <para/>
/// <b>The arithmetic is on pixel pairs, not pixels.</b> In the sixteen-bit form one 32-bit word holds
/// two RGB555 pixels and every delta is a 32-bit addend, so a carry out of one channel runs into the
/// next exactly as it does in the reference; the twenty-four-bit form holds one pixel a word. Both
/// are carried here as words and converted to <see cref="PixelFormat.Rgb24"/> only at the boundary,
/// five-bit channels widened by bit replication.
/// <para/>
/// <b>Inter frames edit the previous picture in place.</b> A change bit per four-pixel run (two in the
/// twenty-four-bit form) marks a run as copied from the previous picture, and the horizontal predictor
/// is re-seeded from what the copy left behind, so the decoder keeps its last picture for the next.
/// <para/>
/// The twenty-four-bit form states twice its width in the header, as FFmpeg reads it, and is decoded at
/// the halved width with a 2:1 sample aspect ratio. What the reference flags and gives up on is refused
/// here too, by name: sprite frames, odd widths and heights that are not a multiple of four, and — at
/// sixteen bits — a width that is not a multiple of four, where the reference writes past the row.
/// Where the reference stops decoding part-way because the index stream ran out or an index ran off its
/// table, leaving the rest of the picture as it was, this decoder refuses the frame instead, because a
/// half-updated picture is a plausible wrong picture. Compression types without an algorithm repeat the
/// previous picture, as does an empty packet; FFmpeg instead switches such a frame to sixteen bits at the
/// unhalved width, which matters only in a twenty-four-bit film and is the one place the two differ.
/// </remarks>
public sealed class TrueMotion1VideoDecoder : IVideoCodecDecoder<TrueMotion1VideoDecoder> {

  private static readonly CodecTag _Duck = CodecTag.FromCharacters("DUCK");
  private static readonly CodecTag _Pvez = CodecTag.FromCharacters("PVEZ");

  private readonly int _streamIndex;
  private readonly PredictorTables _tables = new();
  private int _lastDeltaSet;
  private int _lastVectorTable;
  private Algorithm _tableAlgorithm = Algorithm.None;

  private int _width;
  private int _height;
  private Algorithm _algorithm = Algorithm.None;
  private uint[] _words = [];
  private uint[] _vertical = [];
  private bool _hasPicture;

  private TrueMotion1VideoDecoder(int streamIndex) => this._streamIndex = streamIndex;

  public static string CodecName => "Duck TrueMotion 1";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video
      && (stream.Codec.EqualsIgnoringCase(_Duck) || stream.Codec.EqualsIgnoringCase(_Pvez));
  }

  public static TrueMotion1VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Duck TrueMotion 1.");

    // The picture size is stated by every frame header and may differ from the container's — the
    // twenty-four-bit form's header width is twice the picture's — so it is taken from the frames.
    return new(stream.Index);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length == 0) {
      if (!this._hasPicture)
        throw new InvalidDataException($"TrueMotion 1 stream {this._streamIndex} begins with an empty packet.");

      frame = this._Picture();
      return true;
    }

    var header = ReadHeader(data);
    var flags = header.EffectiveFlags;
    if ((flags & FlagSprite) != 0)
      throw new NotSupportedException(
        $"TrueMotion 1 stream {this._streamIndex} carries a sprite frame, which neither this decoder nor FFmpeg's implements.");

    if (header.Compression >= CompressionTypes.Length)
      throw new InvalidDataException($"A TrueMotion 1 header names the invalid compression type {header.Compression}.");

    var type = CompressionTypes[header.Compression];
    var vectorTable = SelectVectorTable(header);

    // A compression type without an algorithm codes no picture: the previous one stands. FFmpeg reads
    // such a frame as a switch to sixteen bits at the unhalved width, which in a twenty-four-bit film
    // discards the picture the following inter frames edit; that is a reading of FFmpeg's, not of the
    // format, and is not reproduced. The frame changes neither the picture nor the tables.
    if (type.Algorithm == Algorithm.None) {
      if (!this._hasPicture)
        throw new InvalidDataException($"TrueMotion 1 stream {this._streamIndex} begins with a frame that codes no picture.");

      frame = this._Picture();
      return true;
    }

    var algorithm = type.Algorithm;
    var width = header.XSize >> (algorithm == Algorithm.Rgb24 ? 1 : 0);
    var height = header.YSize;

    if ((width & 1) != 0)
      throw new NotSupportedException($"TrueMotion 1 stream {this._streamIndex} states the odd width {width}.");
    if ((height & 3) != 0 || height == 0)
      throw new NotSupportedException(
        $"TrueMotion 1 stream {this._streamIndex} states a height of {height}, which is not a positive multiple of four.");
    if (width == 0)
      throw new InvalidDataException($"TrueMotion 1 stream {this._streamIndex} states a zero width.");
    if (algorithm == Algorithm.Rgb16 && (width & 3) != 0)
      throw new NotSupportedException(
        $"TrueMotion 1 stream {this._streamIndex} states a sixteen-bit width of {width}; every row is coded in runs of "
        + "four pixels and a width that is not a multiple of four has no defined last run.");
    if ((long)width * height > int.MaxValue / 4)
      throw new InvalidDataException($"TrueMotion 1 stream {this._streamIndex} states a {width}x{height} picture, too large to hold.");

    if (width != this._width || height != this._height || algorithm != this._algorithm) {
      this._width = width;
      this._height = height;
      this._algorithm = algorithm;
      this._words = new uint[this._WordsPerRow * height];
      this._vertical = new uint[width];
      this._hasPicture = false;
    }

    // As in the reference, the tables are rebuilt when the delta set or the vector table number
    // changes. They are also rebuilt when the depth does, which the reference does not do and only ever
    // misses after one of the empty frames above.
    if (header.DeltaSet != this._lastDeltaSet || header.VectorTable != this._lastVectorTable
        || algorithm != this._tableAlgorithm) {
      this._tables.SelectDeltas(header.DeltaSet);
      this._tables.Generate(vectorTable, algorithm);
      this._tableAlgorithm = algorithm;
    }

    this._lastDeltaSet = header.DeltaSet;
    this._lastVectorTable = header.VectorTable;

    var keyFrame = (flags & FlagKeyFrame) != 0;
    if (!keyFrame && !this._hasPicture)
      throw new InvalidDataException(
        $"TrueMotion 1 stream {this._streamIndex} begins with an inter frame, which has no picture to change.");

    var changeRowSize = ((width >> (algorithm == Algorithm.Rgb24 ? 1 : 2)) + 7) >> 3;
    var changeBits = data[header.HeaderSize..];
    ReadOnlySpan<byte> indices;
    if (keyFrame) {
      if (width * height / 2048 + header.HeaderSize > data.Length)
        throw new InvalidDataException($"A TrueMotion 1 key frame of {data.Length} bytes is too short for its picture.");
      indices = changeBits;
    } else {
      var changeBytes = changeRowSize * (height >> 2);
      if (changeBytes > changeBits.Length)
        throw new InvalidDataException("A TrueMotion 1 inter frame ends inside its change bits.");
      indices = changeBits[changeBytes..];
    }

    this._DecodePicture(type, keyFrame, changeBits, changeRowSize, indices);

    this._hasPicture = true;
    frame = this._Picture();
    return true;
  }

  private int _WordsPerRow => this._algorithm == Algorithm.Rgb24 ? this._width : this._width >> 1;

  private void _DecodePicture(
    CompressionType type, bool keyFrame, ReadOnlySpan<byte> changeBits, int changeRowSize, ReadOnlySpan<byte> indices) {
    var rgb24 = this._algorithm == Algorithm.Rgb24;
    var luma = this._tables.Luma;
    var chroma = this._tables.Chroma;
    var escapeLuma = rgb24 ? this._tables.FatLuma : this._tables.Luma;
    var escapeChroma = rgb24 ? this._tables.FatChroma : this._tables.Chroma;
    var escapeScale = rgb24 ? 1u : 5u;
    var wordsPerRow = this._WordsPerRow;
    var stepPixels = rgb24 ? 2 : 4;

    var stream = new IndexStream(indices);
    var vertical = this._vertical.AsSpan(0, wordsPerRow);
    vertical.Clear();
    var words = this._words.AsSpan();

    var index = stream.Next();
    var rowChange = changeBits;

    for (var y = 0; y < this._height; ++y) {
      uint horizontal = 0;
      var at = y * wordsPerRow;
      var v = 0;
      var changeAt = 0;
      byte changeByte = 0;
      byte changeMask = 1;
      if (!keyFrame)
        changeByte = rowChange[changeAt++];

      for (var pixelsLeft = this._width; pixelsLeft > 0; pixelsLeft -= stepPixels) {
        if (keyFrame || (changeByte & changeMask) == 0) {
          switch (y & 3) {
            case 0:
              if (type.BlockWidth == 2) {
                _Apply(chroma, escapeChroma, escapeScale, ref horizontal, ref index, ref stream);
                _Apply(luma, escapeLuma, escapeScale, ref horizontal, ref index, ref stream);
                _Output(words, vertical, ref at, ref v, horizontal);
                _Apply(chroma, escapeChroma, escapeScale, ref horizontal, ref index, ref stream);
                _Apply(luma, escapeLuma, escapeScale, ref horizontal, ref index, ref stream);
                _Output(words, vertical, ref at, ref v, horizontal);
              } else {
                _Apply(chroma, escapeChroma, escapeScale, ref horizontal, ref index, ref stream);
                _Apply(luma, escapeLuma, escapeScale, ref horizontal, ref index, ref stream);
                _Output(words, vertical, ref at, ref v, horizontal);
                _Apply(luma, escapeLuma, escapeScale, ref horizontal, ref index, ref stream);
                _Output(words, vertical, ref at, ref v, horizontal);
              }
              break;

            case 2 when type.BlockType == BlockType.Block2x2:
              _Apply(chroma, escapeChroma, escapeScale, ref horizontal, ref index, ref stream);
              _Apply(luma, escapeLuma, escapeScale, ref horizontal, ref index, ref stream);
              _Output(words, vertical, ref at, ref v, horizontal);
              _Apply(chroma, escapeChroma, escapeScale, ref horizontal, ref index, ref stream);
              _Apply(luma, escapeLuma, escapeScale, ref horizontal, ref index, ref stream);
              _Output(words, vertical, ref at, ref v, horizontal);
              break;

            case 2 when type.BlockType == BlockType.Block4x2:
              _Apply(chroma, escapeChroma, escapeScale, ref horizontal, ref index, ref stream);
              _Apply(luma, escapeLuma, escapeScale, ref horizontal, ref index, ref stream);
              _Output(words, vertical, ref at, ref v, horizontal);
              _Apply(luma, escapeLuma, escapeScale, ref horizontal, ref index, ref stream);
              _Output(words, vertical, ref at, ref v, horizontal);
              break;

            default:
              _Apply(luma, escapeLuma, escapeScale, ref horizontal, ref index, ref stream);
              _Output(words, vertical, ref at, ref v, horizontal);
              _Apply(luma, escapeLuma, escapeScale, ref horizontal, ref index, ref stream);
              _Output(words, vertical, ref at, ref v, horizontal);
              break;
          }
        } else {
          // Copy the run from the previous picture and re-seed the horizontal predictor from it.
          vertical[v++] = words[at++];
          horizontal = unchecked(words[at] - vertical[v]);
          vertical[v++] = words[at++];
        }

        if (!keyFrame) {
          changeMask <<= 1;
          if (changeMask == 0) {
            // FFmpeg reads one byte past a row whose run count is a multiple of eight; it is never used.
            changeByte = changeAt < changeRowSize ? rowChange[changeAt] : (byte)0;
            ++changeAt;
            changeMask = 1;
          }
        }
      }

      if (!keyFrame && ((y + 1) & 3) == 0)
        rowChange = rowChange[changeRowSize..];
    }
  }

  private static void _Output(Span<uint> words, Span<uint> vertical, ref int at, ref int v, uint horizontal) {
    var value = unchecked(vertical[v] + horizontal);
    words[at++] = value;
    vertical[v++] = value;
  }

  /// <summary>FFmpeg's <c>APPLY_*_PREDICTOR</c>: one delta, then the next index or the escape.</summary>
  private static void _Apply(
    uint[] table, uint[] escapeTable, uint escapeScale, ref uint horizontal, ref int index, ref IndexStream stream) {
    var pair = table[index];
    horizontal = unchecked(horizontal + (pair >> 1));
    if ((pair & 1) == 0) {
      _Increment(ref index);
      return;
    }

    index = stream.Next();
    if (index != 0)
      return;

    index = stream.Next();
    pair = escapeTable[index];
    horizontal = unchecked(horizontal + (pair >> 1) * escapeScale);
    if ((pair & 1) != 0)
      index = stream.Next();
    else
      _Increment(ref index);
  }

  private static void _Increment(ref int index) {
    if (index >= TableSize - 1)
      throw new InvalidDataException("A TrueMotion 1 index runs off the end of its vector table.");
    ++index;
  }

  private RawImage _Picture() {
    var pixels = new byte[this._width * this._height * 3];
    var o = 0;
    if (this._algorithm == Algorithm.Rgb24) {
      foreach (var word in this._words) {
        pixels[o++] = (byte)(word >> 16);
        pixels[o++] = (byte)(word >> 8);
        pixels[o++] = (byte)word;
      }
    } else {
      foreach (var word in this._words) {
        _Rgb555(pixels, ref o, (ushort)word);
        _Rgb555(pixels, ref o, (ushort)(word >> 16));
      }
    }

    return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static void _Rgb555(byte[] pixels, ref int o, ushort word) {
    int r = (word >> 10) & 31, g = (word >> 5) & 31, b = word & 31;
    pixels[o++] = (byte)((r << 3) | (r >> 2));
    pixels[o++] = (byte)((g << 3) | (g >> 2));
    pixels[o++] = (byte)((b << 3) | (b >> 2));
  }

  /// <summary>The picture as the reference holds it: RGB555 pairs or 0RGB words, row-major.</summary>
  internal ReadOnlySpan<uint> NativeWords => this._words;

  private ref struct IndexStream(ReadOnlySpan<byte> data) {
    private readonly ReadOnlySpan<byte> _data = data;
    private int _at;

    public int Next() {
      if (this._at >= this._data.Length)
        throw new InvalidDataException("A TrueMotion 1 frame's index stream ends before its picture does.");
      return this._data[this._at++] * 4;
    }
  }
}
