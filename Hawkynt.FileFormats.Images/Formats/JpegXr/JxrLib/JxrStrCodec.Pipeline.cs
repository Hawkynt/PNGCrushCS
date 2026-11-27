using System;
using System.Collections.Generic;

namespace FileFormat.JpegXr.JxrLib;

/// <summary>Macroblock-row pipeline: region setup, packet attachment, per-macroblock decode and row output.</summary>
internal sealed partial class JxrStrCodec {

  private static readonly int[] _CbChannels = [1, 1, 2, 2, 2, 4, 4, 4, -1, -1, -1];

  private IEnumerable<JxrStrCodec> _Planes() {
    yield return this;
    if (this.Next != null)
      yield return this.Next;
  }

  #region region of interest and output lookup tables

  private void _SetROI() {
    var ii = this.II;
    var scp = this.SCP;
    var p = this.P;
    var d = this.D;

    ii.Width -= p.ExtraPixelsLeft + p.ExtraPixelsRight;
    ii.Height -= p.ExtraPixelsTop + p.ExtraPixelsBottom;

    d.SkipFlexbits = scp.Subband == JxrSubband.NoFlexbits;
    d.DecodeHP = scp.Subband is JxrSubband.All or JxrSubband.NoFlexbits;
    d.DecodeLP = scp.Subband != JxrSubband.DcOnly;
    d.ThumbnailScale = 1;
    while ((long)d.ThumbnailScale * ii.ThumbnailWidth < ii.Width)
      d.ThumbnailScale <<= 1;

    if (scp.FrequencyOrder) {
      if (d.ThumbnailScale >= 4)
        d.DecodeHP = false;
      if (d.ThumbnailScale >= 16)
        d.DecodeLP = false;
    }

    ii.Width += p.ExtraPixelsLeft + p.ExtraPixelsRight;
    ii.Height += p.ExtraPixelsTop + p.ExtraPixelsBottom;

    d.RoiLeftX = ii.RoiLeftX * d.ThumbnailScale + p.ExtraPixelsLeft;
    d.RoiRightX = d.RoiLeftX + ii.RoiWidth * d.ThumbnailScale - 1;
    d.RoiTopY = ii.RoiTopY * d.ThumbnailScale + p.ExtraPixelsTop;
    d.RoiBottomY = d.RoiTopY + ii.RoiHeight * d.ThumbnailScale - 1;
    if (d.RoiRightX >= ii.Width)
      d.RoiRightX = ii.Width - 1;
    if (d.RoiBottomY >= ii.Height)
      d.RoiBottomY = ii.Height - 1;

    d.DecodeFullFrame = d.RoiLeftX + d.RoiTopY == 0
      && (d.RoiRightX + 15) / 16 >= (ii.Width + 14) / 16
      && (d.RoiBottomY + 15) / 16 >= (ii.Height + 14) / 16;

    ii.Width -= p.ExtraPixelsLeft + p.ExtraPixelsRight;
    ii.Height -= p.ExtraPixelsTop + p.ExtraPixelsBottom;

    if (scp.FrequencyOrder && ii.SkipFlexbits)
      d.SkipFlexbits = true;

    this.TileColumn = this.TileRow = 0;
  }

