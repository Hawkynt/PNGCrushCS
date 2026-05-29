namespace FileFormat.WebP.Vp8;

// Port of pred.go + predfunc.go: intra predictor mode parsing + prediction functions (RFC 6386 §11, §12).
internal sealed partial class Vp8Decoder {

  private const int NPred = 10;

  private const byte PredDC = 0;
  private const byte PredTM = 1;
  private const byte PredVE = 2;
  private const byte PredHE = 3;
  private const byte PredRD = 4;
  private const byte PredVR = 5;
  private const byte PredLD = 6;
  private const byte PredVL = 7;
  private const byte PredHD = 8;
  private const byte PredHU = 9;
  private const byte PredDCTop = 10;
  private const byte PredDCLeft = 11;
  private const byte PredDCTopLeft = 12;

  private static byte _CheckTopLeftPred(int mbx, int mby, byte p) {
    if (p != PredDC) return p;
    if (mbx == 0)
      return mby == 0 ? PredDCTopLeft : PredDCLeft;
    if (mby == 0) return PredDCTop;
    return PredDC;
  }

  // --- Predictor mode parsing (pred.go) ---

  private void _ParsePredModeY16(int mbx) {
    byte p;
    if (!_fp.ReadBit(156)) {
      p = !_fp.ReadBit(163) ? PredDC : PredVE;
    } else if (!_fp.ReadBit(128)) {
      p = PredHE;
    } else {
      p = PredTM;
    }
    for (var i = 0; i < 4; ++i) {
      _upMB[mbx].SetPred(i, p);
      _leftMB.SetPred(i, p);
    }
    _predY16 = p;
  }

  private void _ParsePredModeC8() {
    if (!_fp.ReadBit(142)) _predC8 = PredDC;
    else if (!_fp.ReadBit(114)) _predC8 = PredVE;
    else if (!_fp.ReadBit(183)) _predC8 = PredHE;
    else _predC8 = PredTM;
  }

  private void _ParsePredModeY4(int mbx) {
    for (var j = 0; j < 4; ++j) {
      var p = _leftMB.GetPred(j);
      for (var i = 0; i < 4; ++i) {
        var baseIdx = (_upMB[mbx].GetPred(i) * NPred + p) * 9;
        if (!_fp.ReadBit(_PredProb[baseIdx + 0])) p = PredDC;
        else if (!_fp.ReadBit(_PredProb[baseIdx + 1])) p = PredTM;
        else if (!_fp.ReadBit(_PredProb[baseIdx + 2])) p = PredVE;
        else if (!_fp.ReadBit(_PredProb[baseIdx + 3])) {
          if (!_fp.ReadBit(_PredProb[baseIdx + 4])) p = PredHE;
          else if (!_fp.ReadBit(_PredProb[baseIdx + 5])) p = PredRD;
          else p = PredVR;
        } else if (!_fp.ReadBit(_PredProb[baseIdx + 6])) p = PredLD;
        else if (!_fp.ReadBit(_PredProb[baseIdx + 7])) p = PredVL;
        else if (!_fp.ReadBit(_PredProb[baseIdx + 8])) p = PredHD;
        else p = PredHU;
        _predY4[j * 4 + i] = p;
        _upMB[mbx].SetPred(i, p);
      }
      _leftMB.SetPred(j, p);
    }
  }

