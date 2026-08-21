using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs.DnxHd;

/// <summary>
/// The header of one coding unit — SMPTE ST 2019-1:2016, 7.2.
/// </summary>
/// <remarks>
/// A fixed layout at fixed byte offsets, unlike the bit-packed headers of most codecs: Table 8 gives
/// every data region a starting offset and a length, and the regions this decoder does not use —
/// timecode, user data, the reserved stretches — are stepped over rather than parsed. For HD rasters
/// the header is 640 bytes; for the resolution-independent profile it grows by four bytes for every
/// sixteen lines past 1088, because the table of macroblock scan indices at the end of it grows with
/// the picture.
/// <para/>
/// <b>The header states the raster, and it is believed over the compression identifier.</b> Annex C
/// lists a raster for each of the HD identifiers, but Table C.2 lists none for the
/// resolution-independent ones — their whole point is that the raster is free — so the samples per
/// line and active lines per frame are read from 7.2.3 in every case, and the identifier is used for
/// the two things the header does not carry: which quantisation weights and which code tables.
/// </remarks>
internal sealed class DnxHdFrameHeader {

  /// <summary>The five bytes a coding unit begins with, 7.2.1.</summary>
  /// <remarks>
  /// The first four are the header size, which is 0x00000280 for both HD header versions; the fifth
  /// is the header version itself. So the prefix of an HD coding unit is a constant, and that is what
  /// a demuxer's frame boundary is checked against.
  /// </remarks>
  private const int _HD_HEADER_SIZE = 0x280;

  private const int _SCAN_INDICES_AT = 0x170;

  internal required int HeaderSize { get; init; }
  internal required int HeaderVersion { get; init; }
  internal required int CompressionIdValue { get; init; }
  internal required DnxHdCompressionId Compression { get; init; }

  /// <summary>Samples per line, 7.2.3. A whole number of macroblocks is coded whatever this says.</summary>
  internal required int SamplesPerLine { get; init; }

  /// <summary>Active lines per frame, 7.2.3.</summary>
  internal required int ActiveLines { get; init; }

  /// <summary>Bits per sample: 8, 10 or 12, from the SBD field of 7.2.3.</summary>
  internal required int BitDepth { get; init; }

  /// <summary>Whether the source was interlaced, from the SST field of 7.2.3.</summary>
  internal required bool InterlacedSource { get; init; }

  /// <summary>Whether this coding unit holds a whole frame, from the FFE field of 7.2.5.</summary>
  internal required bool FrameEncoded { get; init; }

  /// <summary>The chroma sampling, from the SSC field of 7.2.5: 0 is 4:2:2, 1 is 4:2:0, 2 is 4:4:4.</summary>
  internal required int SubSampling { get; init; }

  /// <summary>Whether the channels are RGB rather than Y′CbCr, from the CLF field of 7.2.5.</summary>
  internal required bool Rgb { get; init; }

  /// <summary>The colour volume, from the CLV field of 7.2.5.</summary>
  internal required int ColorVolume { get; init; }

  /// <summary>Whether macroblocks may choose field or frame coding, from the MACF field of 7.2.2.</summary>
  internal required bool AdaptiveMacroblocks { get; init; }

  /// <summary>Whether an alpha channel is present, from the ALP field of 7.2.2.</summary>
  internal required bool Alpha { get; init; }

  /// <summary>The byte offset of each macroblock scan line's first macroblock, 7.2.11.</summary>
  /// <remarks>
  /// Relative to the start of the compressed payload, which begins where the header ends. Every scan
  /// line is reached through this table rather than by running on from the one before, which is what
  /// 7.3.1's four-byte alignment of each scan line is for.
  /// </remarks>
  internal required int[] ScanIndices { get; init; }

  /// <summary>The width of the coded picture in macroblocks.</summary>
  internal int WidthInMacroblocks => (this.SamplesPerLine + 15) / 16;

  /// <summary>The height of the coded picture in macroblocks, which the header states outright.</summary>
  internal int HeightInMacroblocks => this.ScanIndices.Length;

