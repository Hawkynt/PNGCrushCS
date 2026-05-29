using System;
using System.Runtime.CompilerServices;

namespace FileFormat.Heif.Codec;

/// <summary>CABAC (Context-Adaptive Binary Arithmetic Coding) decoder for HEVC.
/// Implements the arithmetic engine with context model updates per ITU-T H.265.</summary>
internal sealed class HeifCabacDecoder {

  private readonly byte[] _data;
  private int _byteOffset;
  private readonly int _endByte;
  private uint _range;
  private uint _value;
  private int _bitsNeeded;

  // Context model states (9-bit: 7-bit state + MPS)
  private readonly byte[] _contextStates;

  // LPS range table (Table 9-48 in H.265)
  private static readonly byte[,] _LPS_RANGE_TABLE = {
    { 128, 176, 208, 240 }, { 128, 167, 197, 227 }, { 128, 158, 187, 216 }, { 123, 150, 178, 205 },
    { 116, 142, 169, 195 }, { 111, 135, 160, 185 }, { 105, 128, 152, 175 }, { 100, 122, 144, 166 },
    {  95, 116, 137, 158 }, {  90, 110, 130, 150 }, {  85, 104, 123, 142 }, {  81,  99, 117, 135 },
    {  77,  94, 111, 128 }, {  73,  89, 105, 122 }, {  69,  85, 100, 116 }, {  66,  80,  95, 110 },
    {  62,  76,  90, 104 }, {  59,  72,  86,  99 }, {  56,  69,  81,  94 }, {  53,  65,  77,  89 },
    {  51,  62,  73,  85 }, {  48,  59,  69,  80 }, {  46,  56,  66,  76 }, {  43,  53,  63,  72 },
    {  41,  50,  59,  69 }, {  39,  48,  56,  65 }, {  37,  45,  54,  62 }, {  35,  43,  51,  59 },
    {  33,  41,  48,  56 }, {  32,  39,  46,  53 }, {  30,  37,  43,  50 }, {  29,  35,  41,  48 },
    {  27,  33,  39,  45 }, {  26,  31,  37,  43 }, {  24,  30,  35,  41 }, {  23,  28,  33,  39 },
    {  22,  27,  32,  37 }, {  21,  26,  30,  35 }, {  20,  24,  29,  33 }, {  19,  23,  27,  31 },
    {  18,  22,  26,  30 }, {  17,  21,  25,  28 }, {  16,  20,  23,  27 }, {  15,  19,  22,  25 },
    {  14,  18,  21,  24 }, {  14,  17,  20,  23 }, {  13,  16,  19,  22 }, {  12,  15,  18,  21 },
    {  12,  14,  17,  20 }, {  11,  14,  16,  19 }, {  11,  13,  15,  18 }, {  10,  12,  15,  17 },
    {  10,  12,  14,  16 }, {   9,  11,  13,  15 }, {   9,  11,  12,  14 }, {   8,  10,  12,  14 },
    {   8,   9,  11,  13 }, {   7,   9,  11,  12 }, {   7,   9,  10,  12 }, {   7,   8,  10,  11 },
    {   6,   8,   9,  11 }, {   6,   7,   9,  10 }, {   6,   7,   8,   9 }, {   2,   2,   2,   2 },
  };

  // Transition table for state update
  private static readonly byte[] _TRANS_LPS = [
     0,  0,  1,  2,  2,  4,  4,  5,  6,  7,  8,  9,  9, 11, 11, 12,
    13, 13, 15, 15, 16, 16, 18, 18, 19, 19, 21, 21, 22, 22, 23, 24,
    24, 25, 26, 26, 27, 27, 28, 29, 29, 30, 30, 30, 31, 32, 32, 33,
    33, 33, 34, 34, 35, 35, 35, 36, 36, 36, 37, 37, 37, 38, 38, 63,
  ];

  private static readonly byte[] _TRANS_MPS = [
     1,  2,  3,  4,  5,  6,  7,  8,  9, 10, 11, 12, 13, 14, 15, 16,
    17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
    33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48,
    49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 62, 63,
  ];

