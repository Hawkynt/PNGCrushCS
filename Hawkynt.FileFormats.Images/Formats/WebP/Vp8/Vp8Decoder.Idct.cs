namespace FileFormat.WebP.Vp8;

// Port of idct.go: Inverse DCT (§14.3) and Inverse Walsh-Hadamard Transform (§14.4).
internal sealed partial class Vp8Decoder {

  private const int C1 = 85627; // 65536 * cos(pi/8) * sqrt(2)
  private const int C2 = 35468; // 65536 * sin(pi/8) * sqrt(2)

  /// <summary>4x4 IDCT, adding residual to ybr[y..y+4, x..x+4].</summary>
  private void _InverseDct4(int y, int x, int coeffBase) {
    var m = new int[16]; // m[i*4 + j] where i is x-index and j is row in the transposed temp
    for (var i = 0; i < 4; ++i) {
      var c0 = _coeff[coeffBase + 0];
      var c4 = _coeff[coeffBase + 4];
      var c8 = _coeff[coeffBase + 8];
      var c12 = _coeff[coeffBase + 12];
      var a = c0 + c8;
      var b = c0 - c8;
      var c = (c4 * C2 >> 16) - (c12 * C1 >> 16);
      var d = (c4 * C1 >> 16) + (c12 * C2 >> 16);
      m[i * 4 + 0] = a + d;
      m[i * 4 + 1] = b + c;
      m[i * 4 + 2] = b - c;
      m[i * 4 + 3] = a - d;
      ++coeffBase;
    }
    for (var j = 0; j < 4; ++j) {
      var dc = m[0 * 4 + j] + 4;
      var a = dc + m[2 * 4 + j];
      var b = dc - m[2 * 4 + j];
      var c = (m[1 * 4 + j] * C2 >> 16) - (m[3 * 4 + j] * C1 >> 16);
      var d = (m[1 * 4 + j] * C1 >> 16) + (m[3 * 4 + j] * C2 >> 16);
      var row = (y + j) * 32 + x;
      _ybr[row + 0] = _Clip8(_ybr[row + 0] + (a + d >> 3));
      _ybr[row + 1] = _Clip8(_ybr[row + 1] + (b + c >> 3));
      _ybr[row + 2] = _Clip8(_ybr[row + 2] + (b - c >> 3));
      _ybr[row + 3] = _Clip8(_ybr[row + 3] + (a - d >> 3));
    }
  }

  /// <summary>DC-only 4x4 IDCT shortcut.</summary>
  private void _InverseDct4DcOnly(int y, int x, int coeffBase) {
    var dc = _coeff[coeffBase + 0] + 4 >> 3;
    for (var j = 0; j < 4; ++j) {
      var row = (y + j) * 32 + x;
      for (var i = 0; i < 4; ++i)
        _ybr[row + i] = _Clip8(_ybr[row + i] + dc);
    }
  }

  /// <summary>8x8 IDCT as four 4x4 blocks.</summary>
  private void _InverseDct8(int y, int x, int coeffBase) {
    _InverseDct4(y + 0, x + 0, coeffBase + 0 * 16);
    _InverseDct4(y + 0, x + 4, coeffBase + 1 * 16);
    _InverseDct4(y + 4, x + 0, coeffBase + 2 * 16);
    _InverseDct4(y + 4, x + 4, coeffBase + 3 * 16);
  }

  private void _InverseDct8DcOnly(int y, int x, int coeffBase) {
    _InverseDct4DcOnly(y + 0, x + 0, coeffBase + 0 * 16);
    _InverseDct4DcOnly(y + 0, x + 4, coeffBase + 1 * 16);
    _InverseDct4DcOnly(y + 4, x + 0, coeffBase + 2 * 16);
    _InverseDct4DcOnly(y + 4, x + 4, coeffBase + 3 * 16);
  }

  /// <summary>Inverse Walsh-Hadamard Transform of the 16 Y2 coefficients (DCs of the 4x4 luma blocks).
  /// Reads from coeff[whtCoeffBase..whtCoeffBase+16] and writes DC values back into coeff[0,16,32,...,240].</summary>
  private void _InverseWht16() {
    var m = new int[16];
    for (var i = 0; i < 4; ++i) {
      var a0 = _coeff[384 + 0 + i] + _coeff[384 + 12 + i];
      var a1 = _coeff[384 + 4 + i] + _coeff[384 + 8 + i];
      var a2 = _coeff[384 + 4 + i] - _coeff[384 + 8 + i];
      var a3 = _coeff[384 + 0 + i] - _coeff[384 + 12 + i];
      m[0 + i] = a0 + a1;
      m[8 + i] = a0 - a1;
      m[4 + i] = a3 + a2;
      m[12 + i] = a3 - a2;
    }
    var outIdx = 0;
    for (var i = 0; i < 4; ++i) {
      var dc = m[0 + i * 4] + 3;
      var a0 = dc + m[3 + i * 4];
      var a1 = m[1 + i * 4] + m[2 + i * 4];
      var a2 = m[1 + i * 4] - m[2 + i * 4];
      var a3 = dc - m[3 + i * 4];
      _coeff[outIdx + 0] = (short)(a0 + a1 >> 3);
      _coeff[outIdx + 16] = (short)(a3 + a2 >> 3);
      _coeff[outIdx + 32] = (short)(a0 - a1 >> 3);
      _coeff[outIdx + 48] = (short)(a3 - a2 >> 3);
      outIdx += 64;
    }
  }
}
