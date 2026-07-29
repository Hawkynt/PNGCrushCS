using System;

namespace FileFormat.Bpg.Codec;

/// <summary>CABAC (Context-Adaptive Binary Arithmetic Coding) decoder for HEVC.</summary>
internal sealed class HevcCabacDecoder {

  /// <summary>Number of context models used in HEVC CABAC.</summary>
  private const int _ContextCount = 154;

  private readonly byte[] _data;
  private int _byteOffset;
  private uint _range;
  private uint _value;
  private int _bitsNeeded;

  private readonly byte[] _contextState = new byte[_ContextCount];
  private readonly byte[] _contextMps = new byte[_ContextCount];

  // CABAC range LPS table (Table 9-48 in HEVC spec)
  private static readonly byte[,] _RangeLpsTable = new byte[64, 4] {
    {128,176,208,240},{128,167,197,227},{128,158,187,216},{123,150,178,205},
    {116,142,169,195},{111,135,160,185},{105,128,152,175},{100,122,144,166},
    { 95,116,137,158},{ 90,110,130,150},{ 85,104,123,142},{ 81, 99,117,135},
    { 77, 94,111,128},{ 73, 89,105,122},{ 69, 85,100,116},{ 66, 80, 95,110},
    { 62, 76, 90,104},{ 59, 72, 86, 99},{ 56, 69, 81, 94},{ 53, 65, 77, 89},
    { 51, 62, 73, 85},{ 48, 59, 69, 80},{ 46, 56, 66, 76},{ 43, 53, 63, 72},
    { 41, 50, 59, 69},{ 39, 48, 56, 65},{ 37, 45, 54, 62},{ 35, 43, 51, 59},
    { 33, 41, 48, 56},{ 32, 39, 46, 53},{ 30, 37, 43, 50},{ 29, 35, 41, 48},
    { 27, 33, 39, 45},{ 26, 31, 37, 43},{ 24, 30, 35, 41},{ 23, 28, 33, 39},
    { 22, 27, 32, 37},{ 21, 26, 30, 35},{ 20, 24, 29, 33},{ 19, 23, 27, 31},
    { 18, 22, 26, 30},{ 17, 21, 25, 28},{ 16, 20, 23, 27},{ 15, 19, 22, 25},
    { 14, 18, 21, 24},{ 14, 17, 20, 23},{ 13, 16, 19, 22},{ 12, 15, 18, 21},
    { 12, 14, 17, 20},{ 11, 14, 16, 19},{ 11, 13, 15, 18},{ 10, 12, 15, 17},
    { 10, 12, 14, 16},{  9, 11, 13, 15},{  9, 11, 12, 14},{  8, 10, 12, 14},
    {  8,  9, 11, 13},{  7,  9, 11, 12},{  7,  9, 10, 12},{  7,  8, 10, 11},
    {  6,  8,  9, 11},{  6,  7,  9, 10},{  6,  7,  8, 10},{  2,  2,  2,  2},
  };

  // CABAC transition table for state updates
  private static readonly byte[] _TransIdxLps = [
     0, 0, 1, 2, 2, 4, 4, 5, 6, 7, 8, 9, 9,11,11,12,
    13,13,15,15,16,16,18,18,19,19,21,21,22,22,23,24,
    24,25,26,26,27,27,28,29,29,30,30,30,31,32,32,33,
    33,33,34,34,35,35,35,36,36,36,37,37,37,38,38,63,
  ];

  private static readonly byte[] _TransIdxMps = [
     1, 2, 3, 4, 5, 6, 7, 8, 9,10,11,12,13,14,15,16,
    17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,
    33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,
    49,50,51,52,53,54,55,56,57,58,59,60,61,62,62,63,
  ];

  public HevcCabacDecoder(byte[] data, int offset) {
    _data = data ?? throw new ArgumentNullException(nameof(data));
    _byteOffset = offset;
    _range = 510;
    _bitsNeeded = -8;
    _value = 0;

    // Read first two bytes to initialize
    _value = (uint)(_data[_byteOffset] << 8) | _data[_byteOffset + 1];
    _byteOffset += 2;
    _value <<= 16;
    _value >>= 7; // Normalize for 9-bit precision
  }

  /// <summary>Initializes context models for I-slice with the given QP.</summary>
  public void InitContextModels(int sliceQp) {
    for (var i = 0; i < _ContextCount; ++i) {
      var initValue = _DefaultInitValues[Math.Min(i, _DefaultInitValues.Length - 1)];
      var slopeIdx = initValue >> 4;
      var offsetIdx = initValue & 0x0F;
      var m = slopeIdx * 5 - 45;
      var n = (offsetIdx << 3) - 16;
      var preCtxState = Math.Clamp(((m * sliceQp) >> 4) + n, 1, 126);

      if (preCtxState <= 63) {
        _contextState[i] = (byte)(63 - preCtxState);
        _contextMps[i] = 0;
      } else {
        _contextState[i] = (byte)(preCtxState - 64);
        _contextMps[i] = 1;
      }
    }
  }

