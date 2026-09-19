using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.DnxHd;

/// <summary>Encodes one progressive 8-bit 4:2:2 DNxHD coding unit.</summary>
/// <remarks>
/// The transform and quantisation follow SMPTE ST 2019-1:2016 section 8; the frame layout follows
/// section 7. The HD profile is constant-size, so the encoder measures the entropy stream at a
/// candidate quantisation scale, selects the smallest scale found to fit the profile's fixed coding
/// unit, and fills the remaining payload with the padding section the standard defines.
/// </remarks>
internal sealed class DnxHdFrameEncoder {

  private const int _HEADER_SIZE = 0x280;
  private const int _END_OF_FRAME_SIZE = 4;
  private const int _BLOCKS_PER_MACROBLOCK_422 = 8;
  private const int _COEFFICIENTS_PER_BLOCK = 64;
  private const int _MAX_QUANTISATION_SCALE = 0x7FF;
  private const int _MAX_EIGHT_BIT_AMPLITUDE = 64 + 15 * 64;

  private const int _AMPLITUDE_MASK = 0x7F;
  private const int _RUN_FLAG = 1 << 7;
  private const int _INDEX_FLAG = 1 << 8;

  private static readonly (int Component, int X, int Y)[] _Blocks422 = [
    (0, 0, 0), (0, 8, 0), (1, 0, 0), (2, 0, 0),
    (0, 0, 8), (0, 8, 8), (1, 0, 8), (2, 0, 8),
  ];

  private readonly DnxHdProfile _profile;
  private readonly DnxHdCompressionId _compression;
  private readonly DnxHdVlcEncoderTable _dc;
  private readonly DnxHdVlcEncoderTable _amplitude;
  private readonly DnxHdVlcEncoderTable _run;
  private readonly byte[] _lumaWeights;
  private readonly byte[] _chromaWeights;
  private readonly int _endOfBlockSymbol;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;

  internal DnxHdFrameEncoder(DnxHdProfile profile) {
    this._profile = profile ?? throw new ArgumentNullException(nameof(profile));
    if (profile.Interlaced || profile.AdaptiveMacroblocks || profile.Is444 || profile.BitDepth != 8)
      throw new NotSupportedException(
        $"The DNxHD writer currently emits progressive 8-bit 4:2:2; compression ID {profile.CompressionId} is outside that write profile.");

    this._compression = DnxHdCompressionId.Find(profile.CompressionId)
      ?? throw new InvalidDataException($"Compression ID {profile.CompressionId} has no VC-3 coding tables.");

    var group = this._compression.VlcGroup;
    this._dc = DnxHdVlcEncoderTable.From(DnxHdVlcTables.DcLengths[group], DnxHdVlcTables.DcBitCounts[group]);
    this._amplitude = DnxHdVlcEncoderTable.From(DnxHdVlcTables.AmplitudeLengths[group], DnxHdVlcTables.AmplitudeSymbols[group]);
    this._run = DnxHdVlcEncoderTable.From(DnxHdVlcTables.RunLengths[group], DnxHdVlcTables.RunSymbols[group]);
    this._lumaWeights = DnxHdWeightTables.Luma[this._compression.WeightTable];
    this._chromaWeights = DnxHdWeightTables.Chroma[this._compression.WeightTable];
    this._endOfBlockSymbol = _FindEndOfBlock(DnxHdVlcTables.AmplitudeSymbols[group]);
    this._macroblockWidth = (profile.Width + 15) / 16;
    this._macroblockHeight = (profile.Height + 15) / 16;
  }

