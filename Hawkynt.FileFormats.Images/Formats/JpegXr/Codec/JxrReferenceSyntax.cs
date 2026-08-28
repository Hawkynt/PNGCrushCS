using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.JpegXr.Codec;

/// <summary>
/// Bitstream syntax shared by the JPEG XR reference decoder/encoder. This follows the Microsoft
/// JXRLib 1.0 bitstream implementation (ITU-T T.832 / ISO/IEC 29199-2), not the synthetic header
/// previously used by this project.
/// </summary>
internal static class JxrReferenceSyntax {

  internal const int CodecVersion = 1;
  internal const int SubVersionOriginal = 0;
  internal const int SubVersionSoftTiles = 1;
  internal const int SubVersionHardTiles = 9;
  internal const int MacroblockSize = 16;
  internal const int MaxTiles = 16;
  internal const int LogMaxTiles = 4;

  internal enum BitstreamFormat : byte { Spatial = 0, Frequency = 1 }
  internal enum Orientation : byte {
    None = 0, FlipV = 1, FlipH = 2, FlipVH = 3,
    RotateCw = 4, RotateCwFlipV = 5, RotateCwFlipH = 6, RotateCwFlipVH = 7
  }
  internal enum Overlap : byte { None = 0, One = 1, Two = 2 }
  internal enum ColorFormat : byte {
    YOnly = 0, Yuv420 = 1, Yuv422 = 2, Yuv444 = 3, Cmyk = 4,
    NComponent = 6, Rgb = 7, Rgbe = 8, Max = 9
  }
  internal enum BitDepth : byte {
    One = 0, Eight = 1, Sixteen = 2, SixteenS = 3, SixteenF = 4,
    ThirtyTwo = 5, ThirtyTwoS = 6, ThirtyTwoF = 7, Five = 8, Ten = 9,
    FiveSixFive = 10, OneAlt = 15
  }
  internal enum Subband : byte { All = 0, NoFlexbits = 1, NoHighpass = 2, DcOnly = 3, Isolated = 4 }

  internal readonly record struct QuantizerHeader(byte ChannelMode, byte[] Indices);

  internal sealed class PlaneHeader {
    public required ColorFormat InternalColorFormat { get; init; }
    public required bool ScaledArithmetic { get; init; }
    public required Subband Subband { get; init; }
    public required int ChannelCount { get; init; }
    public byte ChromaCenteringX { get; init; }
    public byte ChromaCenteringY { get; init; }
    public byte MantissaOrShift { get; init; }
    public sbyte ExponentBias { get; init; }
    public required bool DcUniform { get; init; }
    public QuantizerHeader? DcQuantizer { get; init; }
    public bool LpUsesDc { get; init; }
    public bool LpUniform { get; init; }
    public QuantizerHeader? LpQuantizer { get; init; }
    public bool HpUsesLp { get; init; }
    public bool HpUniform { get; init; }
    public QuantizerHeader? HpQuantizer { get; init; }
  }

  internal sealed class CodestreamHeader {
    public required int Version { get; init; }
    public required int SubVersion { get; init; }
    public required bool TilingPresent { get; init; }
    public required BitstreamFormat BitstreamFormat { get; init; }
    public required Orientation Orientation { get; init; }
    public required bool HasIndexTable { get; init; }
    public required Overlap Overlap { get; init; }
    public required bool AbbreviatedHeader { get; init; }
    public required bool Inscribed { get; init; }
    public required bool TrimFlexbits { get; init; }
    public required bool TileStretch { get; init; }
    public required bool RedBlueSwapped { get; init; }
    public required bool HasInterleavedAlpha { get; init; }
    public required ColorFormat ExternalColorFormat { get; init; }
    public required BitDepth ExternalBitDepth { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public int ExtraPixelsTop { get; init; }
    public int ExtraPixelsLeft { get; init; }
    public int ExtraPixelsBottom { get; init; }
    public int ExtraPixelsRight { get; init; }
    public required int[] TileX { get; init; }
    public required int[] TileY { get; init; }
    public required PlaneHeader Plane { get; init; }
    public required int[] PacketOffsets { get; init; }
    public required int PacketBodyOffset { get; init; }
    public required int HeaderByteCount { get; init; }

    public int MacroblocksWide => (Width + ExtraPixelsLeft + ExtraPixelsRight + 15) >> 4;
    public int MacroblocksHigh => (Height + ExtraPixelsTop + ExtraPixelsBottom + 15) >> 4;
    public int TileColumns => TileX.Length;
    public int TileRows => TileY.Length;
    public int SubbandCount => Plane.Subband switch {
      Subband.DcOnly => 1,
      Subband.NoHighpass => 2,
      Subband.NoFlexbits => 3,
      _ => 4
    };
  }

