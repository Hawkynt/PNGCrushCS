using System;
using System.IO;
using FileFormat.Codecs.MsMpeg4;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>Writes one rectangular MPEG-4 Part 2 I-VOP and the VOL needed to decode it.</summary>
internal sealed class Mpeg4PictureEncoder {

  /// <summary>Macroblock type 3 is intra without a DQUANT field (Table B-6).</summary>
  private const int _INTRA_MACROBLOCK = 3;

  /// <summary>The largest positive or negative coefficient the third escape's signed twelve bits state.</summary>
  private const int _MAX_LEVEL = 2047;

  private readonly Mpeg4Frame _source;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _quantiser;
  private readonly int _timeIncrementResolution;
  private readonly int _timeIncrement;
  private readonly int _moduloSeconds;
  private readonly int _timeIncrementBits;
  private readonly MsMpeg4BitWriter _writer = new();
  private readonly Mpeg4IntraPrediction _prediction;

  internal Mpeg4PictureEncoder(
    Mpeg4Frame source,
    int width,
    int height,
    int macroblockWidth,
    int macroblockHeight,
    int quantiser,
    int timeIncrementResolution,
    int timeIncrement,
    int moduloSeconds) {
    this._source = source;
    this._width = width;
    this._height = height;
    this._macroblockWidth = macroblockWidth;
    this._macroblockHeight = macroblockHeight;
    this._quantiser = quantiser;
    this._timeIncrementResolution = timeIncrementResolution;
    this._timeIncrement = timeIncrement;
    this._moduloSeconds = moduloSeconds;
    this._timeIncrementBits = _BitsFor(timeIncrementResolution);
    this._prediction = new(macroblockWidth, macroblockHeight);
  }

  internal byte[] Encode() {
    this._WriteVideoObjectLayer();
    this._WriteVideoObjectPlane();

    var macroblocks = checked(this._macroblockWidth * this._macroblockHeight);
    for (var address = 0; address < macroblocks; ++address)
      this._WriteMacroblock(address);

    return this._writer.ToArray();
  }

  // ============================================================================================
  // Headers — ISO/IEC 14496-2, 6.2.3 and 6.2.5
  // ============================================================================================

  /// <summary>
  /// The fixed set of coding tools this encoder uses: rectangular 8-bit 4:2:0, H.263 quantisation,
  /// progressive, no sprites, no resync markers, no data partitioning and no scalability.
  /// </summary>
  private void _WriteVideoObjectLayer() {
    this._StartCode(Mpeg4StartCode.FirstVideoObjectLayer);
    this._writer.Write(1, 1);                              // random_accessible_vol: every picture is intra
    this._writer.Write(1, 8);                              // video_object_type_indication: Simple object
    this._writer.Write(0, 1);                              // is_object_layer_identifier: version 1
    this._writer.Write(1, 4);                              // aspect_ratio_info: square pixels
    this._writer.Write(0, 1);                              // vol_control_parameters
    this._writer.Write(0, 2);                              // video_object_layer_shape: rectangular
    this._writer.Write(1, 1);                              // marker_bit
    this._writer.Write(this._timeIncrementResolution, 16);
    this._writer.Write(1, 1);                              // marker_bit
    this._writer.Write(0, 1);                              // fixed_vop_rate
    this._writer.Write(1, 1);                              // marker_bit
    this._writer.Write(this._width, 13);
    this._writer.Write(1, 1);                              // marker_bit
    this._writer.Write(this._height, 13);
    this._writer.Write(1, 1);                              // marker_bit
    this._writer.Write(0, 1);                              // interlaced
    this._writer.Write(1, 1);                              // obmc_disable
    this._writer.Write(0, 1);                              // sprite_enable, verid 1
    this._writer.Write(0, 1);                              // not_8_bit
    this._writer.Write(0, 1);                              // quant_type: H.263
    this._writer.Write(1, 1);                              // complexity_estimation_disable
    this._writer.Write(1, 1);                              // resync_marker_disable
    this._writer.Write(0, 1);                              // data_partitioned
    this._writer.Write(0, 1);                              // scalability
    this._NextStartCode();
  }