  internal byte[] Encode(byte[] planes) {
    ArgumentNullException.ThrowIfNull(planes);

    var lumaSamples = checked(this._profile.Width * this._profile.Height);
    var chromaSamples = checked((this._profile.Width / 2) * this._profile.Height);
    var expected = checked(lumaSamples + chromaSamples * 2);
    if (planes.Length != expected)
      throw new InvalidDataException(
        $"A {this._profile.Width}x{this._profile.Height} 4:2:2 frame needs {expected} planar bytes; {planes.Length} arrived.");

    var coefficients = this._Transform(planes, lumaSamples, chromaSamples);
    var payloadLimit = this._profile.CodingUnitSize - _HEADER_SIZE - _END_OF_FRAME_SIZE;
    var qscale = this._ChooseQuantisationScale(coefficients, payloadLimit);

    var rows = new byte[this._macroblockHeight][];
    var scanIndices = new int[this._macroblockHeight];
    var payloadLength = 0;

    for (var mbY = 0; mbY < this._macroblockHeight; ++mbY) {
      scanIndices[mbY] = payloadLength;
      rows[mbY] = this._EncodeScanLine(coefficients, mbY, qscale);
      payloadLength = checked(payloadLength + rows[mbY].Length);
    }

    if (payloadLength > payloadLimit)
      throw new InvalidOperationException(
        $"DNxHD rate selection chose q={qscale}, but its {payloadLength}-byte payload exceeds the {payloadLimit}-byte coding-unit budget.");

    var result = new byte[this._profile.CodingUnitSize];
    this._WriteHeader(result, scanIndices);

    var at = _HEADER_SIZE;
    foreach (var row in rows) {
      row.CopyTo(result, at);
      at += row.Length;
    }

    // 7.3.2: the unused compressed-data region is padding. The array is already zero-filled.
    result[^4] = 0x60;
    result[^3] = 0x0D;
    result[^2] = 0xC0;
    result[^1] = 0xDE;
    return result;
  }

  private double[] _Transform(byte[] planes, int lumaSamples, int chromaSamples) {
    var blockCount = checked(this._macroblockWidth * this._macroblockHeight * _BLOCKS_PER_MACROBLOCK_422);
    var result = new double[checked(blockCount * _COEFFICIENTS_PER_BLOCK)];
    Span<double> samples = stackalloc double[64];

    for (var mbY = 0; mbY < this._macroblockHeight; ++mbY)
      for (var mbX = 0; mbX < this._macroblockWidth; ++mbX)
        for (var block = 0; block < _Blocks422.Length; ++block) {
          var (component, x, y) = _Blocks422[block];
          var planeWidth = component == 0 ? this._profile.Width : this._profile.Width / 2;
          var planeHeight = this._profile.Height;
          var planeOffset = component switch {
            0 => 0,
            1 => lumaSamples,
            _ => lumaSamples + chromaSamples,
          };
          var macroblockWidth = component == 0 ? 16 : 8;
          var originX = mbX * macroblockWidth + x;
          var originY = mbY * 16 + y;

          for (var by = 0; by < 8; ++by)
            for (var bx = 0; bx < 8; ++bx) {
              var sourceX = originX + bx;
              var sourceY = originY + by;

              // 6.3 augmentation samples/lines are neutral after level adjustment. All classic HD
              // widths are a whole macroblock; the 1080-line profiles exercise the vertical case.
              samples[by * 8 + bx] = sourceX < planeWidth && sourceY < planeHeight
                ? planes[planeOffset + sourceY * planeWidth + sourceX] - 128d
                : 0d;
            }

          var coefficientOffset = this._CoefficientOffset(mbX, mbY, block);
          DnxHdForwardDct.Transform(samples, result.AsSpan(coefficientOffset, 64));
        }

    return result;
  }

  private int _ChooseQuantisationScale(double[] coefficients, int payloadLimit) {
    if (this._MeasurePayload(coefficients, 1) <= payloadLimit)
      return 1;

    if (this._MeasurePayload(coefficients, _MAX_QUANTISATION_SCALE) > payloadLimit)
      throw new InvalidDataException(
        $"This frame does not fit DNxHD compression ID {this._profile.CompressionId} even at the maximum quantisation scale.");

    var low = 2;
    var high = _MAX_QUANTISATION_SCALE;
    while (low < high) {
      var middle = low + (high - low) / 2;
      if (this._MeasurePayload(coefficients, middle) <= payloadLimit)
        high = middle;
      else
        low = middle + 1;
    }

    // VLC lengths can make the byte count locally non-monotonic even though coefficient magnitudes
    // only decrease. Walk back over any immediately preceding scales that also fit rather than
    // pretending the entropy code is a smooth rate function.
    while (low > 1 && this._MeasurePayload(coefficients, low - 1) <= payloadLimit)
      --low;

    return low;
  }