  internal sealed class BitReader {
    private readonly ReadOnlyMemory<byte> _memory;
    private int _byteOffset;
    private int _bitOffset;

    public BitReader(ReadOnlyMemory<byte> data, int byteOffset = 0) {
      if ((uint)byteOffset > (uint)data.Length)
        throw new ArgumentOutOfRangeException(nameof(byteOffset));
      _memory = data;
      _byteOffset = byteOffset;
    }

    public int ByteOffset => _byteOffset;
    public int BitOffset => _bitOffset;
    public int BitsRead => checked(_byteOffset * 8 + _bitOffset);
    public bool EndOfData => _byteOffset >= _memory.Length;

    public uint ReadBits(int count) {
      if ((uint)count > 32u)
        throw new ArgumentOutOfRangeException(nameof(count));
      if (count == 0)
        return 0;
      if (BitsRead > _memory.Length * 8 - count)
        throw new EndOfStreamException("Unexpected end of JPEG XR codestream.");

      var result = 0u;
      var span = _memory.Span;
      while (count > 0) {
        var available = 8 - _bitOffset;
        var take = Math.Min(count, available);
        var shift = available - take;
        var mask = (1u << take) - 1u;
        result = (result << take) | ((uint)(span[_byteOffset] >> shift) & mask);
        _bitOffset += take;
        count -= take;
        if (_bitOffset == 8) {
          _bitOffset = 0;
          ++_byteOffset;
        }
      }
      return result;
    }

    public bool ReadBool() => ReadBits(1) != 0;
    public byte ReadByte() => (byte)ReadBits(8);
    public ushort ReadUInt16() => (ushort)ReadBits(16);

    public void AlignToByte() {
      if (_bitOffset == 0)
        return;
      _bitOffset = 0;
      ++_byteOffset;
    }
  }