  /// <summary>Builds the column/row element offsets into the output buffer, honouring flips (jxrlib initLookupTables).</summary>
  private void _InitLookupTables() {
    var ii = this.II;
    var d = this.D;
    long w, h;
    if (d.ThumbnailScale > 1) {
      w = ii.ThumbnailWidth;
      h = ii.ThumbnailHeight;
    } else {
      w = ii.Width;
      h = ii.Height;
    }

    w += (d.RoiLeftX + d.ThumbnailScale - 1) / d.ThumbnailScale;
    h += (d.RoiTopY + d.ThumbnailScale - 1) / d.ThumbnailScale;

    long strideY = ii.BitDepth switch {
      JxrBitDepth.Bd16 or JxrBitDepth.Bd16S or JxrBitDepth.Bd5 or JxrBitDepth.Bd565 or JxrBitDepth.Bd16F => this.OutStride / 2,
      JxrBitDepth.Bd32 or JxrBitDepth.Bd32S or JxrBitDepth.Bd32F or JxrBitDepth.Bd10 => this.OutStride / 4,
      _ => this.OutStride,
    };

    long strideX;
    switch (ii.ColorFormat) {
      case JxrColorFormat.Yuv420:
        strideX = 6;
        w >>= 1;
        h >>= 1;
        break;
      case JxrColorFormat.Yuv422:
        strideX = 4;
        w >>= 1;
        break;
      default:
        strideX = (ii.BitsPerUnit >> 3) / _CbChannels[(int)ii.BitDepth];
        break;
    }

    if (ii.BitDepth is JxrBitDepth.Bd1 or JxrBitDepth.Bd5 or JxrBitDepth.Bd10 or JxrBitDepth.Bd565)
      strideX = 1;
    if (ii.Orientation > 3)
      (strideX, strideY) = (strideY, strideX);

    var o = ii.Orientation;
    var reverse = o is 2 or 3 or 5 or 7;
    long first = 0;
    if (!d.DecodeFullFrame)
      first = (d.RoiLeftX + d.ThumbnailScale - 1) / d.ThumbnailScale;
    d.OffsetX = new int[w];
    for (long i = 0; i + first < w; ++i) {
      var span = d.DecodeFullFrame ? w
        : (d.RoiRightX - d.RoiLeftX + d.ThumbnailScale) / d.ThumbnailScale / (ii.ColorFormat is JxrColorFormat.Yuv420 or JxrColorFormat.Yuv422 ? 2 : 1);
      d.OffsetX[i + first] = (int)(ii.LeadingPadding + (reverse ? span - 1 - i : i) * strideX);
    }

    reverse = o is 1 or 3 or 4 or 5;
    first = 0;
    if (!d.DecodeFullFrame)
      first = (d.RoiTopY + d.ThumbnailScale - 1) / d.ThumbnailScale;
    d.OffsetY = new int[h];
    for (long i = 0; i + first < h; ++i) {
      var span = d.DecodeFullFrame ? h
        : (d.RoiBottomY - d.RoiTopY + d.ThumbnailScale) / d.ThumbnailScale / (ii.ColorFormat == JxrColorFormat.Yuv420 ? 2 : 1);
      d.OffsetY[i + first] = (int)((reverse ? span - 1 - i : i) * strideY);
    }
  }

  #endregion

  #region macroblock row buffers

  private void _InitMRPtr() {
    foreach (var sc in this._Planes()) {
      Array.Copy(sc.A0, sc.P0, JxrTables.MaxChannels);
      Array.Copy(sc.A1, sc.P1, JxrTables.MaxChannels);
    }
  }

  private void _AdvanceMRPtr() {
    var cpChroma = JxrTables.CblkChromas[(int)this.P.ColorFormat] * 16;
    foreach (var sc in this._Planes()) {
      var cpStride = 16 * 16;
      for (var i = 0; i < sc.P.NumChannels; ++i) {
        sc.P0[i] += cpStride;
        sc.P1[i] += cpStride;
        cpStride = cpChroma;
      }
    }
  }

  private void _AdvanceOneMBRow() {
    foreach (var sc in this._Planes())
      for (var i = 0; i < sc.P.NumChannels; ++i)
        (sc.PredInfo[i], sc.PredInfoPrevRow[i]) = (sc.PredInfoPrevRow[i], sc.PredInfo[i]);
  }

  private void _SwapMRPtr() {
    foreach (var sc in this._Planes())
      for (var i = 0; i < JxrTables.MaxChannels; ++i)
        (sc.A0[i], sc.A1[i]) = (sc.A1[i], sc.A0[i]);
  }

  #endregion

  #region decode driver

  /// <summary>Decodes macroblock row <paramref name="mbRow"/> and outputs the row above it (jxrlib ImageStrDecDecode in re-entrant mode).</summary>
  internal int DecodeRow(int mbRow, byte[] output, int lines, int stride) {
    var next = this.Next;
    this.Out = output;
    this.OutLines = lines;
    this.OutStride = stride;
    this.LinesDecoded = 0;

    if (mbRow == 0) {
      this._SetROI();
      if (next != null) {
        next.Out = output;
        next.OutLines = lines;
        next.OutStride = stride;
        next._SetROI();
      }

      this._InitLookupTables();
      next?._InitLookupTables();
    }

    var chromaElements = this.P.ColorFormat == JxrColorFormat.Yuv420 ? 8 * 8
      : this.P.ColorFormat == JxrColorFormat.Yuv422 ? 8 * 16 : 16 * 16;

    this.Row = mbRow;
    this.Column = 0;
    this._InitMRPtr();

    Array.Clear(this.Mem, this.P1[0], 16 * 16 * this.MbWidth);
    for (var k = 1; k < this.P.NumChannels; ++k)
      Array.Clear(this.Mem, this.P1[k], chromaElements * this.MbWidth);
    if (next != null)
      Array.Clear(next.Mem, next.P1[0], 16 * 16 * next.MbWidth);

    this._ProcessMacroblock();
    this._AdvanceMRPtr();
    for (this.Column = 1; this.Column < this.MbWidth; ++this.Column) {
      this._ProcessMacroblock();
      this._AdvanceMRPtr();
    }

    this._ProcessMacroblock();

    if (this.Row != 0) {
      var d = this.D;
      if (d.ThumbnailScale < 2 && (d.DecodeFullFrame || ((long)this.Row * 16 > d.RoiTopY && (long)this.Row * 16 <= d.RoiBottomY + 16)))
        this._OutputMBRow();
    }

    this._AdvanceOneMBRow();
    this._SwapMRPtr();
    return this.LinesDecoded;
  }