  // Default init values for I-slice context models (simplified set covering primary contexts)
  private static readonly byte[] _DefaultInitValues = [
    // split_cu_flag [0..2]
    139, 141, 157,
    // split_transform_flag [3..5]
    153, 138, 138,
    // cbf_luma [6..7]
    111, 141,
    // cbf_cb, cbf_cr [8..11]
    94, 138, 94, 138,
    // transform_skip_flag [12..13]
    139, 139,
    // last_sig_coeff_x_prefix [14..31]
    110, 110, 124, 125, 140, 153, 125, 127, 140, 109, 111, 143, 127, 111, 79, 108, 123, 63,
    // last_sig_coeff_y_prefix [32..49]
    110, 110, 124, 125, 140, 153, 125, 127, 140, 109, 111, 143, 127, 111, 79, 108, 123, 63,
    // coded_sub_block_flag [50..53]
    91, 171, 134, 141,
    // sig_coeff_flag [54..97]
    111, 111, 125, 110, 110, 94, 124, 108, 124, 107, 125, 141, 179, 153, 125, 107,
    125, 141, 179, 153, 125, 107, 125, 141, 179, 153, 125, 140, 139, 182, 182, 152,
    136, 152, 136, 153, 136, 139, 111, 136, 139, 111, 141, 111,
    // coeff_abs_level_greater1_flag [98..121]
    140, 92, 137, 138, 140, 152, 138, 139, 153, 74, 149, 92, 139, 107, 122, 152,
    140, 179, 166, 182, 140, 227, 122, 197,
    // coeff_abs_level_greater2_flag [122..127]
    138, 153, 136, 167, 152, 152,
    // cu_qp_delta [128..130]
    154, 154, 154,
    // pred_mode_flag [131]
    134,
    // part_mode [132..135]
    154, 139, 154, 154,
    // prev_intra_luma_pred_flag [136]
    184,
    // intra_chroma_pred_mode [137]
    63,
    // merge_flag [138]
    110,
    // merge_idx [139]
    122,
    // cu_transquant_bypass_flag [140]
    154,
    // skip_flag [141..142]
    197, 185,
    // sao_merge [143]
    153,
    // sao_type [144]
    200,
    // padding to 154
    154, 154, 154, 154, 154, 154, 154, 154, 154,
  ];

  /// <summary>Decodes one bin using the specified context model index.</summary>
  public int DecodeBin(int ctxIdx) {
    if (ctxIdx < 0 || ctxIdx >= _ContextCount)
      throw new ArgumentOutOfRangeException(nameof(ctxIdx));

    var state = _contextState[ctxIdx];
    var mps = _contextMps[ctxIdx];
    var qRangeIdx = (_range >> 6) & 3;
    var rangeLps = _RangeLpsTable[state, qRangeIdx];

    _range -= rangeLps;

    int binVal;
    if (_value < _range) {
      // MPS path
      binVal = mps;
      _contextState[ctxIdx] = _TransIdxMps[state];
    } else {
      // LPS path
      binVal = 1 - mps;
      _value -= _range;
      _range = rangeLps;

      if (state == 0)
        _contextMps[ctxIdx] = (byte)(1 - mps);

      _contextState[ctxIdx] = _TransIdxLps[state];
    }

    // Renormalization
    _Renorm();

    return binVal;
  }

  /// <summary>Decodes one bin in bypass mode (equiprobable).</summary>
  public int DecodeBypass() {
    _value <<= 1;
    ++_bitsNeeded;
    if (_bitsNeeded >= 0) {
      if (_byteOffset < _data.Length)
        _value |= (uint)_data[_byteOffset++];
      _bitsNeeded = -8;
    }

    if (_value >= _range) {
      _value -= _range;
      return 1;
    }
    return 0;
  }

  /// <summary>Decodes the terminating bin (end_of_slice_segment_flag or end_of_sub_stream).</summary>
  public int DecodeTerminate() {
    _range -= 2;
    if (_value >= _range) {
      return 1; // terminating
    }
    _Renorm();
    return 0;
  }

  /// <summary>Decodes a truncated Rice (TR) code with given max and rice parameter.</summary>
  public uint DecodeTruncatedRice(int riceParam, int max) {
    var prefix = 0u;
    while (prefix < (uint)(max >> riceParam) && DecodeBypass() != 0)
      ++prefix;

    var value = prefix << riceParam;
    if (riceParam > 0)
      for (var i = riceParam - 1; i >= 0; --i)
        value |= (uint)(DecodeBypass() << i);

    return Math.Min(value, (uint)max);
  }

  /// <summary>Decodes an unsigned Exp-Golomb code of order k using bypass bins.</summary>
  public uint DecodeExpGolombBypass(int k) {
    var prefix = 0;
    while (DecodeBypass() != 0) {
      ++prefix;
      ++k;
    }

    var value = 0u;
    for (var i = k - 1; i >= 0; --i)
      value |= (uint)(DecodeBypass() << i);

    return value + ((1u << prefix) - 1) * (1u << (k - prefix));
  }

  /// <summary>Decodes a unary code in bypass mode with optional truncation.</summary>
  public int DecodeUnaryMaxBypass(int max) {
    var val = 0;
    while (val < max && DecodeBypass() != 0)
      ++val;
    return val;
  }

  /// <summary>Current byte offset in the underlying data.</summary>
  public int ByteOffset => _byteOffset;

  private void _Renorm() {
    while (_range < 256) {
      _range <<= 1;
      _value <<= 1;
      ++_bitsNeeded;
      if (_bitsNeeded >= 0) {
        if (_byteOffset < _data.Length)
          _value |= (uint)_data[_byteOffset++];
        _bitsNeeded = -8;
      }
    }
  }
}
