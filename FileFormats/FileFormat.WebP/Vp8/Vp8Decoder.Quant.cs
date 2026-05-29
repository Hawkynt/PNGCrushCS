namespace FileFormat.WebP.Vp8;

// Port of quant.go: quantization factor parsing and dequant tables (RFC 6386 §9.6, §14.1).
internal sealed partial class Vp8Decoder {

  private static int _Clip(int x, int min, int max) => x < min ? min : x > max ? max : x;

  private void _ParseQuant() {
    var baseQ0 = _fp.ReadUint(Vp8Partition.UniformProb, 7);
    var dqy1DC = _fp.ReadOptionalInt(Vp8Partition.UniformProb, 4);
    const int dqy1AC = 0;
    var dqy2DC = _fp.ReadOptionalInt(Vp8Partition.UniformProb, 4);
    var dqy2AC = _fp.ReadOptionalInt(Vp8Partition.UniformProb, 4);
    var dquvDC = _fp.ReadOptionalInt(Vp8Partition.UniformProb, 4);
    var dquvAC = _fp.ReadOptionalInt(Vp8Partition.UniformProb, 4);
    for (var i = 0; i < NSegment; ++i) {
      var q = (int)baseQ0;
      if (_segmentHeader.UseSegment) {
        if (_segmentHeader.RelativeDelta)
          q += _segmentHeader.GetQuantizer(i);
        else
          q = _segmentHeader.GetQuantizer(i);
      }
      _quant[i].Y1Dc = _DequantTableDC[_Clip(q + dqy1DC, 0, 127)];
      _quant[i].Y1Ac = _DequantTableAC[_Clip(q + dqy1AC, 0, 127)];
      _quant[i].Y2Dc = (ushort)(_DequantTableDC[_Clip(q + dqy2DC, 0, 127)] * 2);
      var y2Ac = _DequantTableAC[_Clip(q + dqy2AC, 0, 127)] * 155 / 100;
      if (y2Ac < 8) y2Ac = 8;
      _quant[i].Y2Ac = (ushort)y2Ac;
      // 117 (not a typo): dequantTableDC[117] == 132, the spec's upper clamp for UV DC.
      _quant[i].UvDc = _DequantTableDC[_Clip(q + dquvDC, 0, 117)];
      _quant[i].UvAc = _DequantTableAC[_Clip(q + dquvAC, 0, 127)];
    }
  }

  private static readonly ushort[] _DequantTableDC = [
    4, 5, 6, 7, 8, 9, 10, 10,
    11, 12, 13, 14, 15, 16, 17, 17,
    18, 19, 20, 20, 21, 21, 22, 22,
    23, 23, 24, 25, 25, 26, 27, 28,
    29, 30, 31, 32, 33, 34, 35, 36,
    37, 37, 38, 39, 40, 41, 42, 43,
    44, 45, 46, 46, 47, 48, 49, 50,
    51, 52, 53, 54, 55, 56, 57, 58,
    59, 60, 61, 62, 63, 64, 65, 66,
    67, 68, 69, 70, 71, 72, 73, 74,
    75, 76, 76, 77, 78, 79, 80, 81,
    82, 83, 84, 85, 86, 87, 88, 89,
    91, 93, 95, 96, 98, 100, 101, 102,
    104, 106, 108, 110, 112, 114, 116, 118,
    122, 124, 126, 128, 130, 132, 134, 136,
    138, 140, 143, 145, 148, 151, 154, 157,
  ];

  private static readonly ushort[] _DequantTableAC = [
    4, 5, 6, 7, 8, 9, 10, 11,
    12, 13, 14, 15, 16, 17, 18, 19,
    20, 21, 22, 23, 24, 25, 26, 27,
    28, 29, 30, 31, 32, 33, 34, 35,
    36, 37, 38, 39, 40, 41, 42, 43,
    44, 45, 46, 47, 48, 49, 50, 51,
    52, 53, 54, 55, 56, 57, 58, 60,
    62, 64, 66, 68, 70, 72, 74, 76,
    78, 80, 82, 84, 86, 88, 90, 92,
    94, 96, 98, 100, 102, 104, 106, 108,
    110, 112, 114, 116, 119, 122, 125, 128,
    131, 134, 137, 140, 143, 146, 149, 152,
    155, 158, 161, 164, 167, 170, 173, 177,
    181, 185, 189, 193, 197, 201, 205, 209,
    213, 217, 221, 225, 229, 234, 239, 245,
    249, 254, 259, 264, 269, 274, 279, 284,
  ];
}
