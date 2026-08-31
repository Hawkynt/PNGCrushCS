using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes the Canopus Lossless Codec (<c>CLLC</c>).</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/cllc.c</c>, copyright (c) 2012-2013 Derek Buitenhuis,
/// distributed there under LGPL-2.1-or-later. The canonical-code construction follows FFmpeg's
/// LGPL <c>ff_vlc_init_from_lengths</c>. This adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later.
/// <para/>
/// CLLC bitstreams are read MSB-first after byte-swapping every 16-bit word. Each frame carries its
/// own canonical Huffman tables and stores byte-valued predictive deltas. Coding types 0, 1/2 and 3
/// reconstruct YUV 4:2:2, RGB24 and ARGB respectively.
/// </remarks>
public sealed class CanopusLosslessVideoDecoder : IVideoCodecDecoder<CanopusLosslessVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("CLLC");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;

  private CanopusLosslessVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Canopus Lossless Codec";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static CanopusLosslessVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"CLLC stream {stream.Index} states an invalid {stream.Width}x{stream.Height} picture.");
    if ((long)stream.Width * stream.Height * 4 > int.MaxValue)
      throw new InvalidDataException($"CLLC stream {stream.Index} is too large to hold in one managed frame.");

    return new(stream.Width, stream.Height, stream.Index);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var source = packet.Data.Span;
    if (source.Length < 8)
      throw new InvalidDataException($"CLLC stream {this._streamIndex} supplied a frame shorter than eight bytes.");

    if (source[..4].SequenceEqual("INFO"u8)) {
      var infoLength = BinaryPrimitives.ReadUInt32LittleEndian(source[4..]);
      if (infoLength > int.MaxValue || (long)infoLength + 8 > source.Length)
        throw new InvalidDataException("A CLLC INFO prefix extends beyond the coded frame.");
      source = source[(checked((int)infoLength) + 8)..];
    }

    var dataSize = source.Length & ~1;
    if (dataSize < 4)
      throw new InvalidDataException("A CLLC frame has no complete 16-bit words after its optional INFO prefix.");
    source = source[..dataSize];

    var codingType = source[1];
    var bits = new WordSwappedBitReader(source);
    frame = codingType switch {
      0 => this._DecodeYuv422(ref bits),
      1 or 2 => this._DecodeRgb24(ref bits),
      3 => this._DecodeArgb(ref bits),
      _ => throw new NotSupportedException($"CLLC coding type {codingType} is not defined."),
    };
    return true;
  }

  private RawImage _DecodeRgb24(ref WordSwappedBitReader bits) {
    bits.Skip(16);
    var tables = new[] { _ReadTable(ref bits), _ReadTable(ref bits), _ReadTable(ref bits) };
    var output = new byte[checked(this._width * this._height * 3)];
    Span<int> topLeft = stackalloc int[] { 128, 128, 128 };

    for (var y = 0; y < this._height; ++y) {
      var rowAt = y * this._width * 3;
      for (var component = 0; component < 3; ++component) {
        var prediction = topLeft[component];
        for (var x = 0; x < this._width; ++x) {
          prediction += tables[component].Read(ref bits);
          output[rowAt + x * 3 + component] = unchecked((byte)prediction);
        }
        topLeft[component] = output[rowAt + component];
      }
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = output,
    };
  }

  private RawImage _DecodeArgb(ref WordSwappedBitReader bits) {
    bits.Skip(16);
    var tables = new[] { _ReadTable(ref bits), _ReadTable(ref bits), _ReadTable(ref bits), _ReadTable(ref bits) };
    var output = new byte[checked(this._width * this._height * 4)];
    Span<int> topLeft = stackalloc int[] { 0, 128, 128, 128 };

    for (var y = 0; y < this._height; ++y) {
      var rowAt = y * this._width * 4;
      Span<int> prediction = stackalloc int[4];
      topLeft.CopyTo(prediction);
      for (var x = 0; x < this._width; ++x) {
        var at = rowAt + x * 4;
        prediction[0] += tables[0].Read(ref bits);
        var alpha = unchecked((byte)prediction[0]);
        output[at] = alpha;
        if (alpha != 0) {
          for (var component = 1; component < 4; ++component) {
            prediction[component] += tables[component].Read(ref bits);
            output[at + component] = unchecked((byte)prediction[component]);
          }
        } else {
          output[at + 1] = 0;
          output[at + 2] = 0;
          output[at + 3] = 0;
        }
      }

      topLeft[0] = output[rowAt];
      if (topLeft[0] != 0) {
        topLeft[1] = output[rowAt + 1];
        topLeft[2] = output[rowAt + 2];
        topLeft[3] = output[rowAt + 3];
      }
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Argb32,
      PixelData = output,
    };
  }

  private RawImage _DecodeYuv422(ref WordSwappedBitReader bits) {
    if ((this._width & 1) != 0)
      throw new NotSupportedException($"CLLC stream {this._streamIndex} has odd-width 4:2:2 chroma ({this._width} pixels).");

    bits.Skip(8);
    var blocked = bits.ReadBits(8);
    if (blocked != 0)
      throw new NotSupportedException("Blocked CLLC YUV coding is not implemented by the LGPL reference decoder either.");

    var lumaTable = _ReadTable(ref bits);
    var chromaTable = _ReadTable(ref bits);
    var yPlane = new byte[checked(this._width * this._height)];
    var chromaWidth = this._width / 2;
    var uPlane = new byte[checked(chromaWidth * this._height)];
    var vPlane = new byte[uPlane.Length];
    var yTopLeft = 128;
    var uTopLeft = 128;
    var vTopLeft = 128;

    for (var row = 0; row < this._height; ++row) {
      yTopLeft = _DecodeLine(ref bits, lumaTable, yPlane.AsSpan(row * this._width, this._width), yTopLeft);
      uTopLeft = _DecodeLine(ref bits, chromaTable, uPlane.AsSpan(row * chromaWidth, chromaWidth), uTopLeft);
      vTopLeft = _DecodeLine(ref bits, chromaTable, vPlane.AsSpan(row * chromaWidth, chromaWidth), vTopLeft);
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = _Yuv422ToRgb(yPlane, uPlane, vPlane, this._width, this._height),
    };
  }

  private static int _DecodeLine(
    ref WordSwappedBitReader bits,
    HuffmanTable table,
    Span<byte> destination,
    int initialPrediction
  ) {
    var prediction = initialPrediction;
    for (var i = 0; i < destination.Length; ++i) {
      prediction += table.Read(ref bits);
      destination[i] = unchecked((byte)prediction);
    }
    return destination[0];
  }

  private static HuffmanTable _ReadTable(ref WordSwappedBitReader bits) {
    var lengthCount = checked((int)bits.ReadBits(5));
    if (lengthCount > 14)
      throw new InvalidDataException($"A CLLC Huffman table states a maximum code length of {lengthCount}; 14 is the format limit.");

    var entries = new List<(int Length, byte Symbol)>(256);
    for (var length = 1; length <= lengthCount; ++length) {
      var count = checked((int)bits.ReadBits(9));
      if (entries.Count + count > 256)
        throw new InvalidDataException("A CLLC Huffman table contains more than 256 symbols.");
      for (var i = 0; i < count; ++i)
        entries.Add((length, checked((byte)bits.ReadBits(8))));
    }

    if (entries.Count == 0)
      throw new InvalidDataException("A CLLC Huffman table contains no codes.");

    var codes = new Dictionary<int, byte>(entries.Count);
    uint code = 0;
    var previousLength = 0;
    var maximumLength = 0;
    foreach (var entry in entries) {
      if (entry.Length > previousLength)
        code <<= entry.Length - previousLength;
      if (code >= 1u << entry.Length)
        throw new InvalidDataException("A CLLC Huffman table is overdetermined.");

      var key = (entry.Length << 16) | checked((int)code);
      if (!codes.TryAdd(key, entry.Symbol))
        throw new InvalidDataException("A CLLC Huffman table assigns the same canonical code twice.");
      ++code;
      previousLength = entry.Length;
      maximumLength = entry.Length;
    }

    return new(codes, maximumLength);
  }

  private static byte[] _Yuv422ToRgb(
    ReadOnlySpan<byte> y,
    ReadOnlySpan<byte> u,
    ReadOnlySpan<byte> v,
    int width,
    int height
  ) {
    var output = new byte[checked(width * height * 3)];
    var chromaWidth = width / 2;
    var at = 0;
    for (var row = 0; row < height; ++row)
      for (var column = 0; column < width; ++column) {
        var yy = y[row * width + column];
        var chromaAt = row * chromaWidth + (column >> 1);
        var cb = u[chromaAt];
        var cr = v[chromaAt];
        var c = yy - 16;
        var d = cb - 128;
        var e = cr - 128;
        output[at++] = _Clamp((298 * c + 409 * e + 128) >> 8);
        output[at++] = _Clamp((298 * c - 100 * d - 208 * e + 128) >> 8);
        output[at++] = _Clamp((298 * c + 516 * d + 128) >> 8);
      }
    return output;
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

  private sealed class HuffmanTable {
    private readonly Dictionary<int, byte> _codes;
    private readonly int _maximumLength;

    internal HuffmanTable(Dictionary<int, byte> codes, int maximumLength) {
      this._codes = codes;
      this._maximumLength = maximumLength;
    }

    internal int Read(ref WordSwappedBitReader bits) {
      uint code = 0;
      for (var length = 1; length <= this._maximumLength; ++length) {
        code = (code << 1) | bits.ReadBits(1);
        if (this._codes.TryGetValue((length << 16) | checked((int)code), out var symbol))
          return symbol;
      }
      throw new InvalidDataException("A CLLC entropy code does not exist in the frame's Huffman table.");
    }
  }

  private ref struct WordSwappedBitReader {
    private readonly ReadOnlySpan<byte> _source;
    private int _position;

    internal WordSwappedBitReader(ReadOnlySpan<byte> source) {
      if ((source.Length & 1) != 0)
        throw new ArgumentException("The CLLC word-swapped bitreader requires complete 16-bit words.", nameof(source));
      this._source = source;
      this._position = 0;
    }

    internal void Skip(int count) {
      if (count < 0 || this._source.Length * 8 - this._position < count)
        throw new InvalidDataException("A CLLC frame ends while skipping its coding header.");
      this._position += count;
    }

    internal uint ReadBits(int count) {
      if (count < 0 || count > 32 || this._source.Length * 8 - this._position < count)
        throw new InvalidDataException($"A CLLC frame ends while reading {count} entropy bit(s).");

      uint value = 0;
      for (var i = 0; i < count; ++i) {
        var logicalByte = this._position >> 3;
        var sourceByte = logicalByte ^ 1;
        var bit = (this._source[sourceByte] >> (7 - (this._position & 7))) & 1;
        value = (value << 1) | (uint)bit;
        ++this._position;
      }
      return value;
    }
  }
}
