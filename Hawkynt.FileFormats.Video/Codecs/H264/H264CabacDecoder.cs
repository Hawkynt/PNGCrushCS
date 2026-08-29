using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>One H.264 CABAC context variable: pStateIdx plus valMPS (clause 9.3.1.1).</summary>
internal struct H264CabacContext {
  internal byte StateIndex;
  internal byte MostProbableSymbol;

  internal static H264CabacContext Initialize(int m, int n, int sliceQpY) {
    if (sliceQpY is < -36 or > 51)
      throw new InvalidDataException($"H.264 CABAC cannot initialize from SliceQPY {sliceQpY}.");
    var clippedQp = Math.Clamp(sliceQpY, 0, 51);
    var pre = Math.Clamp(((m * clippedQp) >> 4) + n, 1, 126);
    return pre <= 63
      ? new() { StateIndex = (byte)(63 - pre), MostProbableSymbol = 0 }
      : new() { StateIndex = (byte)(pre - 64), MostProbableSymbol = 1 };
  }
}

/// <summary>
/// The arithmetic engine of H.264 CABAC: decision, bypass and terminate bins (clause 9.3.3.2).
/// </summary>
/// <remarks>
/// Adapted to C# from OxideAV/oxideav-h264 <c>src/cabac.rs</c>, Copyright (c) 2026 Karpeles Lab Inc.,
/// MIT License. The range/state tables are the normative H.264 Tables 9-44 and 9-45.
/// </remarks>
internal ref struct H264CabacDecoder {
  private static readonly byte[,] _RANGE_LPS = {
    {128,176,208,240},{128,167,197,227},{128,158,187,216},{123,150,178,205},
    {116,142,169,195},{111,135,160,185},{105,128,152,175},{100,122,144,166},
    {95,116,137,158},{90,110,130,150},{85,104,123,142},{81,99,117,135},
    {77,94,111,128},{73,89,105,122},{69,85,100,116},{66,80,95,110},
    {62,76,90,104},{59,72,86,99},{56,69,81,94},{53,65,77,89},
    {51,62,73,85},{48,59,69,80},{46,56,66,76},{43,53,63,72},
    {41,50,59,69},{39,48,56,65},{37,45,54,62},{35,43,51,59},
    {33,41,48,56},{32,39,46,53},{30,37,43,50},{29,35,41,48},
    {27,33,39,45},{26,31,37,43},{24,30,35,41},{23,28,33,39},
    {22,27,32,37},{21,26,30,35},{20,24,29,33},{19,23,27,31},
    {18,22,26,30},{17,21,25,28},{16,20,23,27},{15,19,22,25},
    {14,18,21,24},{14,17,20,23},{13,16,19,22},{12,15,18,21},
    {12,14,17,20},{11,14,16,19},{11,13,15,18},{10,12,15,17},
    {10,12,14,16},{9,11,13,15},{9,11,12,14},{8,10,12,14},
    {8,9,11,13},{7,9,11,12},{7,9,10,12},{7,8,10,11},
    {6,8,9,11},{6,7,9,10},{6,7,8,9},{2,2,2,2},
  };

  private static readonly byte[] _TRANS_LPS = [
    0,0,1,2,2,4,4,5,6,7,8,9,9,11,11,12,
    13,13,15,15,16,16,18,18,19,19,21,21,22,22,23,24,
    24,25,26,26,27,27,28,29,29,30,30,30,31,32,32,33,
    33,33,34,34,35,35,35,36,36,36,37,37,37,38,38,63,
  ];

  private static readonly byte[] _TRANS_MPS = [
    1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,
    17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,
    33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,
    49,50,51,52,53,54,55,56,57,58,59,60,61,62,62,63,
  ];

  private H264BitReader _reader;
  private int _range;
  private int _offset;

  internal H264CabacDecoder(H264BitReader reader) {
    reader.AlignToByte();
    this._reader = reader;
    this._range = 510;
    this._offset = this._reader.ReadBits(9);
  }

  internal readonly int BitPosition => this._reader.BitPosition;
  internal readonly int Range => this._range;
  internal readonly int Offset => this._offset;

  internal int DecodeDecision(ref H264CabacContext context) {
    var qRange = (this._range >> 6) & 3;
    var lps = _RANGE_LPS[context.StateIndex, qRange];
    this._range -= lps;
    int bin;
    if (this._offset >= this._range) {
      bin = 1 - context.MostProbableSymbol;
      this._offset -= this._range;
      this._range = lps;
      if (context.StateIndex == 0)
        context.MostProbableSymbol = (byte)(1 - context.MostProbableSymbol);
      context.StateIndex = _TRANS_LPS[context.StateIndex];
    } else {
      bin = context.MostProbableSymbol;
      context.StateIndex = _TRANS_MPS[context.StateIndex];
    }
    this._Renormalize();
    return bin;
  }

  internal int DecodeBypass() {
    this._offset = (this._offset << 1) | this._reader.ReadBit();
    if (this._offset < this._range)
      return 0;
    this._offset -= this._range;
    return 1;
  }

  internal int DecodeTerminate() {
    this._range -= 2;
    if (this._offset >= this._range)
      return 1;
    this._Renormalize();
    return 0;
  }

  internal int DecodeBypassSigned(int magnitude) => this.DecodeBypass() == 0 ? magnitude : -magnitude;

  /// <summary>Unsigned Exp-Golomb-like bypass suffix used by CABAC coefficient levels.</summary>
  internal int DecodeBypassExpGolomb(int order) {
    var prefix = 0;
    while (this.DecodeBypass() != 0) {
      if (++prefix > 31 - order)
        throw new InvalidDataException("H.264 CABAC bypass Exp-Golomb suffix exceeds 31 bits.");
    }

    long value = 0;
    for (var i = 0; i < prefix + order; ++i)
      value = (value << 1) | (uint)this.DecodeBypass();
    value += ((1L << prefix) - 1) << order;
    if (value > int.MaxValue)
      throw new InvalidDataException("H.264 CABAC bypass Exp-Golomb value exceeds Int32.");
    return (int)value;
  }

  private void _Renormalize() {
    while (this._range < 256) {
      this._range <<= 1;
      this._offset = (this._offset << 1) | this._reader.ReadBit();
    }
  }
}