  private void _WriteVideoObjectPlane() {
    this._StartCode(Mpeg4StartCode.VideoObjectPlane);
    this._writer.Write(Mpeg4VideoObjectPlane.IntraCoded, 2);

    for (var second = 0; second < this._moduloSeconds; ++second)
      this._writer.Write(1, 1);

    this._writer.Write(0, 1);                              // modulo_time_base terminator
    this._writer.Write(1, 1);                              // marker_bit
    this._writer.Write(this._timeIncrement, this._timeIncrementBits);
    this._writer.Write(1, 1);                              // marker_bit
    this._writer.Write(1, 1);                              // vop_coded
    this._writer.Write(0, 3);                              // intra_dc_vlc_thr: always use DC VLC
    this._writer.Write(this._quantiser, 5);                // vop_quant
  }

  // ============================================================================================
  // Macroblocks and blocks — 6.2.6, 6.2.7, Annex B
  // ============================================================================================

  private void _WriteMacroblock(int address) {
    Span<int> levels = stackalloc int[6 * 64];
    var codedPattern = 0;

    for (var block = 0; block < 6; ++block) {
      var blockLevels = levels.Slice(block * 64, 64);
      this._QuantiseBlock(address, block, blockLevels);

      for (var scan = 1; scan < 64; ++scan)
        if (blockLevels[Mpeg4Quantisation.ZigZag[scan]] != 0) {
          codedPattern |= 1 << (5 - block);
          break;
        }
    }

    var chrominancePattern = codedPattern & 0x03;
    var luminancePattern = codedPattern >> 2;
    this._WriteVlc(Mpeg4VlcTables.IntraMacroblockType, _INTRA_MACROBLOCK * 4 + chrominancePattern);
    this._writer.Write(0, 1);                              // ac_pred_flag
    this._WriteVlc(Mpeg4VlcTables.LuminancePattern, luminancePattern);

    for (var block = 0; block < 6; ++block) {
      var blockLevels = levels.Slice(block * 64, 64);
      this._WriteDc(address, block, blockLevels[0]);
      if ((codedPattern & (1 << (5 - block))) != 0)
        this._WriteCoefficients(blockLevels);
    }
  }

  private void _QuantiseBlock(int address, int block, Span<int> levels) {
    Span<int> samples = stackalloc int[64];
    Span<double> coefficients = stackalloc double[64];
    this._ReadBlock(address, block, samples);
    MsMpeg4ForwardDct.Transform(samples, coefficients);

    var dcScaler = Mpeg4Quantisation.DcScaler(this._quantiser, block < 4);
    levels[0] = Math.Clamp(
      (int)Math.Round(coefficients[0] / dcScaler, MidpointRounding.AwayFromZero),
      -_MAX_LEVEL,
      _MAX_LEVEL);

    for (var index = 1; index < 64; ++index)
      levels[index] = _NearestH263Level(coefficients[index], this._quantiser);
  }

  /// <summary>
  /// Inverts the decoder's H.263 reconstruction by choosing the nearest level that reconstruction
  /// can actually produce. Level zero is special, so the candidate around the algebraic inverse is
  /// compared with zero and its immediate neighbours rather than rounded by a separate formula.
  /// </summary>
  private static int _NearestH263Level(double coefficient, int quantiser) {
    if (coefficient == 0)
      return 0;

    var sign = coefficient < 0 ? -1 : 1;
    var magnitude = Math.Abs(coefficient);
    var evenAdjustment = (quantiser & 1) == 0 ? 1 : 0;
    var estimate = (int)Math.Round(
      (magnitude - quantiser + evenAdjustment) / (2d * quantiser),
      MidpointRounding.AwayFromZero);

    var first = Math.Max(0, estimate - 2);
    var last = Math.Min(_MAX_LEVEL, Math.Max(2, estimate + 2));
    var best = 0;
    var bestError = magnitude;

    for (var candidate = first; candidate <= last; ++candidate) {
      var reconstructed = Math.Abs(Mpeg4Quantisation.DequantiseH263(candidate, quantiser));
      var error = Math.Abs(magnitude - reconstructed);
      if (error >= bestError)
        continue;

      best = candidate;
      bestError = error;
    }

    return sign * best;
  }