  private int _MeasurePayload(double[] coefficients, int qscale) {
    long bytes = 0;

    for (var mbY = 0; mbY < this._macroblockHeight; ++mbY) {
      var bits = 0L;
      Span<int> prediction = stackalloc int[3];
      prediction.Clear();

      for (var mbX = 0; mbX < this._macroblockWidth; ++mbX) {
        bits += 12;

        for (var block = 0; block < _Blocks422.Length; ++block) {
          var component = _Blocks422[block].Component;
          var blockCoefficients = coefficients.AsSpan(this._CoefficientOffset(mbX, mbY, block), 64);
          var blockBits = this._MeasureBlock(blockCoefficients, component != 0, qscale, ref prediction[component]);
          if (blockBits == int.MaxValue)
            return int.MaxValue;

          bits += blockBits;
        }
      }

      var rowBytes = (bits + 7) / 8;
      bytes += (rowBytes + 3) & ~3L;
      if (bytes >= int.MaxValue)
        return int.MaxValue;
    }

    return (int)bytes;
  }

  private int _MeasureBlock(ReadOnlySpan<double> coefficients, bool chroma, int qscale, ref int prediction) {
    var dc = _RoundCoefficient(coefficients[0]);
    var correction = dc - prediction;
    prediction = dc;

    var dcBits = _DcBitCount(correction);
    if (dcBits > 11)
      return int.MaxValue;

    var bits = this._dc.LengthOf(dcBits) + dcBits;
    var last = 0;
    var weights = chroma ? this._chromaWeights : this._lumaWeights;

    for (var index = 1; index < 64; ++index) {
      var position = DnxHdScan.RasterPosition[index];
      var level = this._Quantise(coefficients[position], weights[position], qscale);
      if (level == 0)
        continue;

      var magnitude = Math.Abs(level);
      if (magnitude > _MAX_EIGHT_BIT_AMPLITUDE)
        return int.MaxValue;

      var run = index - last - 1;
      var symbol = _AmplitudeSymbol(magnitude, run != 0, out var amplitudeIndex);
      bits += this._amplitude.LengthOf(symbol) + 1;
      if (amplitudeIndex != 0)
        bits += 4;
      if (run != 0)
        bits += this._run.LengthOf(run);

      last = index;
    }

    return bits + this._amplitude.LengthOf(this._endOfBlockSymbol);
  }

  private byte[] _EncodeScanLine(double[] coefficients, int mbY, int qscale) {
    var writer = new DnxHdBitWriter();
    Span<int> prediction = stackalloc int[3];
    prediction.Clear();

    for (var mbX = 0; mbX < this._macroblockWidth; ++mbX) {
      writer.WriteBits((uint)qscale, 11);
      writer.WriteBit(false); // reserved for every 4:2:2 profile written here

      for (var block = 0; block < _Blocks422.Length; ++block) {
        var component = _Blocks422[block].Component;
        var blockCoefficients = coefficients.AsSpan(this._CoefficientOffset(mbX, mbY, block), 64);
        this._WriteBlock(writer, blockCoefficients, component != 0, qscale, ref prediction[component]);
      }
    }

    writer.AlignToFourBytes();
    return writer.ToArray();
  }

  private void _WriteBlock(
    DnxHdBitWriter writer,
    ReadOnlySpan<double> coefficients,
    bool chroma,
    int qscale,
    ref int prediction) {
    var dc = _RoundCoefficient(coefficients[0]);
    var correction = dc - prediction;
    prediction = dc;

    var dcBits = _DcBitCount(correction);
    this._dc.Write(writer, dcBits);
    if (dcBits != 0) {
      var rho = correction < 0 ? correction - 1 : correction;
      writer.WriteBits(unchecked((uint)rho), dcBits);
    }

    var last = 0;
    var weights = chroma ? this._chromaWeights : this._lumaWeights;
    for (var index = 1; index < 64; ++index) {
      var position = DnxHdScan.RasterPosition[index];
      var level = this._Quantise(coefficients[position], weights[position], qscale);
      if (level == 0)
        continue;

      var magnitude = Math.Abs(level);
      var run = index - last - 1;
      var symbol = _AmplitudeSymbol(magnitude, run != 0, out var amplitudeIndex);
      this._amplitude.Write(writer, symbol);
      writer.WriteBit(level < 0);
      if (amplitudeIndex != 0)
        writer.WriteBits((uint)amplitudeIndex, 4);
      if (run != 0)
        this._run.Write(writer, run);

      last = index;
    }

    this._amplitude.Write(writer, this._endOfBlockSymbol);
  }