  /// <summary>§11.5 predictor-mode probabilities, flattened [10][10][9] → 900 bytes.
  /// Exposed to the encoder (<c>Vp8Encoder.Bitstream._WriteI4Mode</c>) to keep a single
  /// source of truth for RFC 6386 spec tables.</summary>
  internal static readonly byte[] _PredProb = [
    // above=PredDC
    231,120,48,89,115,113,120,152,112,
    152,179,64,126,170,118,46,70,95,
    175,69,143,80,85,82,72,155,103,
    56,58,10,171,218,189,17,13,152,
    114,26,17,163,44,195,21,10,173,
    121,24,80,195,26,62,44,64,85,
    144,71,10,38,171,213,144,34,26,
    170,46,55,19,136,160,33,206,71,
    63,20,8,114,114,208,12,9,226,
    81,40,11,96,182,84,29,16,36,
    // above=PredTM
    134,183,89,137,98,101,106,165,148,
    72,187,100,130,157,111,32,75,80,
    66,102,167,99,74,62,40,234,128,
    41,53,9,178,241,141,26,8,107,
    74,43,26,146,73,166,49,23,157,
    65,38,105,160,51,52,31,115,128,
    104,79,12,27,217,255,87,17,7,
    87,68,71,44,114,51,15,186,23,
    47,41,14,110,182,183,21,17,194,
    66,45,25,102,197,189,23,18,22,
    // above=PredVE
    88,88,147,150,42,46,45,196,205,
    43,97,183,117,85,38,35,179,61,
    39,53,200,87,26,21,43,232,171,
    56,34,51,104,114,102,29,93,77,
    39,28,85,171,58,165,90,98,64,
    34,22,116,206,23,34,43,166,73,
    107,54,32,26,51,1,81,43,31,
    68,25,106,22,64,171,36,225,114,
    34,19,21,102,132,188,16,76,124,
    62,18,78,95,85,57,50,48,51,
    // above=PredHE
    193,101,35,159,215,111,89,46,111,
    60,148,31,172,219,228,21,18,111,
    112,113,77,85,179,255,38,120,114,
    40,42,1,196,245,209,10,25,109,
    88,43,29,140,166,213,37,43,154,
    61,63,30,155,67,45,68,1,209,
    100,80,8,43,154,1,51,26,71,
    142,78,78,16,255,128,34,197,171,
    41,40,5,102,211,183,4,1,221,
    51,50,17,168,209,192,23,25,82,
    // above=PredRD
    138,31,36,171,27,166,38,44,229,
    67,87,58,169,82,115,26,59,179,
    63,59,90,180,59,166,93,73,154,
    40,40,21,116,143,209,34,39,175,
    47,15,16,183,34,223,49,45,183,
    46,17,33,183,6,98,15,32,183,
    57,46,22,24,128,1,54,17,37,
    65,32,73,115,28,128,23,128,205,
    40,3,9,115,51,192,18,6,223,
    87,37,9,115,59,77,64,21,47,
    // above=PredVR
    104,55,44,218,9,54,53,130,226,
    64,90,70,205,40,41,23,26,57,
    54,57,112,184,5,41,38,166,213,
    30,34,26,133,152,116,10,32,134,
    39,19,53,221,26,114,32,73,255,
    31,9,65,234,2,15,1,118,73,
    75,32,12,51,192,255,160,43,51,
    88,31,35,67,102,85,55,186,85,
    56,21,23,111,59,205,45,37,192,
    55,38,70,124,73,102,1,34,98,
    // above=PredLD
    125,98,42,88,104,85,117,175,82,
    95,84,53,89,128,100,113,101,45,
    75,79,123,47,51,128,81,171,1,
    57,17,5,71,102,57,53,41,49,
    38,33,13,121,57,73,26,1,85,
    41,10,67,138,77,110,90,47,114,
    115,21,2,10,102,255,166,23,6,
    101,29,16,10,85,128,101,196,26,
    57,18,10,102,102,213,34,20,43,
    117,20,15,36,163,128,68,1,26,
    // above=PredVL
    102,61,71,37,34,53,31,243,192,
    69,60,71,38,73,119,28,222,37,
    68,45,128,34,1,47,11,245,171,
    62,17,19,70,146,85,55,62,70,
    37,43,37,154,100,163,85,160,1,
    63,9,92,136,28,64,32,201,85,
    75,15,9,9,64,255,184,119,16,
    86,6,28,5,64,255,25,248,1,
    56,8,17,132,137,255,55,116,128,
    58,15,20,82,135,57,26,121,40,
    // above=PredHD
    164,50,31,137,154,133,25,35,218,
    51,103,44,131,131,123,31,6,158,
    86,40,64,135,148,224,45,183,128,
    22,26,17,131,240,154,14,1,209,
    45,16,21,91,64,222,7,1,197,
    56,21,39,155,60,138,23,102,213,
    83,12,13,54,192,255,68,47,28,
    85,26,85,85,128,128,32,146,171,
    18,11,7,63,144,171,4,4,246,
    35,27,10,146,174,171,12,26,128,
    // above=PredHU
    190,80,35,99,180,80,126,54,45,
    85,126,47,87,176,51,41,20,32,
    101,75,128,139,118,146,116,128,85,
    56,41,15,176,236,85,37,9,62,
    71,30,17,119,118,255,17,18,138,
    101,38,60,138,55,70,43,26,142,
    146,36,19,30,171,255,97,27,20,
    138,45,61,62,219,1,81,188,64,
    32,41,20,117,151,142,20,21,163,
    112,19,12,61,195,128,48,4,24,
  ];

