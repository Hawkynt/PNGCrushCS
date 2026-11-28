using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs;

/// <summary>
/// What a Duck TrueMotion 1 frame header says and the predictor tables it selects — the half of the
/// codec the decoder and the encoder share.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/truemotion1.c</c>, copyright (C) 2003 Alex Beregszaszi and
/// Mike Melanson, LGPL-2.1-or-later; this adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later.
/// <para/>
/// The predictor tables are built exactly as the reference builds them, including two details that
/// look like slips and are not: the skinny luminance deltas are halved after their low bit is cleared,
/// so a negative odd delta rounds away from zero, and every entry is stored doubled with its low bit
/// carrying the end-of-group flag, so adding the entry shifted right by one discards bit 31 of a
/// negative delta. Both only ever touch bits no pixel reads, but the 32-bit pixel-pair arithmetic is
/// carried bit for bit so that two pictures agree as words and not merely as colours.
/// </remarks>
internal static class TrueMotion1Format {

  public const int TableSize = 1024;

  public const int FlagSprite = 32;
  public const int FlagKeyFrame = 16;
  public const int FlagInterFrame = 8;

  public enum Algorithm { None, Rgb16, Rgb24 }

  public enum BlockType { Block2x2, Block2x4, Block4x2, Block4x4 }

  public readonly record struct CompressionType(Algorithm Algorithm, int BlockWidth, int BlockHeight, BlockType BlockType);

  /// <summary>The seventeen compression types, by number, as FFmpeg's <c>compression_types</c> lists them.</summary>
  public static readonly CompressionType[] CompressionTypes = [
    new(Algorithm.None, 0, 0, BlockType.Block2x2),
    new(Algorithm.Rgb16, 4, 4, BlockType.Block4x4),
    new(Algorithm.Rgb16, 4, 4, BlockType.Block4x4),
    new(Algorithm.Rgb16, 4, 2, BlockType.Block4x2),
    new(Algorithm.Rgb16, 4, 2, BlockType.Block4x2),
    new(Algorithm.Rgb16, 2, 4, BlockType.Block2x4),
    new(Algorithm.Rgb16, 2, 4, BlockType.Block2x4),
    new(Algorithm.Rgb16, 2, 2, BlockType.Block2x2),
    new(Algorithm.Rgb16, 2, 2, BlockType.Block2x2),
    new(Algorithm.None, 4, 4, BlockType.Block4x4),
    new(Algorithm.Rgb24, 4, 4, BlockType.Block4x4),
    new(Algorithm.None, 4, 2, BlockType.Block4x2),
    new(Algorithm.Rgb24, 4, 2, BlockType.Block4x2),
    new(Algorithm.None, 2, 4, BlockType.Block2x4),
    new(Algorithm.Rgb24, 2, 4, BlockType.Block2x4),
    new(Algorithm.None, 2, 2, BlockType.Block2x2),
    new(Algorithm.Rgb24, 2, 2, BlockType.Block2x2),
  ];

  /// <summary>A frame header after its XOR scrambling has been undone.</summary>
  public readonly record struct Header(
    int HeaderSize,
    int Compression,
    int DeltaSet,
    int VectorTable,
    int YSize,
    int XSize,
    int Checksum,
    int Version,
    int HeaderType,
    int Flags,
    int Control) {

    /// <summary>The flags the decoder acts on: version 2 type 2/3 headers state them, everything else is a key frame.</summary>
    public int EffectiveFlags {
      get {
        if (this.Version < 2 || this.HeaderType < 2)
          return FlagKeyFrame;

        var flags = this.Flags;
        if ((flags & FlagInterFrame) == 0)
          flags |= FlagKeyFrame;
        return flags;
      }
    }
  }

  /// <summary>Reads and unscrambles a frame header.</summary>
  /// <exception cref="InvalidDataException">The header is malformed or does not fit the packet.</exception>
  public static Header ReadHeader(ReadOnlySpan<byte> data) {
    if (data.Length == 0)
      throw new InvalidDataException("A TrueMotion 1 packet must start with its header-size byte.");

    if (data[0] < 0x10)
      throw new InvalidDataException($"A TrueMotion 1 header states the invalid header-size byte 0x{data[0]:X2}.");

    var headerSize = ((data[0] >> 5) | (data[0] << 3)) & 0x7f;
    if (headerSize + 1 > data.Length)
      throw new InvalidDataException(
        $"A TrueMotion 1 header of {headerSize} bytes does not fit a packet of {data.Length} bytes.");

    // The header is stored as a chain of XORs; the last link reaches the first byte after it.
    Span<byte> header = stackalloc byte[128];
    header.Clear();
    for (var i = 1; i < headerSize; ++i)
      header[i - 1] = (byte)(data[i] ^ data[i + 1]);

    return new(
      HeaderSize: headerSize,
      Compression: header[0],
      DeltaSet: header[1],
      VectorTable: header[2],
      YSize: BinaryPrimitives.ReadUInt16LittleEndian(header[3..]),
      XSize: BinaryPrimitives.ReadUInt16LittleEndian(header[5..]),
      Checksum: BinaryPrimitives.ReadUInt16LittleEndian(header[7..]),
      Version: header[9],
      HeaderType: header[10],
      Flags: header[11],
      Control: header[12]);
  }