  private int _Quantise(double coefficient, int weight, int qscale) {
    if (coefficient == 0d)
      return 0;

    var magnitude = Math.Abs(coefficient);
    var quantised = (int)Math.Floor(magnitude * this._compression.InverseQuantisationDivisor / (qscale * (double)weight));
    return coefficient < 0d ? -quantised : quantised;
  }

  private void _WriteHeader(Span<byte> unit, ReadOnlySpan<int> scanIndices) {
    BinaryPrimitives.WriteUInt32BigEndian(unit, _HEADER_SIZE);
    unit[4] = (byte)this._profile.HeaderVersion;
    unit[5] = 0x01; // progressive frame, frame 1
    unit[6] = 0x80; // CRC flag clear
    unit[7] = 0xA0;

    BinaryPrimitives.WriteUInt16BigEndian(unit[0x18..], checked((ushort)this._profile.Height));
    BinaryPrimitives.WriteUInt16BigEndian(unit[0x1A..], checked((ushort)this._profile.Width));
    BinaryPrimitives.WriteUInt16BigEndian(unit[0x1D..], checked((ushort)this._profile.Height));
    unit[0x21] = 0x38; // SBD=1, eight bits; fixed low bits from Figure 20
    unit[0x22] = 0x88; // progressive source

    BinaryPrimitives.WriteUInt32BigEndian(unit[0x28..], checked((uint)this._profile.CompressionId));
    unit[0x2C] = 0x80; // frame encoded, 4:2:2, BT.709, Y'CbCr
    unit[0x5F] = 0x01;
    unit[0x167] = 0x02;
    BinaryPrimitives.WriteUInt16BigEndian(unit[0x16A..], checked((ushort)(this._macroblockHeight * 4 + 4)));
    BinaryPrimitives.WriteUInt16BigEndian(unit[0x16C..], checked((ushort)this._macroblockHeight));
    unit[0x16F] = 0x10;

    for (var i = 0; i < scanIndices.Length; ++i)
      BinaryPrimitives.WriteUInt32BigEndian(unit[(0x170 + i * 4)..], checked((uint)scanIndices[i]));
  }

  private int _CoefficientOffset(int mbX, int mbY, int block)
    => (((mbY * this._macroblockWidth + mbX) * _BLOCKS_PER_MACROBLOCK_422 + block) * _COEFFICIENTS_PER_BLOCK);

  private static int _RoundCoefficient(double value)
    => checked((int)Math.Round(value, MidpointRounding.AwayFromZero));

  private static int _DcBitCount(int correction) {
    if (correction == 0)
      return 0;

    var value = correction < 0 ? checked(-2 * correction) : checked(2 * correction);
    var result = 0;
    while ((value >>= 1) != 0)
      ++result;

    return result;
  }

  private static int _AmplitudeSymbol(int magnitude, bool hasRun, out int amplitudeIndex) {
    amplitudeIndex = (magnitude - 1) >> 6;
    var amplitude = magnitude - amplitudeIndex * 64;
    return (amplitude & _AMPLITUDE_MASK)
           | (hasRun ? _RUN_FLAG : 0)
           | (amplitudeIndex != 0 ? _INDEX_FLAG : 0);
  }

  private static int _FindEndOfBlock(ReadOnlySpan<ushort> symbols) {
    foreach (var symbol in symbols)
      if (DnxHdAmplitude.IsEndOfBlock(symbol))
        return symbol;

    throw new InvalidDataException("A VC-3 amplitude table has no end-of-block codeword.");
  }
}