  private void _WriteDc(int address, int block, int absoluteLevel) {
    var isLuminance = block < 4;
    var dcScaler = Mpeg4Quantisation.DcScaler(this._quantiser, isLuminance);

    // Apply the decoder's predictor once to a zero differential to obtain exactly the predicted level,
    // then once with the real differential. The second pass overwrites the temporary current-block
    // state with the final DC; its predictor only reads neighbouring blocks, never the current one.
    Span<int> predicted = stackalloc int[64];
    var fromAbove = this._prediction.PredictsFromAbove(address, block);
    this._prediction.Apply(address, block, predicted, this._quantiser, dcScaler, predictAc: false, fromAbove);
    var differential = absoluteLevel - predicted[0];
    predicted[0] = differential;
    this._prediction.Apply(address, block, predicted, this._quantiser, dcScaler, predictAc: false, fromAbove);

    var size = 0;
    while (differential >= 1 << size || differential < -((1 << size) - 1))
      ++size;

    if (size > 12)
      throw new InvalidDataException(
        $"The DC differential {differential} needs {size} bits, beyond MPEG-4 Part 2's DC-size tables.");

    this._WriteVlc(isLuminance ? Mpeg4VlcTables.LuminanceDcSize : Mpeg4VlcTables.ChrominanceDcSize, size);
    if (size == 0)
      return;

    var bits = differential > 0 ? differential : differential + (1 << size) - 1;
    this._writer.Write(bits, size);
    if (size > 8)
      this._writer.Write(1, 1);                            // marker_bit
  }

  /// <summary>
  /// Writes every non-zero AC term through escape type 3. It is longer than Annex B's common rows,
  /// but it is the one form able to state every legal (last, run, level) triple directly and makes
  /// the writer independent of a second inverse index over the already validated decoder table.
  /// </summary>
  private void _WriteCoefficients(ReadOnlySpan<int> levels) {
    var lastIndex = 0;
    for (var scan = 1; scan < 64; ++scan)
      if (levels[Mpeg4Quantisation.ZigZag[scan]] != 0)
        lastIndex = scan;

    var previous = 0;
    for (var scan = 1; scan <= lastIndex; ++scan) {
      var level = levels[Mpeg4Quantisation.ZigZag[scan]];
      if (level == 0)
        continue;

      var run = scan - previous - 1;
      previous = scan;

      this._WriteVlc(Mpeg4VlcTables.IntraCoefficient, Mpeg4VlcTables.CoefficientEscape);
      this._writer.Write(3, 2);                            // escape type 3
      this._writer.Write(scan == lastIndex ? 1 : 0, 1);  // last
      this._writer.Write(run, 6);
      this._writer.Write(1, 1);                            // marker_bit
      this._writer.Write(level & 0xFFF, 12);
      this._writer.Write(1, 1);                            // marker_bit
    }
  }

  // ============================================================================================
  // Planes and bit output
  // ============================================================================================

  private void _ReadBlock(int address, int block, Span<int> samples) {
    var (plane, stride, origin, _, _) = this._source.PlaneOf(block);
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    var left = block < 4 ? column * 16 + (block & 1) * 8 : column * 8;
    var top = block < 4 ? row * 16 + (block >> 1) * 8 : row * 8;

    for (var y = 0; y < 8; ++y) {
      var source = origin + (top + y) * stride + left;
      for (var x = 0; x < 8; ++x)
        samples[y * 8 + x] = plane[source + x];
    }
  }

  private void _WriteVlc(Mpeg4VlcTable table, int value) {
    foreach (var (code, entryValue) in table.Entries) {
      if (entryValue != value)
        continue;

      foreach (var bit in code)
        switch (bit) {
          case '0': this._writer.Write(0, 1); break;
          case '1': this._writer.Write(1, 1); break;
          case ' ': break;
          default: throw new InvalidDataException($"{table.Name} contains '{bit}', which is not a bit.");
        }

      return;
    }

    throw new InvalidDataException($"{table.Name} has no code for value {value}.");
  }

  private void _StartCode(byte code) {
    if ((this._writer.BitCount & 7) != 0)
      throw new InvalidOperationException("An MPEG-4 start code can only begin on a byte boundary.");

    this._writer.Write(0x000001, 24);
    this._writer.Write(code, 8);
  }

  /// <summary>Writes the stuffing bit and ones that align the following start code.</summary>
  private void _NextStartCode() {
    this._writer.Write(0, 1);
    while ((this._writer.BitCount & 7) != 0)
      this._writer.Write(1, 1);
  }

  private static int _BitsFor(int resolution) {
    var bits = 1;
    while ((1 << bits) < resolution)
      ++bits;

    return bits;
  }
}