  // --- Prediction functions (predfunc.go) ---
  // All operate on the ybr workspace at position (y, x). Access via row*32+col.

  private void _Pred16(int mode, int y, int x) {
    switch (mode) {
      case PredDC: _PredFunc16DC(y, x); break;
      case PredTM: _PredFunc16TM(y, x); break;
      case PredVE: _PredFunc16VE(y, x); break;
      case PredHE: _PredFunc16HE(y, x); break;
      case PredDCTop: _PredFunc16DCTop(y, x); break;
      case PredDCLeft: _PredFunc16DCLeft(y, x); break;
      case PredDCTopLeft: _PredFunc16DCTopLeft(y, x); break;
    }
  }

  private void _Pred8(int mode, int y, int x) {
    switch (mode) {
      case PredDC: _PredFunc8DC(y, x); break;
      case PredTM: _PredFunc8TM(y, x); break;
      case PredVE: _PredFunc8VE(y, x); break;
      case PredHE: _PredFunc8HE(y, x); break;
      case PredDCTop: _PredFunc8DCTop(y, x); break;
      case PredDCLeft: _PredFunc8DCLeft(y, x); break;
      case PredDCTopLeft: _PredFunc8DCTopLeft(y, x); break;
    }
  }

  private void _Pred4(int mode, int y, int x) {
    switch (mode) {
      case PredDC: _PredFunc4DC(y, x); break;
      case PredTM: _PredFunc4TM(y, x); break;
      case PredVE: _PredFunc4VE(y, x); break;
      case PredHE: _PredFunc4HE(y, x); break;
      case PredRD: _PredFunc4RD(y, x); break;
      case PredVR: _PredFunc4VR(y, x); break;
      case PredLD: _PredFunc4LD(y, x); break;
      case PredVL: _PredFunc4VL(y, x); break;
      case PredHD: _PredFunc4HD(y, x); break;
      case PredHU: _PredFunc4HU(y, x); break;
    }
  }

  // --- 4x4 predictors ---

  private void _PredFunc4DC(int y, int x) {
    uint sum = 4;
    for (var i = 0; i < 4; ++i) sum += _ybr[(y - 1) * 32 + x + i];
    for (var j = 0; j < 4; ++j) sum += _ybr[(y + j) * 32 + x - 1];
    var avg = (byte)(sum / 8);
    for (var j = 0; j < 4; ++j)
      for (var i = 0; i < 4; ++i)
        _ybr[(y + j) * 32 + x + i] = avg;
  }

  private void _PredFunc4TM(int y, int x) {
    var delta0 = -_ybr[(y - 1) * 32 + x - 1];
    for (var j = 0; j < 4; ++j) {
      var delta1 = delta0 + _ybr[(y + j) * 32 + x - 1];
      for (var i = 0; i < 4; ++i) {
        var delta2 = delta1 + _ybr[(y - 1) * 32 + x + i];
        _ybr[(y + j) * 32 + x + i] = (byte)_Clip(delta2, 0, 255);
      }
    }
  }