  public HeifCabacDecoder(byte[] data, int offset, int length, int numContexts = 256) {
    _data = data;
    _byteOffset = offset;
    _endByte = offset + length;
    _contextStates = new byte[numContexts];

    // Initialize context states to equiprobable
    for (var i = 0; i < numContexts; ++i)
      _contextStates[i] = 0; // state=0, MPS=0

    // Initialize arithmetic decoder
    _range = 510;
    _bitsNeeded = -8;

    // Read initial value
    _value = 0;
    for (var i = 0; i < 2; ++i) {
      _value <<= 8;
      if (_byteOffset < _endByte)
        _value |= _data[_byteOffset++];
    }
    _value <<= 1;
    _range = 510;
    _bitsNeeded = -8;
  }

  /// <summary>Whether the decoder has been exhausted.</summary>
  public bool IsAtEnd => _byteOffset >= _endByte && _bitsNeeded >= 0;

  /// <summary>Initializes a context with a given initial probability state.</summary>
  public void InitContext(int ctxIdx, int initValue) {
    if (ctxIdx < _contextStates.Length) {
      var slope = (initValue >> 4) * 5 - 45;
      var offset2 = ((initValue & 15) << 3) - 16;
      var initState = Math.Clamp(((slope * 26 + 128) >> 8) + offset2, 1, 126);
      _contextStates[ctxIdx] = initState >= 64
        ? (byte)(((initState - 64) << 1) | 1) // MPS=1
        : (byte)((63 - initState) << 1);       // MPS=0
    }
  }

  /// <summary>Decodes a single bin using context-adaptive arithmetic coding.</summary>
  public int DecodeBin(int ctxIdx) {
    var state = _contextStates[ctxIdx];
    var stateIdx = state >> 1;
    var mps = state & 1;

    var qRangeIdx = (_range >> 6) & 3;
    var lpsRange = (uint)_LPS_RANGE_TABLE[stateIdx, qRangeIdx];
    _range -= lpsRange;

    int binVal;
    if (_value < _range) {
      // MPS path
      binVal = mps;
      _contextStates[ctxIdx] = (byte)((_TRANS_MPS[stateIdx] << 1) | mps);
    } else {
      // LPS path
      binVal = 1 - mps;
      _value -= _range;
      _range = lpsRange;

      if (stateIdx == 0)
        _contextStates[ctxIdx] = (byte)((_TRANS_LPS[stateIdx] << 1) | (1 - mps));
      else
        _contextStates[ctxIdx] = (byte)((_TRANS_LPS[stateIdx] << 1) | mps);
    }

    _Renormalize();
    return binVal;
  }

  /// <summary>Decodes a bypass (equiprobable) bin.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int DecodeBypass() {
    _value <<= 1;
    ++_bitsNeeded;
    if (_bitsNeeded >= 0) {
      if (_byteOffset < _endByte) {
        _value |= _data[_byteOffset++];
        _bitsNeeded = -8;
      }
    }

    var half = _range;
    if (_value >= half) {
      _value -= half;
      return 1;
    }
    return 0;
  }

  /// <summary>Decodes a terminate bin (end of slice segment).</summary>
  public int DecodeTerminate() {
    _range -= 2;
    if (_value >= _range) {
      return 1; // end of slice
    }
    _Renormalize();
    return 0;
  }

  /// <summary>Reads N bypass bins as an unsigned integer.</summary>
  public uint ReadBypassBits(int n) {
    var result = 0u;
    for (var i = 0; i < n; ++i)
      result = (result << 1) | (uint)DecodeBypass();
    return result;
  }

  /// <summary>Decodes a truncated Rice (TU+EG) coded value for coefficient levels.</summary>
  public uint DecodeExpGolomb(int k) {
    // Unary prefix
    var prefix = 0u;
    while (DecodeBypass() != 0)
      ++prefix;

    // Binary suffix
    if (prefix < 32)
      return (prefix << k) + ReadBypassBits(k);

    return uint.MaxValue; // overflow guard
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void _Renormalize() {
    while (_range < 256) {
      _range <<= 1;
      _value <<= 1;
      ++_bitsNeeded;
      if (_bitsNeeded >= 0) {
        if (_byteOffset < _endByte)
          _value |= _data[_byteOffset++];
        _bitsNeeded = -8;
      }
    }
  }
}