  internal static CodestreamHeader Parse(ReadOnlyMemory<byte> data) {
    if (data.Length < 16)
      throw new InvalidDataException("JPEG XR codestream is too short.");
    var span = data.Span;
    if (!span[..7].SequenceEqual("WMPHOTO"u8))
      throw new InvalidDataException("JPEG XR codestream does not start with WMPHOTO.");

    // JXRLib consumes eight signature bytes and only constrains the first seven.
    var r = new BitReader(data, 8);
    var version = (int)r.ReadBits(4);
    if (version != CodecVersion)
      throw new InvalidDataException($"Unsupported JPEG XR codec version {version}.");
    var subVersion = (int)r.ReadBits(4);
    if (subVersion is not (SubVersionOriginal or SubVersionSoftTiles or SubVersionHardTiles))
      throw new InvalidDataException($"Unsupported JPEG XR codec subversion {subVersion}.");

    var tiling = r.ReadBool();
    var bitstreamFormat = (BitstreamFormat)r.ReadBits(1);
    var orientation = (Orientation)r.ReadBits(3);
    var indexTable = r.ReadBool();
    var overlapValue = r.ReadBits(2);
    if (overlapValue > 2)
      throw new InvalidDataException("Reserved JPEG XR overlap mode.");
    var overlap = (Overlap)overlapValue;
    var abbreviated = r.ReadBool();
    _ = r.ReadBits(1); // internal coefficient storage width; JXRLib decodes into 32-bit coefficients.
    var inscribed = r.ReadBool();
    var trimFlex = r.ReadBool();
    var tileStretch = r.ReadBool();
    var rbSwapped = r.ReadBool();
    _ = r.ReadBits(1); // reserved
    var alpha = r.ReadBool();
    var externalColor = _ReadExternalColor(r.ReadBits(4));
    var externalDepth = _ReadBitDepth(r.ReadBits(4));
    if (externalDepth == BitDepth.OneAlt)
      externalDepth = BitDepth.One;

    var dimensionBits = abbreviated ? 16 : 32;
    var width = checked((int)r.ReadBits(dimensionBits) + 1);
    var height = checked((int)r.ReadBits(dimensionBits) + 1);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException("Invalid JPEG XR dimensions.");

    var extraTop = 0;
    var extraLeft = 0;
    var extraBottom = 0;
    var extraRight = 0;
    if (!inscribed) {
      if ((width & 15) != 0) extraRight = 16 - (width & 15);
      if ((height & 15) != 0) extraBottom = 16 - (height & 15);
    }

    var slicesV = 0;
    var slicesH = 0;
    if (tiling) {
      slicesV = checked((int)r.ReadBits(LogMaxTiles));
      slicesH = checked((int)r.ReadBits(LogMaxTiles));
    }
    if (slicesV >= MaxTiles || slicesH >= MaxTiles)
      throw new InvalidDataException("JPEG XR tile count exceeds the reference profile limit.");
    if (!indexTable && (bitstreamFormat == BitstreamFormat.Frequency || slicesV + slicesH > 0))
      throw new InvalidDataException("JPEG XR frequency/tiled streams require an index table.");

    var tileX = new int[slicesV + 1];
    var tileY = new int[slicesH + 1];
    var tileBits = abbreviated ? 8 : 16;
    for (var i = 0; i < slicesV; ++i)
      tileX[i + 1] = checked(tileX[i] + (int)r.ReadBits(tileBits));
    for (var i = 0; i < slicesH; ++i)
      tileY[i + 1] = checked(tileY[i] + (int)r.ReadBits(tileBits));

    if (tileStretch)
      for (var i = 0; i < (slicesV + 1) * (slicesH + 1); ++i)
        _ = r.ReadBits(8);

    if (inscribed) {
      extraTop = (int)r.ReadBits(6);
      extraLeft = (int)r.ReadBits(6);
      extraBottom = (int)r.ReadBits(6);
      extraRight = (int)r.ReadBits(6);
    }

    var codedWidth = checked(width + extraLeft + extraRight);
    var codedHeight = checked(height + extraTop + extraBottom);
    if ((codedWidth & 15) != 0 || (codedHeight & 15) != 0)
      throw new InvalidDataException("JPEG XR coded dimensions are not macroblock aligned.");

    r.AlignToByte();
    var plane = _ReadPlaneHeader(r, externalDepth);

    // Interleaved alpha is represented by a second image-plane header in the same codestream.
    // The current public JpegXrFile model cannot expose alpha, but parsing it here is essential to
    // advance to the index table correctly for files carrying interleaved alpha.
    if (alpha) {
      _ = _ReadPlaneHeader(r, externalDepth);
    }
    r.AlignToByte();

    var subbandCount = plane.Subband switch {
      Subband.DcOnly => 1,
      Subband.NoHighpass => 2,
      Subband.NoFlexbits => 3,
      _ => 4
    };
    var numBitIo = !indexTable ? 0
      : bitstreamFormat == BitstreamFormat.Spatial ? slicesV + 1
      : (slicesV + 1) * subbandCount;
    var indexCount = numBitIo * (slicesH + 1);
    var packetOffsets = Array.Empty<int>();
    if (numBitIo > 0) {
      if (r.ReadBits(16) != 1)
        throw new InvalidDataException("Invalid JPEG XR index-table marker.");
      packetOffsets = new int[indexCount];
      for (var i = 0; i < packetOffsets.Length; ++i)
        packetOffsets[i] = checked((int)_ReadVlWordEsc(r, out _));
    }

    var declaredHeaderSize = checked((int)_ReadVlWordEsc(r, out _));
    r.AlignToByte();
    var packetBodyOffset = r.ByteOffset + declaredHeaderSize;
    if ((uint)packetBodyOffset > (uint)data.Length)
      throw new InvalidDataException("JPEG XR header size points outside the codestream.");

    return new() {
      Version = version,
      SubVersion = subVersion,
      TilingPresent = tiling,
      BitstreamFormat = bitstreamFormat,
      Orientation = orientation,
      HasIndexTable = indexTable,
      Overlap = overlap,
      AbbreviatedHeader = abbreviated,
      Inscribed = inscribed,
      TrimFlexbits = trimFlex,
      TileStretch = tileStretch,
      RedBlueSwapped = rbSwapped,
      HasInterleavedAlpha = alpha,
      ExternalColorFormat = externalColor,
      ExternalBitDepth = externalDepth,
      Width = width,
      Height = height,
      ExtraPixelsTop = extraTop,
      ExtraPixelsLeft = extraLeft,
      ExtraPixelsBottom = extraBottom,
      ExtraPixelsRight = extraRight,
      TileX = tileX,
      TileY = tileY,
      Plane = plane,
      PacketOffsets = packetOffsets,
      PacketBodyOffset = packetBodyOffset,
      HeaderByteCount = packetBodyOffset,
    };
  }