  private void _PredFunc4VE(int y, int x) {
    int a = _ybr[(y - 1) * 32 + x - 1];
    int b = _ybr[(y - 1) * 32 + x + 0];
    int c = _ybr[(y - 1) * 32 + x + 1];
    int d = _ybr[(y - 1) * 32 + x + 2];
    int e = _ybr[(y - 1) * 32 + x + 3];
    int f = _ybr[(y - 1) * 32 + x + 4];
    var abc = (byte)((a + 2 * b + c + 2) / 4);
    var bcd = (byte)((b + 2 * c + d + 2) / 4);
    var cde = (byte)((c + 2 * d + e + 2) / 4);
    var def = (byte)((d + 2 * e + f + 2) / 4);
    for (var j = 0; j < 4; ++j) {
      _ybr[(y + j) * 32 + x + 0] = abc;
      _ybr[(y + j) * 32 + x + 1] = bcd;
      _ybr[(y + j) * 32 + x + 2] = cde;
      _ybr[(y + j) * 32 + x + 3] = def;
    }
  }

  private void _PredFunc4HE(int y, int x) {
    int s = _ybr[(y + 3) * 32 + x - 1];
    int r = _ybr[(y + 2) * 32 + x - 1];
    int q = _ybr[(y + 1) * 32 + x - 1];
    int p = _ybr[(y + 0) * 32 + x - 1];
    int a = _ybr[(y - 1) * 32 + x - 1];
    var ssr = (byte)((s + 2 * s + r + 2) / 4);
    var srq = (byte)((s + 2 * r + q + 2) / 4);
    var rqp = (byte)((r + 2 * q + p + 2) / 4);
    var apq = (byte)((a + 2 * p + q + 2) / 4);
    for (var i = 0; i < 4; ++i) {
      _ybr[(y + 0) * 32 + x + i] = apq;
      _ybr[(y + 1) * 32 + x + i] = rqp;
      _ybr[(y + 2) * 32 + x + i] = srq;
      _ybr[(y + 3) * 32 + x + i] = ssr;
    }
  }

  private void _PredFunc4RD(int y, int x) {
    int s = _ybr[(y + 3) * 32 + x - 1];
    int r = _ybr[(y + 2) * 32 + x - 1];
    int q = _ybr[(y + 1) * 32 + x - 1];
    int p = _ybr[(y + 0) * 32 + x - 1];
    int a = _ybr[(y - 1) * 32 + x - 1];
    int b = _ybr[(y - 1) * 32 + x + 0];
    int c = _ybr[(y - 1) * 32 + x + 1];
    int d = _ybr[(y - 1) * 32 + x + 2];
    int e = _ybr[(y - 1) * 32 + x + 3];
    var srq = (byte)((s + 2 * r + q + 2) / 4);
    var rqp = (byte)((r + 2 * q + p + 2) / 4);
    var qpa = (byte)((q + 2 * p + a + 2) / 4);
    var pab = (byte)((p + 2 * a + b + 2) / 4);
    var abc = (byte)((a + 2 * b + c + 2) / 4);
    var bcd = (byte)((b + 2 * c + d + 2) / 4);
    var cde = (byte)((c + 2 * d + e + 2) / 4);
    _ybr[(y + 0) * 32 + x + 0] = pab; _ybr[(y + 0) * 32 + x + 1] = abc; _ybr[(y + 0) * 32 + x + 2] = bcd; _ybr[(y + 0) * 32 + x + 3] = cde;
    _ybr[(y + 1) * 32 + x + 0] = qpa; _ybr[(y + 1) * 32 + x + 1] = pab; _ybr[(y + 1) * 32 + x + 2] = abc; _ybr[(y + 1) * 32 + x + 3] = bcd;
    _ybr[(y + 2) * 32 + x + 0] = rqp; _ybr[(y + 2) * 32 + x + 1] = qpa; _ybr[(y + 2) * 32 + x + 2] = pab; _ybr[(y + 2) * 32 + x + 3] = abc;
    _ybr[(y + 3) * 32 + x + 0] = srq; _ybr[(y + 3) * 32 + x + 1] = rqp; _ybr[(y + 3) * 32 + x + 2] = qpa; _ybr[(y + 3) * 32 + x + 3] = pab;
  }