  internal static DnxHdFrameHeader Parse(ReadOnlySpan<byte> unit) {
    if (unit.Length < _SCAN_INDICES_AT + 4)
      throw new InvalidDataException(
        $"A VC-3 coding unit header is at least {_SCAN_INDICES_AT + 4} bytes and this packet holds {unit.Length}.");

    var headerSize = (int)BinaryPrimitives.ReadUInt32BigEndian(unit);
    var version = unit[4];

    // 7.2.1: version 1 and 2 are the HD profile and state a header of exactly 0x280 bytes; version 3
    // is the resolution-independent profile, whose header grows with the picture and whose size is
    // therefore the one thing the prefix has to carry.
    if (version is < 1 or > 3)
      throw new NotSupportedException(
        $"This VC-3 coding unit states header version {version}. SMPTE ST 2019-1 defines 1, 2 and 3.");

    if (version < 3 && headerSize != _HD_HEADER_SIZE)
      throw new InvalidDataException(
        $"A VC-3 coding unit of header version {version} states a header of {headerSize} bytes, where 7.2.1 fixes it at {_HD_HEADER_SIZE}.");

    if (headerSize < _SCAN_INDICES_AT + 4 || headerSize > unit.Length)
      throw new InvalidDataException(
        $"A VC-3 coding unit states a header of {headerSize} bytes, which is not within the {unit.Length} bytes of the packet.");

    var codingControlA = unit[5..8];
    var codingControlB = unit[0x2C];

    var compressionId = (int)BinaryPrimitives.ReadUInt32BigEndian(unit[0x28..]);
    var compression = DnxHdCompressionId.Find(compressionId)
      ?? throw new NotSupportedException(
        $"This VC-3 coding unit states compression ID {compressionId}, which is in neither Table C.1 nor Table C.2 of SMPTE ST 2019-1. The identifier chooses the quantisation weights and the code tables, so a frame naming an unknown one cannot be decoded and is not guessed at.");

    // 7.2.3, SBD: 1 is eight bits, 2 is ten, 3 is twelve.
    var depthCode = unit[0x21] >> 5;
    var depth = depthCode switch {
      1 => 8,
      2 => 10,
      3 => 12,
      _ => throw new NotSupportedException(
        $"This VC-3 coding unit states sample bit depth code {depthCode}, which SMPTE ST 2019-1 7.2.3 does not define."),
    };

    var scanLineCount = BinaryPrimitives.ReadUInt16BigEndian(unit[0x16C..]);
    if (scanLineCount <= 0 || _SCAN_INDICES_AT + scanLineCount * 4 > headerSize)
      throw new InvalidDataException(
        $"A VC-3 coding unit states {scanLineCount} macroblock scan lines, whose indices do not fit in its {headerSize}-byte header.");

    var indices = new int[scanLineCount];
    for (var i = 0; i < scanLineCount; ++i)
      indices[i] = (int)BinaryPrimitives.ReadUInt32BigEndian(unit[(_SCAN_INDICES_AT + i * 4)..]);

    return new() {
      HeaderSize = headerSize,
      HeaderVersion = version,
      CompressionIdValue = compressionId,
      Compression = compression,
      SamplesPerLine = BinaryPrimitives.ReadUInt16BigEndian(unit[0x1A..]),
      ActiveLines = BinaryPrimitives.ReadUInt16BigEndian(unit[0x18..]),
      BitDepth = depth,
      InterlacedSource = ((unit[0x22] >> 2) & 1) != 0,
      FrameEncoded = (codingControlB & 0x80) != 0,
      SubSampling = (codingControlB >> 5) & 3,
      ColorVolume = (codingControlB >> 1) & 3,
      Rgb = (codingControlB & 1) != 0,
      AdaptiveMacroblocks = ((codingControlA[1] >> 5) & 1) != 0,
      Alpha = (codingControlA[2] & 1) != 0,
      ScanIndices = indices,
    };
  }
}
