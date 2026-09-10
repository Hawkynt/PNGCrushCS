using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs.DnxHd;

/// <summary>Encodes one progressive 8-bit 4:2:2 DNxHR SQ coding unit (compression ID 1273).</summary>
/// <remarks>
/// This is the inverse of <see cref="DnxHdCodingUnitDecoder"/> at the syntax layer. The bitstream
/// choices are intentionally narrow: RI header version 3, compression ID 1273, CBR, no alpha, one
/// global quantisation scale factor and the Y′CbCr block arrangement of Table 5. The standard permits
/// finer per-macroblock rate control; using one value everywhere is simpler and still conforming.
/// </remarks>
internal static class DnxHdCodingUnitEncoder {

  private const int _COMPRESSION_ID = 1273;
  private const int _MINIMUM_HEADER_SIZE = 0x280;
  private const int _SCAN_INDICES_AT = 0x170;
  private const int _REFERENCE_MACROBLOCKS = 8160;
  private const int _REFERENCE_FRAME_SIZE = 606208;
  private const int _MINIMUM_FRAME_SIZE = 8192;
  private const int _FRAME_SIZE_QUANTUM = 4096;
  private const int _MAX_QUANTISATION_SCALE = 2047;
  private const int _INITIAL_QUANTISATION_SCALE = 4;
  private const int _MAX_AMPLITUDE = 1024;
  private const int _AMPLITUDE_INDEX_BITS = 4;

  private static readonly byte[] _EofSignature = [0x60, 0x0D, 0xC0, 0xDE];

  private static readonly (int Component, int X, int Y)[] _Blocks422 = [
    (0, 0, 0), (0, 8, 0), (1, 0, 0), (2, 0, 0),
    (0, 0, 8), (0, 8, 8), (1, 0, 8), (2, 0, 8),
  ];

  private static readonly DnxHdCompressionId _Compression =
    DnxHdCompressionId.Find(_COMPRESSION_ID)
    ?? throw new InvalidOperationException($"Compression ID {_COMPRESSION_ID} is missing from the VC-3 table.");

  private static readonly DnxHdVlcTable _Dc =
    DnxHdVlcTable.From(
      DnxHdVlcTables.DcLengths[_Compression.VlcGroup],
      DnxHdVlcTables.DcBitCounts[_Compression.VlcGroup]);

  private static readonly DnxHdVlcTable _Amplitude =
    DnxHdVlcTable.From(
      DnxHdVlcTables.AmplitudeLengths[_Compression.VlcGroup],
      DnxHdVlcTables.AmplitudeSymbols[_Compression.VlcGroup]);

  private static readonly DnxHdVlcTable _Run =
    DnxHdVlcTable.From(
      DnxHdVlcTables.RunLengths[_Compression.VlcGroup],
      DnxHdVlcTables.RunSymbols[_Compression.VlcGroup]);

  private static readonly byte[] _LumaWeights = DnxHdWeightTables.Luma[_Compression.WeightTable];
  private static readonly byte[] _ChromaWeights = DnxHdWeightTables.Chroma[_Compression.WeightTable];