  private void _PredFunc4VR(int y, int x) {
    int r = _ybr[(y + 2) * 32 + x - 1];
    int q = _ybr[(y + 1) * 32 + x - 1];
    int p = _ybr[(y + 0) * 32 + x - 1];
    int a = _ybr[(y - 1) * 32 + x - 1];
    int b = _ybr[(y - 1) * 32 + x + 0];
    int c = _ybr[(y - 1) * 32 + x + 1];
    int d = _ybr[(y - 1) * 32 + x + 2];
    int e = _ybr[(y - 1) * 32 + x + 3];
    var ab = (byte)((a + b + 1) / 2);
    var bc = (byte)((b + c + 1) / 2);
    var cd = (byte)((c + d + 1) / 2);
    var de = (byte)((d + e + 1) / 2);
    var rqp = (byte)((r + 2 * q + p + 2) / 4);
    var qpa = (byte)((q + 2 * p + a + 2) / 4);
    var pab = (byte)((p + 2 * a + b + 2) / 4);
    var abc = (byte)((a + 2 * b + c + 2) / 4);
    var bcd = (byte)((b + 2 * c + d + 2) / 4);
    var cde = (byte)((c + 2 * d + e + 2) / 4);
    _ybr[(y + 0) * 32 + x + 0] = ab;  _ybr[(y + 0) * 32 + x + 1] = bc;  _ybr[(y + 0) * 32 + x + 2] = cd;  _ybr[(y + 0) * 32 + x + 3] = de;
    _ybr[(y + 1) * 32 + x + 0] = pab; _ybr[(y + 1) * 32 + x + 1] = abc; _ybr[(y + 1) * 32 + x + 2] = bcd; _ybr[(y + 1) * 32 + x + 3] = cde;
    _ybr[(y + 2) * 32 + x + 0] = qpa; _ybr[(y + 2) * 32 + x + 1] = ab;  _ybr[(y + 2) * 32 + x + 2] = bc;  _ybr[(y + 2) * 32 + x + 3] = cd;
    _ybr[(y + 3) * 32 + x + 0] = rqp; _ybr[(y + 3) * 32 + x + 1] = pab; _ybr[(y + 3) * 32 + x + 2] = abc; _ybr[(y + 3) * 32 + x + 3] = bcd;
  }

  private void _PredFunc4LD(int y, int x) {
    int a = _ybr[(y - 1) * 32 + x + 0];
    int b = _ybr[(y - 1) * 32 + x + 1];
    int c = _ybr[(y - 1) * 32 + x + 2];
    int d = _ybr[(y - 1) * 32 + x + 3];
    int e = _ybr[(y - 1) * 32 + x + 4];
    int f = _ybr[(y - 1) * 32 + x + 5];
    int g = _ybr[(y - 1) * 32 + x + 6];
    int h = _ybr[(y - 1) * 32 + x + 7];
    var abc = (byte)((a + 2 * b + c + 2) / 4);
    var bcd = (byte)((b + 2 * c + d + 2) / 4);
    var cde = (byte)((c + 2 * d + e + 2) / 4);
    var def = (byte)((d + 2 * e + f + 2) / 4);
    var efg = (byte)((e + 2 * f + g + 2) / 4);
    var fgh = (byte)((f + 2 * g + h + 2) / 4);
    var ghh = (byte)((g + 2 * h + h + 2) / 4);
    _ybr[(y + 0) * 32 + x + 0] = abc; _ybr[(y + 0) * 32 + x + 1] = bcd; _ybr[(y + 0) * 32 + x + 2] = cde; _ybr[(y + 0) * 32 + x + 3] = def;
    _ybr[(y + 1) * 32 + x + 0] = bcd; _ybr[(y + 1) * 32 + x + 1] = cde; _ybr[(y + 1) * 32 + x + 2] = def; _ybr[(y + 1) * 32 + x + 3] = efg;
    _ybr[(y + 2) * 32 + x + 0] = cde; _ybr[(y + 2) * 32 + x + 1] = def; _ybr[(y + 2) * 32 + x + 2] = efg; _ybr[(y + 2) * 32 + x + 3] = fgh;
    _ybr[(y + 3) * 32 + x + 0] = def; _ybr[(y + 3) * 32 + x + 1] = efg; _ybr[(y + 3) * 32 + x + 2] = fgh; _ybr[(y + 3) * 32 + x + 3] = ghh;
  }