  private void _GetTilePos(int mbX, int mbY) {
    var scp = this.SCP;
    if (mbX == 0)
      this.TileColumn = 0;
    else if (this.TileColumn < scp.NumSliceMinus1V && mbX == scp.TileX[this.TileColumn + 1])
      ++this.TileColumn;

    if (mbY == 0)
      this.TileRow = 0;
    else if (this.TileRow < scp.NumSliceMinus1H && mbY == scp.TileY[this.TileRow + 1])
      ++this.TileRow;

    this.CtxLeft = mbX == scp.TileX[this.TileColumn];
    this.CtxTop = mbY == scp.TileY[this.TileRow];
    this.ResetContext = this.ResetRGITotals = ((mbX - scp.TileX[this.TileColumn]) & 0xf) == 0;
    if (this.TileColumn == scp.NumSliceMinus1V) {
      if (mbX + 1 == this.MbWidth)
        this.ResetContext = true;
    } else if (mbX + 1 == scp.TileX[this.TileColumn + 1])
      this.ResetContext = true;
  }

  /// <summary>Entropy decodes and reconstructs one macroblock of every plane, then runs the delayed inverse transform.</summary>
  private void _ProcessMacroblock() {
    var bottom = this.Row == this.MbHeight;
    var bottomOrRight = bottom || this.Column == this.MbWidth;
    var d = this.D;

    foreach (var sc in this._Planes()) {
      if (sc != this) {
        sc.Row = this.Row;
        sc.Column = this.Column;
      }

      if (!bottomOrRight) {
        sc._GetTilePos(sc.Column, sc.Row);
        if (sc.Next != null) {
          sc.Next.TileColumn = sc.TileColumn;
          sc.Next.TileRow = sc.TileRow;
        }
        var ctx = sc.Contexts[sc.TileColumn];
        sc._ReadPackets();

        sc._DecodeMacroblockDC(ctx);
        if (d.DecodeLP)
          sc._DecodeMacroblockLowpass(ctx);
        sc._PredDCACDec();
        sc._DequantizeMacroblock();
        if (d.DecodeHP) {
          sc._DecodeMacroblockHighpass(ctx);
          sc._PredACDec();
        }

        sc._UpdatePredInfo();
      }

      if (sc.P.SubVersion == _SUBVERSION)
        sc._InvTransformMacroblock();
      else
        sc._InvTransformMacroblockAlteredOperators();
    }
  }

  #endregion

  #region packets and tile headers

  /// <summary>Packet header: start code 00 00 01 and a byte of packet type / tile id; reading stops at the first mismatch.</summary>
  private static bool _TryReadPacketHeader(JxrBitReader io) {
    if (io.Get(8) != 0 || io.Get(8) != 0 || io.Get(8) != 1)
      return false;
    io.Get(8);
    return true;
  }

  private static void _ReadPacketHeader(JxrBitReader io) {
    if (!_TryReadPacketHeader(io))
      throw _Bad("packet start code missing");
  }

  private void _ReadPackets() {
    var scp = this.SCP;
    if (this.Column == 0 && this.Row == scp.TileY[this.TileRow]) {
      if (this.Secondary) {
        if (this.NumBitIO > 0)
          for (var k = 0; k <= scp.NumSliceMinus1V; ++k)
            _ResetCodingContext(this.Contexts[k]);
        else
          _ResetCodingContext(this.Contexts[0]);
      } else {
        for (var k = 0; k < this.NumBitIO; ++k) {
          if (this.TileRow > 0)
            this.BitIO[k].Detach();
          var idx = (long)this.NumBitIO * this.TileRow + k;
          this.BitIO[k].Attach(this.IndexTable[idx] + this.HeaderSize);
        }

        if (this.NumBitIO == 0) {
          this.IOHeader.Detach();
          this.IOHeader.Attach(this.HeaderSize);
        }

        for (var k = 0; k <= scp.NumSliceMinus1V; ++k) {
          if (!scp.FrequencyOrder) {
            var io = this.NumBitIO == 0 ? this.IOHeader : this.BitIO[k];
            if (!io.Attached)
              throw _Bad("packet not attached");
            _ReadPacketHeader(io);
            this.Contexts[k].TrimFlexBits = this.P.TrimFlexbitsFlag ? (int)io.Get(4) : 0;
          } else {
            var b = k * this.CSB;
            _ReadPacketHeader(this.BitIO[b]);
            if (this.CSB > 1)
              _ReadPacketHeader(this.BitIO[b + 1]);
            if (this.CSB > 2)
              _ReadPacketHeader(this.BitIO[b + 2]);
            if (this.CSB > 3) {
              var fl = this.BitIO[b + 3];
              _TryReadPacketHeader(fl); // a damaged flexbits packet header is tolerated
              this.Contexts[k].TrimFlexBits = this.P.TrimFlexbitsFlag ? (int)fl.Get(4) : 0;
            }
          }

          _ResetCodingContext(this.Contexts[k]);
        }
      }
    }

    if (this.CtxLeft && this.CtxTop && !this.Secondary) {
      var ctx = this.Contexts[this.TileColumn];
      this._ReadTileHeaderDC(ctx.IODC!);
      this.Next?._ReadTileHeaderDC(ctx.IODC!);
      if (this.CSB > 1) {
        this._ReadTileHeaderLP(ctx.IOLP!);
        this.Next?._ReadTileHeaderLP(ctx.IOLP!);
      }

      if (this.CSB > 2) {
        this._ReadTileHeaderHP(ctx.IOAC!);
        this.Next?._ReadTileHeaderHP(ctx.IOAC!);
      }
    }
  }