  private static PlaneHeader _ReadPlaneHeader(BitReader r, BitDepth externalDepth) {
    var internalColor = _ReadInternalColor(r.ReadBits(3));
    var scaled = r.ReadBool();
    var subbandValue = r.ReadBits(4);
    if (subbandValue > (uint)Subband.Isolated)
      throw new InvalidDataException("Invalid JPEG XR subband mode.");
    var subband = (Subband)subbandValue;
    if (subband == Subband.Isolated)
      throw new NotSupportedException("JPEG XR isolated subband streams are not image-decodable frames.");

    var channels = internalColor switch {
      ColorFormat.YOnly => 1,
      ColorFormat.Yuv420 or ColorFormat.Yuv422 or ColorFormat.Yuv444 => 3,
      ColorFormat.Cmyk => 4,
      ColorFormat.NComponent => checked((int)r.ReadBits(4) + 1),
      _ => throw new InvalidDataException($"Invalid JPEG XR internal colour format {internalColor}.")
    };

    byte chromaX = 0, chromaY = 0;
    switch (internalColor) {
      case ColorFormat.Yuv420:
        _ = r.ReadBits(1);
        chromaX = (byte)r.ReadBits(3);
        _ = r.ReadBits(1);
        chromaY = (byte)r.ReadBits(3);
        break;
      case ColorFormat.Yuv422:
        _ = r.ReadBits(1);
        chromaX = (byte)r.ReadBits(3);
        _ = r.ReadBits(4);
        break;
      case ColorFormat.Yuv444:
        _ = r.ReadBits(4);
        _ = r.ReadBits(4);
        break;
      case ColorFormat.NComponent:
        _ = r.ReadBits(4);
        break;
    }

    byte shift = 0;
    sbyte expBias = 0;
    if (externalDepth is BitDepth.Sixteen or BitDepth.SixteenS or BitDepth.ThirtyTwo or BitDepth.ThirtyTwoS)
      shift = (byte)r.ReadBits(8);
    else if (externalDepth == BitDepth.ThirtyTwoF) {
      shift = (byte)r.ReadBits(8);
      expBias = unchecked((sbyte)r.ReadBits(8));
    }

    var dcExplicit = r.ReadBool();
    QuantizerHeader? dc = dcExplicit ? _ReadQuantizer(r, channels) : null;
    var dcUniform = !dcExplicit;

    var lpUsesDc = true;
    var lpUniform = true;
    QuantizerHeader? lp = null;
    var hpUsesLp = true;
    var hpUniform = true;
    QuantizerHeader? hp = null;

    if (subband != Subband.DcOnly) {
      lpUsesDc = r.ReadBool();
      if (!lpUsesDc) {
        var lpExplicit = r.ReadBool();
        lp = lpExplicit ? _ReadQuantizer(r, channels) : null;
        lpUniform = !lpExplicit;
      }

      if (subband != Subband.NoHighpass) {
        hpUsesLp = r.ReadBool();
        if (!hpUsesLp) {
          var hpExplicit = r.ReadBool();
          hp = hpExplicit ? _ReadQuantizer(r, channels) : null;
          hpUniform = !hpExplicit;
        }
      }
    }

    r.AlignToByte();
    return new() {
      InternalColorFormat = internalColor,
      ScaledArithmetic = scaled,
      Subband = subband,
      ChannelCount = channels,
      ChromaCenteringX = chromaX,
      ChromaCenteringY = chromaY,
      MantissaOrShift = shift,
      ExponentBias = expBias,
      DcUniform = dcUniform,
      DcQuantizer = dc,
      LpUsesDc = lpUsesDc,
      LpUniform = lpUniform,
      LpQuantizer = lp,
      HpUsesLp = hpUsesLp,
      HpUniform = hpUniform,
      HpQuantizer = hp,
    };
  }