  private void _PredFunc4VL(int y, int x) {
    int a = _ybr[(y - 1) * 32 + x + 0];
    int b = _ybr[(y - 1) * 32 + x + 1];
    int c = _ybr[(y - 1) * 32 + x + 2];
    int d = _ybr[(y - 1) * 32 + x + 3];
    int e = _ybr[(y - 1) * 32 + x + 4];
    int f = _ybr[(y - 1) * 32 + x + 5];
    int g = _ybr[(y - 1) * 32 + x + 6];
    int h = _ybr[(y - 1) * 32 + x + 7];
    var ab = (byte)((a + b + 1) / 2);
    var bc = (byte)((b + c + 1) / 2);
    var cd = (byte)((c + d + 1) / 2);
    var de = (byte)((d + e + 1) / 2);
    var abc = (byte)((a + 2 * b + c + 2) / 4);
    var bcd = (byte)((b + 2 * c + d + 2) / 4);
    var cde = (byte)((c + 2 * d + e + 2) / 4);
    var def = (byte)((d + 2 * e + f + 2) / 4);
    var efg = (byte)((e + 2 * f + g + 2) / 4);
    var fgh = (byte)((f + 2 * g + h + 2) / 4);
    _ybr[(y + 0) * 32 + x + 0] = ab;  _ybr[(y + 0) * 32 + x + 1] = bc;  _ybr[(y + 0) * 32 + x + 2] = cd;  _ybr[(y + 0) * 32 + x + 3] = de;
    _ybr[(y + 1) * 32 + x + 0] = abc; _ybr[(y + 1) * 32 + x + 1] = bcd; _ybr[(y + 1) * 32 + x + 2] = cde; _ybr[(y + 1) * 32 + x + 3] = def;
    _ybr[(y + 2) * 32 + x + 0] = bc;  _ybr[(y + 2) * 32 + x + 1] = cd;  _ybr[(y + 2) * 32 + x + 2] = de;  _ybr[(y + 2) * 32 + x + 3] = efg;
    _ybr[(y + 3) * 32 + x + 0] = bcd; _ybr[(y + 3) * 32 + x + 1] = cde; _ybr[(y + 3) * 32 + x + 2] = def; _ybr[(y + 3) * 32 + x + 3] = fgh;
  }

  private void _PredFunc4HD(int y, int x) {
    int s = _ybr[(y + 3) * 32 + x - 1];
    int r = _ybr[(y + 2) * 32 + x - 1];
    int q = _ybr[(y + 1) * 32 + x - 1];
    int p = _ybr[(y + 0) * 32 + x - 1];
    int a = _ybr[(y - 1) * 32 + x - 1];
    int b = _ybr[(y - 1) * 32 + x + 0];
    int c = _ybr[(y - 1) * 32 + x + 1];
    int d = _ybr[(y - 1) * 32 + x + 2];
    var sr = (byte)((s + r + 1) / 2);
    var rq = (byte)((r + q + 1) / 2);
    var qp = (byte)((q + p + 1) / 2);
    var pa = (byte)((p + a + 1) / 2);
    var srq = (byte)((s + 2 * r + q + 2) / 4);
    var rqp = (byte)((r + 2 * q + p + 2) / 4);
    var qpa = (byte)((q + 2 * p + a + 2) / 4);
    var pab = (byte)((p + 2 * a + b + 2) / 4);
    var abc = (byte)((a + 2 * b + c + 2) / 4);
    var bcd = (byte)((b + 2 * c + d + 2) / 4);
    _ybr[(y + 0) * 32 + x + 0] = pa;  _ybr[(y + 0) * 32 + x + 1] = pab; _ybr[(y + 0) * 32 + x + 2] = abc; _ybr[(y + 0) * 32 + x + 3] = bcd;
    _ybr[(y + 1) * 32 + x + 0] = qp;  _ybr[(y + 1) * 32 + x + 1] = qpa; _ybr[(y + 1) * 32 + x + 2] = pa;  _ybr[(y + 1) * 32 + x + 3] = pab;
    _ybr[(y + 2) * 32 + x + 0] = rq;  _ybr[(y + 2) * 32 + x + 1] = rqp; _ybr[(y + 2) * 32 + x + 2] = qp;  _ybr[(y + 2) * 32 + x + 3] = qpa;
    _ybr[(y + 3) * 32 + x + 0] = sr;  _ybr[(y + 3) * 32 + x + 1] = srq; _ybr[(y + 3) * 32 + x + 2] = rq;  _ybr[(y + 3) * 32 + x + 3] = rqp;
  }