  private static int _ReadQuantizer(JxrQuantizer[]?[] q, JxrBitReader io, int channels, int pos) {
    var chMode = 0;
    if (channels > 1)
      chMode = (int)io.Get(2);
    q[0]![pos].Index = (byte)io.Get(8);
    if (chMode == 1)
      q[1]![pos].Index = (byte)io.Get(8);
    else if (chMode > 0)
      for (var i = 1; i < channels; ++i)
        q[i]![pos].Index = (byte)io.Get(8);
    return chMode;
  }

  private void _ReadTileHeaderDC(JxrBitReader io) {
    if ((this.P.QpMode & 1) == 0)
      return;
    var tile = this.Tiles[this.TileColumn];
    if (this.TileRow + this.TileColumn == 0)
      for (var t = 0; t <= this.SCP.NumSliceMinus1V; ++t)
        _AllocateQuantizer(this.Tiles[t].QuantizerDC, this.P.NumChannels, 1);
    tile.ChModeDC = _ReadQuantizer(tile.QuantizerDC, io, this.P.NumChannels, 0);
    _FormatQuantizer(tile.QuantizerDC, tile.ChModeDC, this.P.NumChannels, 0, true, this.P.ScaledArith);
  }

  private void _ReadTileHeaderLP(JxrBitReader io) {
    if (this.SCP.Subband == JxrSubband.DcOnly || (this.P.QpMode & 2) == 0)
      return;
    var tile = this.Tiles[this.TileColumn];
    tile.UseDC = io.Get(1) == 1;
    tile.BitsLP = 0;
    tile.NumQPLP = 1;
    if (tile.UseDC) {
      _AllocateQuantizer(tile.QuantizerLP, this.P.NumChannels, tile.NumQPLP);
      this._UseDCQuantizer(this.TileColumn);
    } else {
      tile.NumQPLP = (int)io.Get(4) + 1;
      tile.BitsLP = _DquantBits(tile.NumQPLP);
      _AllocateQuantizer(tile.QuantizerLP, this.P.NumChannels, tile.NumQPLP);
      for (var i = 0; i < tile.NumQPLP; ++i) {
        tile.ChModeLP[i] = _ReadQuantizer(tile.QuantizerLP, io, this.P.NumChannels, i);
        _FormatQuantizer(tile.QuantizerLP, tile.ChModeLP[i], this.P.NumChannels, i, true, this.P.ScaledArith);
      }
    }
  }

  private void _ReadTileHeaderHP(JxrBitReader io) {
    if (this.SCP.Subband is JxrSubband.DcOnly or JxrSubband.NoHighpass || (this.P.QpMode & 4) == 0)
      return;
    var tile = this.Tiles[this.TileColumn];
    tile.UseLP = io.Get(1) == 1;
    tile.BitsHP = 0;
    tile.NumQPHP = 1;
    if (tile.UseLP) {
      tile.NumQPHP = tile.NumQPLP;
      _AllocateQuantizer(tile.QuantizerHP, this.P.NumChannels, tile.NumQPHP);
      this._UseLPQuantizer(tile.NumQPHP, this.TileColumn);
    } else {
      tile.NumQPHP = (int)io.Get(4) + 1;
      tile.BitsHP = _DquantBits(tile.NumQPHP);
      _AllocateQuantizer(tile.QuantizerHP, this.P.NumChannels, tile.NumQPHP);
      for (var i = 0; i < tile.NumQPHP; ++i) {
        tile.ChModeHP[i] = _ReadQuantizer(tile.QuantizerHP, io, this.P.NumChannels, i);
        _FormatQuantizer(tile.QuantizerHP, tile.ChModeHP[i], this.P.NumChannels, i, false, this.P.ScaledArith);
      }
    }
  }

  #endregion
}
