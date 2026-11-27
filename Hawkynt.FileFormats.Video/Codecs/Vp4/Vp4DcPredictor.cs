namespace FileFormat.Codecs.Vp4;

/// <summary>
/// VP4's DC prediction, as the coefficients are read: FFmpeg's <c>vp4_dc_pred</c> and the
/// <c>vp4_dc_pred_before</c> / <c>_after</c> bookkeeping around each superblock.
/// </summary>
/// <remarks>
/// A coded block's DC is the mean of its two nearest coded neighbours of the same kind — intra,
/// inter, or golden — above and below first, then left and right, or the last DC of that kind when
/// fewer than two are there. The neighbours live in a 6x6 window round the superblock being read;
/// its left column is carried from the superblock before, including from the last superblock of the
/// row above into the first of the next, which is FFmpeg's behaviour and kept. Shared by the decoder
/// and the encoder, which has to predict exactly what the decoder will.
/// </remarks>
internal sealed class Vp4DcPredictor {

  /// <summary>No coded block of any kind.</summary>
  internal const int Undefined = 3;

  private readonly int[] _rowDc;
  private readonly int[] _rowType;
  private readonly int[] _dc = new int[36];
  private readonly int[] _type = new int[36];
  private readonly int[] _lastDc = new int[3];

  internal Vp4DcPredictor(int lumaSuperblockWidth) {
    this._rowDc = new int[lumaSuperblockWidth * 4];
    this._rowType = new int[lumaSuperblockWidth * 4];
  }

  /// <summary>Starts a picture: the last DC of every kind is nought.</summary>
  internal void BeginPicture() => System.Array.Clear(this._lastDc);

  /// <summary>Starts a plane <paramref name="fragmentWidth"/> blocks wide.</summary>
  internal void BeginPlane(int fragmentWidth) {
    for (var i = 0; i < fragmentWidth; ++i) {
      this._rowDc[i] = 0;
      this._rowType[i] = Undefined;
    }

    System.Array.Clear(this._dc);
    System.Array.Fill(this._type, Undefined);
  }

  internal void BeginSuperblock(int superblockX) {
    for (var i = 0; i < 4; ++i) {
      this._dc[i + 1] = this._rowDc[superblockX * 4 + i];
      this._type[i + 1] = this._rowType[superblockX * 4 + i];
    }

    for (var j = 1; j < 5; ++j)
      for (var i = 0; i < 4; ++i) {
        this._dc[j * 6 + i + 1] = 0;
        this._type[j * 6 + i + 1] = Undefined;
      }
  }

  internal void EndSuperblock(int superblockX) {
    for (var i = 0; i < 4; ++i) {
      this._rowDc[superblockX * 4 + i] = this._dc[4 * 6 + i + 1];
      this._rowType[superblockX * 4 + i] = this._type[4 * 6 + i + 1];
    }

    for (var i = 1; i < 5; ++i) {
      this._dc[i * 6] = this._dc[i * 6 + 4];
      this._type[i * 6] = this._type[i * 6 + 4];
    }
  }

  /// <summary>The prediction for the block at (<paramref name="hx"/>, <paramref name="hy"/>) of the superblock.</summary>
  internal int Predict(int hx, int hy, int type) {
    var at = (hy + 1) * 6 + hx + 1;
    var count = 0;
    var sum = 0;
    if (this._type[at - 6] == type) {
      sum += this._dc[at - 6];
      ++count;
    }

    if (this._type[at + 6] == type) {
      sum += this._dc[at + 6];
      ++count;
    }

    if (count != 2 && this._type[at - 1] == type) {
      sum += this._dc[at - 1];
      ++count;
    }

    if (count != 2 && this._type[at + 1] == type) {
      sum += this._dc[at + 1];
      ++count;
    }

    return count == 2 ? sum / 2 : this._lastDc[type];
  }

  /// <summary>Records the block's reconstructed DC.</summary>
  internal void Record(int hx, int hy, int type, int dc) {
    var at = (hy + 1) * 6 + hx + 1;
    this._type[at] = type;
    this._dc[at] = this._lastDc[type] = dc;
  }
}