  /// <summary>Codes a whole RI frame and pads it to the CBR size prescribed by Annex C and 7.1.</summary>
  internal static byte[] Encode(DnxHdPlanes planes, int width, int height) {
    ArgumentNullException.ThrowIfNull(planes);

    var macroblockWidth = (width + 15) / 16;
    var macroblockHeight = (height + 15) / 16;
    var headerSize = Math.Max(_MINIMUM_HEADER_SIZE, _SCAN_INDICES_AT + macroblockHeight * 4);
    var frameSize = _CompressedFrameSize(macroblockWidth, macroblockHeight);
    var payloadCapacity = frameSize - headerSize - _EofSignature.Length;

    if (payloadCapacity <= 0)
      throw new InvalidDataException(
        $"The VC-3 frame-size formula produced {frameSize} bytes, which cannot hold its {headerSize}-byte header.");

    byte[]? compressed = null;
    int[]? scanIndices = null;
    var quantisationScale = _INITIAL_QUANTISATION_SCALE;

    while (true) {
      (compressed, scanIndices) = _EncodeCompressedData(
        planes, macroblockWidth, macroblockHeight, quantisationScale);

      if (compressed.Length <= payloadCapacity)
        break;

      if (quantisationScale == _MAX_QUANTISATION_SCALE)
        throw new InvalidDataException(
          $"A {width}x{height} DNxHR SQ frame needs {compressed.Length} compressed bytes even at qscale "
          + $"{_MAX_QUANTISATION_SCALE}, but the profile permits only {payloadCapacity}.");

      quantisationScale = quantisationScale >= 1024
        ? _MAX_QUANTISATION_SCALE
        : quantisationScale * 2;
    }

    var frame = new byte[frameSize];
    _WriteHeader(frame.AsSpan(0, headerSize), width, height, headerSize, scanIndices);
    compressed.CopyTo(frame, headerSize);
    _EofSignature.CopyTo(frame, frame.Length - _EofSignature.Length);

    return frame;
  }

  /// <summary>
  /// Encodes the compressed macroblock data and records each scan line's byte offset within it.
  /// </summary>
  private static (byte[] Bytes, int[] ScanIndices) _EncodeCompressedData(
    DnxHdPlanes planes, int macroblockWidth, int macroblockHeight, int quantisationScale) {

    var bits = new DnxHdBitWriter();
    var scanIndices = new int[macroblockHeight];

    for (var macroblockY = 0; macroblockY < macroblockHeight; ++macroblockY) {
      scanIndices[macroblockY] = bits.BytePosition;
      Span<int> dcPrediction = stackalloc int[3];

      for (var macroblockX = 0; macroblockX < macroblockWidth; ++macroblockX) {
        bits.Bits(quantisationScale, 11);
        bits.Bit(0); // reserved for 4:2:2 compression IDs

        foreach (var block in _Blocks422)
          _EncodeBlock(
            bits,
            planes,
            block.Component,
            macroblockX,
            macroblockY,
            block.X,
            block.Y,
            quantisationScale,
            dcPrediction);
      }

      // 7.3.1.3: the next macroblock scan line starts at a four-byte address.
      bits.AlignToFourBytes();
    }

    return (bits.ToArray(), scanIndices);
  }

  private static void _EncodeBlock(
    DnxHdBitWriter bits,
    DnxHdPlanes planes,
    int component,
    int macroblockX,
    int macroblockY,
    int blockX,
    int blockY,
    int quantisationScale,
    Span<int> dcPrediction) {

    var plane = planes.Plane(component);
    var stride = planes.PlaneWidth(component);
    var macroblockSampleWidth = component == 0 ? 16 : 8;
    var x = macroblockX * macroblockSampleWidth + blockX;
    var y = macroblockY * 16 + blockY;

    Span<int> coefficients = stackalloc int[64];
    DnxHdForwardDct.Transform(plane, stride, x, y, coefficients);

    var dc = Math.Clamp(coefficients[0], -1024, 1023);
    _WriteDc(bits, dc - dcPrediction[component]);
    dcPrediction[component] = dc;

    var weights = component == 0 ? _LumaWeights : _ChromaWeights;
    var run = 0;

    for (var scan = 1; scan < 64; ++scan) {
      var position = DnxHdScan.RasterPosition[scan];
      var quantised = _Quantise(coefficients[position], weights[position], quantisationScale);
      if (quantised == 0) {
        ++run;
        continue;
      }

      _WriteAc(bits, quantised, run);
      run = 0;
    }

    _Amplitude.Write(bits, DnxHdAmplitude.EndOfBlockSymbol);
  }

  /// <summary>Equation 8.2: truncating quantisation, with DC excluded before this method is reached.</summary>
  private static int _Quantise(int coefficient, int weight, int quantisationScale) {
    var magnitude = Math.Abs(coefficient);
    if (magnitude == 0)
      return 0;

    var quantised = _Compression.InverseQuantisationDivisor * magnitude / (quantisationScale * weight);
    return coefficient < 0 ? -quantised : quantised;
  }