  private void _PredFunc4HU(int y, int x) {
    int s = _ybr[(y + 3) * 32 + x - 1];
    int r = _ybr[(y + 2) * 32 + x - 1];
    int q = _ybr[(y + 1) * 32 + x - 1];
    int p = _ybr[(y + 0) * 32 + x - 1];
    var pq = (byte)((p + q + 1) / 2);
    var qr = (byte)((q + r + 1) / 2);
    var rs = (byte)((r + s + 1) / 2);
    var pqr = (byte)((p + 2 * q + r + 2) / 4);
    var qrs = (byte)((q + 2 * r + s + 2) / 4);
    var rss = (byte)((r + 2 * s + s + 2) / 4);
    var sss = (byte)s;
    _ybr[(y + 0) * 32 + x + 0] = pq;  _ybr[(y + 0) * 32 + x + 1] = pqr; _ybr[(y + 0) * 32 + x + 2] = qr;  _ybr[(y + 0) * 32 + x + 3] = qrs;
    _ybr[(y + 1) * 32 + x + 0] = qr;  _ybr[(y + 1) * 32 + x + 1] = qrs; _ybr[(y + 1) * 32 + x + 2] = rs;  _ybr[(y + 1) * 32 + x + 3] = rss;
    _ybr[(y + 2) * 32 + x + 0] = rs;  _ybr[(y + 2) * 32 + x + 1] = rss; _ybr[(y + 2) * 32 + x + 2] = sss; _ybr[(y + 2) * 32 + x + 3] = sss;
    _ybr[(y + 3) * 32 + x + 0] = sss; _ybr[(y + 3) * 32 + x + 1] = sss; _ybr[(y + 3) * 32 + x + 2] = sss; _ybr[(y + 3) * 32 + x + 3] = sss;
  }

  // --- 8x8 predictors (chroma) ---

  private void _PredFunc8DC(int y, int x) {
    uint sum = 8;
    for (var i = 0; i < 8; ++i) sum += _ybr[(y - 1) * 32 + x + i];
    for (var j = 0; j < 8; ++j) sum += _ybr[(y + j) * 32 + x - 1];
    var avg = (byte)(sum / 16);
    for (var j = 0; j < 8; ++j)
      for (var i = 0; i < 8; ++i)
        _ybr[(y + j) * 32 + x + i] = avg;
  }

  private void _PredFunc8TM(int y, int x) {
    var delta0 = -_ybr[(y - 1) * 32 + x - 1];
    for (var j = 0; j < 8; ++j) {
      var delta1 = delta0 + _ybr[(y + j) * 32 + x - 1];
      for (var i = 0; i < 8; ++i) {
        var delta2 = delta1 + _ybr[(y - 1) * 32 + x + i];
        _ybr[(y + j) * 32 + x + i] = (byte)_Clip(delta2, 0, 255);
      }
    }
  }

  private void _PredFunc8VE(int y, int x) {
    for (var j = 0; j < 8; ++j)
      for (var i = 0; i < 8; ++i)
        _ybr[(y + j) * 32 + x + i] = _ybr[(y - 1) * 32 + x + i];
  }