  /// <summary>
  /// Writes <paramref name="header"/> scrambled in front of the payload's first byte (<paramref name="firstPayloadByte"/>),
  /// which the XOR chain's last link shares with the payload.
  /// </summary>
  /// <remarks>
  /// The size byte stores the header size rotated three bits and must be at least 0x10; a size whose
  /// low three bits are not all zero meets that, which is why the encoder uses 17.
  /// </remarks>
  public static void WriteHeader(Span<byte> destination, in Header header, byte firstPayloadByte) {
    var size = header.HeaderSize;
    if (size is < 14 or > 127 || (size & 7) == 0)
      throw new ArgumentOutOfRangeException(nameof(header), $"a TrueMotion 1 header of {size} bytes cannot be stored.");

    Span<byte> plain = stackalloc byte[size - 1];
    plain.Clear();
    plain[0] = (byte)header.Compression;
    plain[1] = (byte)header.DeltaSet;
    plain[2] = (byte)header.VectorTable;
    BinaryPrimitives.WriteUInt16LittleEndian(plain[3..], (ushort)header.YSize);
    BinaryPrimitives.WriteUInt16LittleEndian(plain[5..], (ushort)header.XSize);
    BinaryPrimitives.WriteUInt16LittleEndian(plain[7..], (ushort)header.Checksum);
    plain[9] = (byte)header.Version;
    plain[10] = (byte)header.HeaderType;
    plain[11] = (byte)header.Flags;
    plain[12] = (byte)header.Control;

    destination[0] = (byte)(((size & 7) << 5) | (size >> 3));
    destination[size] = firstPayloadByte;
    for (var i = size - 1; i >= 1; --i)
      destination[i] = (byte)(destination[i + 1] ^ plain[i - 1]);
  }

  /// <summary>The four predictor tables a delta set and a vector table produce.</summary>
  public sealed class PredictorTables {
    public readonly uint[] Luma = new uint[TableSize];
    public readonly uint[] Chroma = new uint[TableSize];
    public readonly uint[] FatLuma = new uint[TableSize];
    public readonly uint[] FatChroma = new uint[TableSize];

    private readonly short[] _ydt = new short[8];
    private readonly short[] _cdt = new short[8];
    private readonly short[] _fatYdt = new short[8];
    private readonly short[] _fatCdt = new short[8];

    /// <summary>FFmpeg's <c>select_delta_tables</c>: a set above three keeps the deltas already loaded.</summary>
    public void SelectDeltas(int deltaSet) {
      if (deltaSet > 3)
        return;

      TrueMotion1Tables.LumaDeltas[deltaSet].CopyTo(this._ydt, 0);
      TrueMotion1Tables.ChromaDeltas[deltaSet].CopyTo(this._cdt, 0);
      TrueMotion1Tables.FatLumaDeltas[deltaSet].CopyTo(this._fatYdt, 0);
      TrueMotion1Tables.FatChromaDeltas[deltaSet].CopyTo(this._fatCdt, 0);

      for (var i = 0; i < 8; ++i)
        this._ydt[i] = (short)((short)(this._ydt[i] & unchecked((short)0xFFFE)) / 2);
    }

    public void Generate(ReadOnlySpan<byte> vectorTable, Algorithm algorithm) {
      var at = 0;
      for (var i = 0; i < TableSize; i += 4) {
        var length = vectorTable[at++] / 2;
        int j;
        for (j = 0; j < length; ++j) {
          var pair = vectorTable[at++];
          int p1 = pair >> 4, p2 = pair & 0xf;
          if (algorithm == Algorithm.Rgb24) {
            this.Luma[i + j] = 0xfffffffe & _Luma24(p1, p2, this._ydt);
            this.Chroma[i + j] = 0xfffffffe & _Chroma24(p1, p2, this._cdt);
            this.FatLuma[i + j] = 0xfffffffe & _Luma24(p1, p2, this._fatYdt);
            this.FatChroma[i + j] = 0xfffffffe & _Chroma24(p1, p2, this._fatCdt);
          } else {
            this.Luma[i + j] = 0xfffffffe & _Luma15(p1, p2, this._ydt);
            this.Chroma[i + j] = 0xfffffffe & _Chroma15(p1, p2, this._cdt);
          }
        }

        this.Luma[i + j - 1] |= 1;
        this.Chroma[i + j - 1] |= 1;
        if (algorithm == Algorithm.Rgb24) {
          this.FatLuma[i + j - 1] |= 1;
          this.FatChroma[i + j - 1] |= 1;
        }
      }
    }

    // FFmpeg's make_ydt15_entry for a little-endian host: p1 is the left pixel of the pair.
    private static uint _Luma15(int p1, int p2, short[] ydt) {
      var lo = ydt[p1];
      var low = lo + lo * 32 + lo * 1024;
      var hi = ydt[p2];
      var high = hi + hi * 32 + hi * 1024;
      return unchecked(((uint)low + (uint)high * 65536u) * 2u);
    }

    private static uint _Chroma15(int p1, int p2, short[] cdt) {
      var lo = cdt[p2] + cdt[p1] * 1024;
      return unchecked(((uint)lo + (uint)lo * 65536u) * 2u);
    }

    private static uint _Luma24(int p1, int p2, short[] ydt) {
      int lo = ydt[p1], hi = ydt[p2];
      return unchecked((uint)(lo + hi * 256 + hi * 65536) * 2u);
    }

    private static uint _Chroma24(int p1, int p2, short[] cdt) {
      int b = cdt[p2], r = cdt[p1] * 65536;
      return unchecked((uint)(b + r) * 2u);
    }
  }

  /// <summary>Which vector table a header selects, or throws for an invalid one.</summary>
  public static byte[] SelectVectorTable(in Header header) {
    if ((header.Compression & 1) != 0 && header.HeaderType != 0)
      return TrueMotion1Tables.VectorTables[0];

    if (header.VectorTable is > 0 and < 4)
      return TrueMotion1Tables.VectorTables[header.VectorTable - 1];

    throw new InvalidDataException($"A TrueMotion 1 header names the invalid vector table {header.VectorTable}.");
  }
}