  private static void _WriteDc(DnxHdBitWriter bits, int difference) {
    var magnitude = Math.Abs(difference);
    var bitCount = 0;
    for (var value = magnitude; value != 0; value >>= 1)
      ++bitCount;

    _Dc.Write(bits, bitCount);
    if (bitCount == 0)
      return;

    var value = difference >= 0
      ? difference
      : difference + (1 << bitCount) - 1;

    bits.Bits(value, bitCount);
  }

  private static void _WriteAc(DnxHdBitWriter bits, int coefficient, int run) {
    var magnitude = Math.Abs(coefficient);
    if (magnitude > _MAX_AMPLITUDE)
      throw new InvalidDataException(
        $"A quantised VC-3 coefficient has magnitude {magnitude}; eight-bit amplitude coding can represent at most {_MAX_AMPLITUDE}.");

    var baseAmplitude = ((magnitude - 1) & 63) + 1;
    var amplitudeIndex = (magnitude - 1) >> 6;
    var hasIndex = amplitudeIndex != 0;
    var hasRun = run != 0;

    _Amplitude.Write(bits, DnxHdAmplitude.Symbol(baseAmplitude, hasRun, hasIndex));
    bits.Bit(coefficient < 0 ? 1 : 0);

    if (hasIndex)
      bits.Bits(amplitudeIndex, _AMPLITUDE_INDEX_BITS);

    if (hasRun)
      _Run.Write(bits, run);
  }

  /// <summary>Section 7.1's RI CBR frame-size calculation for CID 1273.</summary>
  private static int _CompressedFrameSize(int macroblockWidth, int macroblockHeight) {
    var macroblocks = checked((long)macroblockWidth * macroblockHeight);
    var size = _REFERENCE_FRAME_SIZE * macroblocks / _REFERENCE_MACROBLOCKS;
    var remainder = size % _FRAME_SIZE_QUANTUM;

    size = remainder >= _FRAME_SIZE_QUANTUM / 2
      ? size + _FRAME_SIZE_QUANTUM - remainder
      : size - remainder;

    return checked((int)Math.Max(size, _MINIMUM_FRAME_SIZE));
  }

  /// <summary>Writes the version-3 RI header defined by 7.2 for progressive 8-bit 4:2:2 Y′CbCr.</summary>
  private static void _WriteHeader(
    Span<byte> header, int width, int height, int headerSize, ReadOnlySpan<int> scanIndices) {

    header.Clear();

    BinaryPrimitives.WriteUInt32BigEndian(header, (uint)headerSize);
    header[4] = 3;       // HVN: RI profile
    header[5] = 0x01;    // CBR, progressive frame
    header[6] = 0x80;    // fixed bit; MACF=0, CRCF=0
    header[7] = 0xA0;    // fixed bits; no alpha

    BinaryPrimitives.WriteUInt16BigEndian(header[0x18..], (ushort)height); // ALPF
    BinaryPrimitives.WriteUInt16BigEndian(header[0x1A..], (ushort)width);  // SPL
    BinaryPrimitives.WriteUInt16BigEndian(header[0x1D..], (ushort)height); // NAL
    header[0x21] = 0x38; // SBD=001 (8 bit), fixed low bits
    header[0x22] = 0x88; // progressive source scan
    BinaryPrimitives.WriteUInt32BigEndian(header[0x28..], (uint)_COMPRESSION_ID);
    header[0x2C] = 0x80; // frame encoded, 4:2:2, BT.709, Y′CbCr

    header[0x167] = 0x02;
    BinaryPrimitives.WriteUInt16BigEndian(header[0x16A..], checked((ushort)(scanIndices.Length * 4 + 4)));
    BinaryPrimitives.WriteUInt16BigEndian(header[0x16C..], checked((ushort)scanIndices.Length));
    header[0x16F] = 0x10;

    for (var i = 0; i < scanIndices.Length; ++i)
      BinaryPrimitives.WriteUInt32BigEndian(header[(_SCAN_INDICES_AT + i * 4)..], checked((uint)scanIndices[i]));
  }
}