  private static QuantizerHeader _ReadQuantizer(BitReader r, int channels) {
    var mode = channels > 1 ? (byte)r.ReadBits(2) : (byte)0;
    var count = mode switch {
      0 => 1,
      1 => Math.Min(2, channels),
      _ => channels
    };
    var indexes = new byte[count];
    for (var i = 0; i < indexes.Length; ++i)
      indexes[i] = (byte)r.ReadBits(8);
    return new(mode, indexes);
  }

  private static ulong _ReadVlWordEsc(BitReader r, out byte escape) {
    var first = r.ReadByte();
    escape = 0;
    if (first is 0xFD or 0xFE or 0xFF) {
      escape = first;
      return 0;
    }
    if (first < 0xFB)
      return (ulong)((first << 8) | r.ReadByte());

    var selector = first - 0xFB;
    ulong value = 0;
    if (selector != 0) {
      value = r.ReadBits(16);
      value = (value | r.ReadBits(16)) << 16;
      value <<= 16;
    }
    value |= (ulong)r.ReadBits(16) << 16;
    value |= r.ReadBits(16);
    return value;
  }

  private static ColorFormat _ReadExternalColor(uint value) => value switch {
    0 => ColorFormat.YOnly,
    1 => ColorFormat.Yuv420,
    2 => ColorFormat.Yuv422,
    3 => ColorFormat.Yuv444,
    4 => ColorFormat.Cmyk,
    6 => ColorFormat.NComponent,
    7 => ColorFormat.Rgb,
    8 => ColorFormat.Rgbe,
    9 => ColorFormat.Max,
    _ => throw new InvalidDataException($"Reserved JPEG XR external colour format {value}.")
  };

  private static ColorFormat _ReadInternalColor(uint value) => value switch {
    0 => ColorFormat.YOnly,
    1 => ColorFormat.Yuv420,
    2 => ColorFormat.Yuv422,
    3 => ColorFormat.Yuv444,
    4 => ColorFormat.Cmyk,
    6 => ColorFormat.NComponent,
    _ => throw new InvalidDataException($"Reserved JPEG XR internal colour format {value}.")
  };

  private static BitDepth _ReadBitDepth(uint value) => value switch {
    0 => BitDepth.One,
    1 => BitDepth.Eight,
    2 => BitDepth.Sixteen,
    3 => BitDepth.SixteenS,
    4 => BitDepth.SixteenF,
    5 => BitDepth.ThirtyTwo,
    6 => BitDepth.ThirtyTwoS,
    7 => BitDepth.ThirtyTwoF,
    8 => BitDepth.Five,
    9 => BitDepth.Ten,
    10 => BitDepth.FiveSixFive,
    15 => BitDepth.OneAlt,
    _ => throw new InvalidDataException($"Reserved JPEG XR bit depth {value}.")
  };
}
