using System;

namespace FileFormat.Avif.Codec;

/// <summary>Loop-restoration unit parsing, AV1 5.11.57 read_lr() and read_lr_unit().</summary>
internal sealed partial class Av1TileDecoder {

  private static readonly int[] _WIENER_TAPS_MIN = [-5, -23, -17];
  private static readonly int[] _WIENER_TAPS_MAX = [10, 8, 46];
  private static readonly int[] _WIENER_TAPS_K = [1, 2, 3];
  private static readonly int[] _WIENER_TAPS_MID = [3, -7, 15];
  private static readonly int[] _SGR_XQD_MIN = [-96, -32];
  private static readonly int[] _SGR_XQD_MAX = [31, 95];
  private static readonly int[] _SGR_XQD_MID = [-32, 31];
  private const int _SGR_SUBEXP_K = 4;

  private void _ReadLoopRestoration(int miRow, int miCol, Av1BlockSize blockSize) {
    if (this._fh.AllowIntraBc || this._restoration is null)
      return;

    var w = Av1StructureTables.MiSizeWide[(int)blockSize];
    var h = Av1StructureTables.MiSizeHigh[(int)blockSize];

    for (var plane = 0; plane < this._numPlanes; ++plane) {
      if (this._fh.LrType[plane] == (int)Av1RestorationType.None)
        continue;

      var subX = this._frame.SubX[plane];
      var subY = this._frame.SubY[plane];
      var unitSize = this._fh.LoopRestorationSize[plane];
      var unitRows = this._restoration.UnitRows[plane];
      var unitCols = this._restoration.UnitCols[plane];

      var rowStep = _MI_SIZE >> subY;
      var colStep = _MI_SIZE >> subX;
      var unitRowStart = (miRow * rowStep + unitSize - 1) / unitSize;
      var unitRowEnd = Math.Min(unitRows, ((miRow + h) * rowStep + unitSize - 1) / unitSize);
      var unitColStart = (miCol * colStep + unitSize - 1) / unitSize;
      var unitColEnd = Math.Min(unitCols, ((miCol + w) * colStep + unitSize - 1) / unitSize);

      for (var unitRow = unitRowStart; unitRow < unitRowEnd; ++unitRow)
        for (var unitCol = unitColStart; unitCol < unitColEnd; ++unitCol)
          this._ReadLoopRestorationUnit(plane, unitRow * unitCols + unitCol);
    }
  }

  private void _ReadLoopRestorationUnit(int plane, int unit) {
    var units = this._restoration!;
    var frameType = (Av1RestorationType)this._fh.LrType[plane];

    var restorationType = frameType switch {
      Av1RestorationType.Wiener =>
        this._reader.ReadSymbol(this._cdf.WienerRestore, 0, 2) != 0
          ? Av1RestorationType.Wiener
          : Av1RestorationType.None,
      Av1RestorationType.SgrProj =>
        this._reader.ReadSymbol(this._cdf.SgrProjRestore, 0, 2) != 0
          ? Av1RestorationType.SgrProj
          : Av1RestorationType.None,
      _ => (Av1RestorationType)this._reader.ReadSymbol(this._cdf.SwitchableRestore, 0, 3),
    };

    units.Types[plane][unit] = (byte)restorationType;

    switch (restorationType) {
      case Av1RestorationType.Wiener:
        for (var pass = 0; pass < 2; ++pass) {
          var taps = pass == 0 ? units.WienerVertical[plane] : units.WienerHorizontal[plane];

          // Chroma uses the five-tap window, so its outermost tap is not coded and stays zero.
          var firstCoefficient = plane > 0 ? 1 : 0;
          if (firstCoefficient == 1)
            taps[unit * 3] = 0;

          for (var j = firstCoefficient; j < Av1Constants.WienerCoeffs; ++j) {
            var reference = this._refLrWiener[plane][pass * Av1Constants.WienerCoeffs + j];
            var value = this._reader.ReadSignedSubexpWithRef(
              _WIENER_TAPS_MIN[j], _WIENER_TAPS_MAX[j] + 1, _WIENER_TAPS_K[j], reference);
            taps[unit * 3 + j] = (short)value;
            this._refLrWiener[plane][pass * Av1Constants.WienerCoeffs + j] = value;
          }
        }
        break;

      case Av1RestorationType.SgrProj: {
        var set = (int)this._reader.ReadLiteral(Av1Constants.SgrProjSgrBits);
        units.SgrSet[plane][unit] = (byte)set;
        // Av1PostFilterTables.SgrParams follows libaom's sgr_params_type ({ int r[2]; int s[2]; }),
        // so the two radii are adjacent at +0 and +1; entries +2 and +3 are the precomputed
        // 1/(n*n*eps) scales. The specification's own Sgr_Params interleaves them as r0, e0, r1, e1,
        // which is why the radius of pass i is *not* at i * 2 here.
        var radius0 = Av1PostFilterTables.SgrParams[set * 4];
        var radius1 = Av1PostFilterTables.SgrParams[set * 4 + 1];

        int xqd0;
        int xqd1;
        if (radius0 == 0) {
          // With the first filter switched off its weight is fixed and only the second is coded.
          xqd0 = 0;
          xqd1 = this._reader.ReadSignedSubexpWithRef(
            _SGR_XQD_MIN[1], _SGR_XQD_MAX[1] + 1, _SGR_SUBEXP_K, this._refSgrXqd[plane][1]);
        } else if (radius1 == 0) {
          xqd0 = this._reader.ReadSignedSubexpWithRef(
            _SGR_XQD_MIN[0], _SGR_XQD_MAX[0] + 1, _SGR_SUBEXP_K, this._refSgrXqd[plane][0]);
          xqd1 = Math.Clamp((1 << Av1Constants.SgrProjPrecBits) - xqd0, _SGR_XQD_MIN[1], _SGR_XQD_MAX[1]);
        } else {
          xqd0 = this._reader.ReadSignedSubexpWithRef(
            _SGR_XQD_MIN[0], _SGR_XQD_MAX[0] + 1, _SGR_SUBEXP_K, this._refSgrXqd[plane][0]);
          xqd1 = this._reader.ReadSignedSubexpWithRef(
            _SGR_XQD_MIN[1], _SGR_XQD_MAX[1] + 1, _SGR_SUBEXP_K, this._refSgrXqd[plane][1]);
        }

        units.SgrXqd[plane][unit * 2] = (short)xqd0;
        units.SgrXqd[plane][unit * 2 + 1] = (short)xqd1;
        this._refSgrXqd[plane][0] = xqd0;
        this._refSgrXqd[plane][1] = xqd1;
        break;
      }
    }
  }
}