  private void _PredFunc8HE(int y, int x) {
    for (var j = 0; j < 8; ++j)
      for (var i = 0; i < 8; ++i)
        _ybr[(y + j) * 32 + x + i] = _ybr[(y + j) * 32 + x - 1];
  }

  private void _PredFunc8DCTop(int y, int x) {
    uint sum = 4;
    for (var j = 0; j < 8; ++j) sum += _ybr[(y + j) * 32 + x - 1];
    var avg = (byte)(sum / 8);
    for (var j = 0; j < 8; ++j)
      for (var i = 0; i < 8; ++i)
        _ybr[(y + j) * 32 + x + i] = avg;
  }

  private void _PredFunc8DCLeft(int y, int x) {
    uint sum = 4;
    for (var i = 0; i < 8; ++i) sum += _ybr[(y - 1) * 32 + x + i];
    var avg = (byte)(sum / 8);
    for (var j = 0; j < 8; ++j)
      for (var i = 0; i < 8; ++i)
        _ybr[(y + j) * 32 + x + i] = avg;
  }

  private void _PredFunc8DCTopLeft(int y, int x) {
    for (var j = 0; j < 8; ++j)
      for (var i = 0; i < 8; ++i)
        _ybr[(y + j) * 32 + x + i] = 0x80;
  }

  // --- 16x16 predictors (luma) ---

  private void _PredFunc16DC(int y, int x) {
    uint sum = 16;
    for (var i = 0; i < 16; ++i) sum += _ybr[(y - 1) * 32 + x + i];
    for (var j = 0; j < 16; ++j) sum += _ybr[(y + j) * 32 + x - 1];
    var avg = (byte)(sum / 32);
    for (var j = 0; j < 16; ++j)
      for (var i = 0; i < 16; ++i)
        _ybr[(y + j) * 32 + x + i] = avg;
  }

  private void _PredFunc16TM(int y, int x) {
    var delta0 = -_ybr[(y - 1) * 32 + x - 1];
    for (var j = 0; j < 16; ++j) {
      var delta1 = delta0 + _ybr[(y + j) * 32 + x - 1];
      for (var i = 0; i < 16; ++i) {
        var delta2 = delta1 + _ybr[(y - 1) * 32 + x + i];
        _ybr[(y + j) * 32 + x + i] = (byte)_Clip(delta2, 0, 255);
      }
    }
  }

  private void _PredFunc16VE(int y, int x) {
    for (var j = 0; j < 16; ++j)
      for (var i = 0; i < 16; ++i)
        _ybr[(y + j) * 32 + x + i] = _ybr[(y - 1) * 32 + x + i];
  }

  private void _PredFunc16HE(int y, int x) {
    for (var j = 0; j < 16; ++j)
      for (var i = 0; i < 16; ++i)
        _ybr[(y + j) * 32 + x + i] = _ybr[(y + j) * 32 + x - 1];
  }

  private void _PredFunc16DCTop(int y, int x) {
    uint sum = 8;
    for (var j = 0; j < 16; ++j) sum += _ybr[(y + j) * 32 + x - 1];
    var avg = (byte)(sum / 16);
    for (var j = 0; j < 16; ++j)
      for (var i = 0; i < 16; ++i)
        _ybr[(y + j) * 32 + x + i] = avg;
  }

  private void _PredFunc16DCLeft(int y, int x) {
    uint sum = 8;
    for (var i = 0; i < 16; ++i) sum += _ybr[(y - 1) * 32 + x + i];
    var avg = (byte)(sum / 16);
    for (var j = 0; j < 16; ++j)
      for (var i = 0; i < 16; ++i)
        _ybr[(y + j) * 32 + x + i] = avg;
  }

  private void _PredFunc16DCTopLeft(int y, int x) {
    for (var j = 0; j < 16; ++j)
      for (var i = 0; i < 16; ++i)
        _ybr[(y + j) * 32 + x + i] = 0x80;
  }
}
