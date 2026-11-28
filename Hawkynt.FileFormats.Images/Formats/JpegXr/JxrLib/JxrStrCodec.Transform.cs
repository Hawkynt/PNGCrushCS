using static FileFormat.JpegXr.JxrLib.JxrTransform;

namespace FileFormat.JpegXr.JxrLib;

/// <summary>
/// Per macroblock inverse transform with the one-macroblock delay the overlap filters need: when called for
/// (Column, Row) it finishes the parts of the neighbourhood whose inputs are now complete.
/// </summary>
internal sealed partial class JxrStrCodec {

  /// <summary>Original (sub-version 0) operators with soft tile boundaries (jxrlib invTransformMacroblock).</summary>
  private void _InvTransformMacroblock() {
    var ol = this.SCP.Overlap;
    var cf = this.P.ColorFormat;
    var left = this.Column == 0;
    var right = this.Column == this.MbWidth;
    var top = this.Row == 0;
    var bottom = this.Row == this.MbHeight;
    var topOrBottom = top || bottom;
    var leftOrRight = left || right;
    var bottomOrRight = bottom || right;
    var channels = cf is JxrColorFormat.Yuv420 or JxrColorFormat.Yuv422 ? 1 : this.P.NumChannels;
    var hpAbsent = this.SCP.Subband is JxrSubband.NoHighpass or JxrSubband.DcOnly;
    var tile = this.Tiles[this.TileColumn];
    var m = this.Mem;
    int j, p;

    for (var i = 0; i < channels; ++i) {
      var p0 = this.P0[i];
      var p1 = this.P1[i];
      var hpQP = hpAbsent ? 255 : tile.QuantizerHP[i]![this.MB.QIndexHP].QP;

      if (!bottomOrRight) {
        IDCT4x4Stage2(m, p1);
        if (this.P.ScaledArith)
          NormalizeDec(m, p1, i != 0);
      }

      if (ol == JxrOverlap.Two) {
        if (leftOrRight && !topOrBottom) {
          j = left ? 0 : -128;
          Post4(m, p0 + j + 32, p0 + j + 48, p1 + j + 0, p1 + j + 16);
          Post4(m, p0 + j + 96, p0 + j + 112, p1 + j + 64, p1 + j + 80);
        }

        if (!leftOrRight) {
          if (topOrBottom) {
            p = top ? p1 : p0 + 32;
            Post4(m, p - 128, p - 64, p + 0, p + 64);
            Post4(m, p - 112, p - 48, p + 16, p + 80);
          } else
            Post4x4Stage2Split(m, p0, p1);
        }
      }

      if (!top)
        for (j = left ? 32 : -96; j < (right ? 32 : 160); j += 64) {
          IDCT4x4Stage1(m, p0 + j + 0);
          IDCT4x4Stage1(m, p0 + j + 16);
        }

      if (!bottom)
        for (j = left ? 0 : -128; j < (right ? 0 : 128); j += 64) {
          IDCT4x4Stage1(m, p1 + j + 0);
          IDCT4x4Stage1(m, p1 + j + 16);
        }

      if (ol == JxrOverlap.None)
        continue;

      if (leftOrRight) {
        j = left ? 0 + 10 : -64 + 14;
        if (!top) {
          p = p0 + 16 + j;
          Post4(m, p + 0, p - 2, p + 6, p + 8);
          Post4(m, p + 1, p - 1, p + 7, p + 9);
          Post4(m, p + 16, p + 14, p + 22, p + 24);
          Post4(m, p + 17, p + 15, p + 23, p + 25);
        }

        if (!bottom) {
          p = p1 + j;
          Post4(m, p + 0, p - 2, p + 6, p + 8);
          Post4(m, p + 1, p - 1, p + 7, p + 9);
        }

        if (!topOrBottom) {
          Post4(m, p0 + 48 + j + 0, p0 + 48 + j - 2, p1 - 10 + j, p1 - 8 + j);
          Post4(m, p0 + 48 + j + 1, p0 + 48 + j - 1, p1 - 9 + j, p1 - 7 + j);
        }
      }

      if (top)
        for (j = left ? 0 : -192; j < (right ? -64 : 64); j += 64) {
          p = p1 + j;
          Post4(m, p + 5, p + 4, p + 64, p + 65);
          Post4(m, p + 7, p + 6, p + 66, p + 67);
          Post4x4Stage1(m, p1 + j, 0, hpQP, hpAbsent);
        }
      else if (bottom)
        for (j = left ? 0 : -192; j < (right ? -64 : 64); j += 64) {
          Post4x4Stage1(m, p0 + 16 + j, 0, hpQP, hpAbsent);
          Post4x4Stage1(m, p0 + 32 + j, 0, hpQP, hpAbsent);
          p = p0 + 48 + j;
          Post4(m, p + 15, p + 14, p + 74, p + 75);
          Post4(m, p + 13, p + 12, p + 72, p + 73);
        }
      else
        for (j = left ? 0 : -192; j < (right ? -64 : 64); j += 64) {
          Post4x4Stage1(m, p0 + 16 + j, 0, hpQP, hpAbsent);
          Post4x4Stage1(m, p0 + 32 + j, 0, hpQP, hpAbsent);
          Post4x4Stage1Split(m, p0 + 48 + j, p1 + j, 0, hpQP, hpAbsent);
          Post4x4Stage1(m, p1 + j, 0, hpQP, hpAbsent);
        }
    }

    if (cf == JxrColorFormat.Yuv420)
      for (var i = 0; i < 2; ++i) {
        var p0 = this.P0[1 + i];
        var p1 = this.P1[1 + i];
        // jxrlib indexes the highpass quantiser by the loop counter here, i.e. channels 0 and 1
        var hpQP = hpAbsent ? 255 : tile.QuantizerHP[i]![this.MB.QIndexHP].QP;

        if (!bottomOrRight) {
          if (!this.P.ScaledArith)
            DCT2x2dn(m, p1, p1 + 32, p1 + 16, p1 + 48);
          else
            DCT2x2dnDec(m, p1, p1 + 32, p1 + 16, p1 + 48);
        }

        if (ol == JxrOverlap.Two) {
          if (leftOrRight && !topOrBottom) {
            j = left ? 0 : -32;
            Post2(m, p0 + j + 16, p1 + j);
          }

          if (!leftOrRight) {
            if (topOrBottom) {
              p = top ? p1 : p0 + 16;
              Post2(m, p - 32, p);
            } else
              Post2x2(m, p0 - 16, p0 + 16, p1 - 32, p1);
          }
        }

        if (!top)
          for (j = left ? 16 : -16; j < (right ? 16 : 48); j += 32)
            IDCT4x4Stage1(m, p0 + j);

        if (!bottom)
          for (j = left ? 0 : -32; j < (right ? 0 : 32); j += 32)
            IDCT4x4Stage1(m, p1 + j);

        if (ol == JxrOverlap.None)
          continue;

        if (!left && !top) {
          if (bottom)
            for (j = -48; j < (right ? -16 : 16); j += 32) {
              p = p0 + j;
              Post4(m, p + 15, p + 14, p + 42, p + 43);
              Post4(m, p + 13, p + 12, p + 40, p + 41);
            }
          else
            for (j = -48; j < (right ? -16 : 16); j += 32)
              Post4x4Stage1Split(m, p0 + j, p1 - 16 + j, 32, hpQP, hpAbsent);

          if (right) {
            if (!bottom) {
              Post4(m, p0 - 2, p0 - 4, p1 - 28, p1 - 26);
              Post4(m, p0 - 1, p0 - 3, p1 - 27, p1 - 25);
            }

            Post4(m, p0 - 18, p0 - 20, p0 - 12, p0 - 10);
            Post4(m, p0 - 17, p0 - 19, p0 - 11, p0 - 9);
          } else
            Post4x4Stage1(m, p0 - 32, 32, hpQP, hpAbsent);

          Post4x4Stage1(m, p0 - 64, 32, hpQP, hpAbsent);
        } else if (top) {
          for (j = left ? 0 : -64; j < (right ? -32 : 0); j += 32) {
            p = p1 + j + 4;
            Post4(m, p + 1, p + 0, p + 28, p + 29);
            Post4(m, p + 3, p + 2, p + 30, p + 31);
          }
        } else if (left) {
          if (!bottom) {
            Post4(m, p0 + 26, p0 + 24, p1 + 0, p1 + 2);
            Post4(m, p0 + 27, p0 + 25, p1 + 1, p1 + 3);
          }

          Post4(m, p0 + 10, p0 + 8, p0 + 16, p0 + 18);
          Post4(m, p0 + 11, p0 + 9, p0 + 17, p0 + 19);
        }
      }

    if (cf == JxrColorFormat.Yuv422)
      for (var i = 0; i < 2; ++i) {
        var p0 = this.P0[1 + i];
        var p1 = this.P1[1 + i];
        var hpQP = hpAbsent ? 255 : tile.QuantizerHP[i]![this.MB.QIndexHP].QP;

        if (!bottomOrRight && this.D.ThumbnailScale < 16) {
          // 1D lossless Haar of the two 4x2 chroma DC halves
          m[p1] -= (m[p1 + 32] + 1) >> 1;
          m[p1 + 32] += m[p1];
          if (!this.P.ScaledArith) {
            DCT2x2dn(m, p1 + 0, p1 + 64, p1 + 16, p1 + 80);
            DCT2x2dn(m, p1 + 32, p1 + 96, p1 + 48, p1 + 112);
          } else {
            DCT2x2dnDec(m, p1 + 0, p1 + 64, p1 + 16, p1 + 80);
            DCT2x2dnDec(m, p1 + 32, p1 + 96, p1 + 48, p1 + 112);
          }
        }

        if (ol == JxrOverlap.Two) {
          if (!bottom) {
            if (leftOrRight) {
              if (!top) {
                j = left ? 0 : -64;
                Post2(m, p0 + 48 + j, p1 + j);
              }

              j = left ? 16 : -48;
              Post2(m, p1 + j, p1 + j + 16);
            } else {
              if (top)
                Post2(m, p1 - 64, p1);
              else
                Post2x2(m, p0 - 16, p0 + 48, p1 - 64, p1);
              Post2x2(m, p1 - 48, p1 + 16, p1 - 32, p1 + 32);
            }
          } else if (!leftOrRight)
            Post2(m, p0 - 16, p0 + 48);
        }

        if (!top)
          for (j = left ? 48 : -16; j < (right ? 48 : 112); j += 64)
            IDCT4x4Stage1(m, p0 + j);

        if (!bottom)
          for (j = left ? 0 : -64; j < (right ? 0 : 64); j += 64) {
            IDCT4x4Stage1(m, p1 + j + 0);
            IDCT4x4Stage1(m, p1 + j + 16);
            IDCT4x4Stage1(m, p1 + j + 32);
          }

        if (ol == JxrOverlap.None)
          continue;

        if (!top) {
          if (leftOrRight) {
            j = left ? 32 + 10 : -32 + 14;
            p = p0 + j;
            Post4(m, p + 0, p - 2, p + 6, p + 8);
            Post4(m, p + 1, p - 1, p + 7, p + 9);
          }

          for (j = left ? 0 : -128; j < (right ? -64 : 0); j += 64)
            Post4x4Stage1(m, p0 + j + 32, 0, hpQP, hpAbsent);
        }

        if (!bottom) {
          if (leftOrRight) {
            j = left ? 0 + 10 : -64 + 14;
            p = p1 + j;
            Post4(m, p + 0, p - 2, p + 6, p + 8);
            Post4(m, p + 1, p - 1, p + 7, p + 9);
            p += 16;
            Post4(m, p + 0, p - 2, p + 6, p + 8);
            Post4(m, p + 1, p - 1, p + 7, p + 9);
          }

          for (j = left ? 0 : -128; j < (right ? -64 : 0); j += 64) {
            Post4x4Stage1(m, p1 + j + 0, 0, hpQP, hpAbsent);
            Post4x4Stage1(m, p1 + j + 16, 0, hpQP, hpAbsent);
          }
        }

        if (topOrBottom) {
          p = top ? p1 + 5 : p0 + 48 + 13;
          for (j = left ? 0 : -128; j < (right ? -64 : 0); j += 64) {
            Post4(m, p + j + 0, p + j - 1, p + j + 59, p + j + 60);
            Post4(m, p + j + 2, p + j + 1, p + j + 61, p + j + 62);
          }
        } else {
          if (leftOrRight) {
            j = left ? 0 + 0 : -64 + 4;
            Post4(m, p0 + j + 48 + 10 + 0, p0 + j + 48 + 10 - 2, p1 + j + 0, p1 + j + 2);
            Post4(m, p0 + j + 48 + 10 + 1, p0 + j + 48 + 10 - 1, p1 + j + 1, p1 + j + 3);
          }

          for (j = left ? 0 : -128; j < (right ? -64 : 0); j += 64)
            Post4x4Stage1Split(m, p0 + j + 48, p1 + j + 0, 0, hpQP, hpAbsent);
        }
      }
  }

  /// <summary>
  /// Altered operators of sub-versions 1 and 9, optionally with hard tile boundaries where the overlap filters
  /// treat tile edges like image edges (jxrlib invTransformMacroblock_alteredOperators_hard).
  /// </summary>
  private void _InvTransformMacroblockAlteredOperators() {
    var ol = this.SCP.Overlap;
    var cf = this.P.ColorFormat;
    var left = this.Column == 0;
    var right = this.Column == this.MbWidth;
    var top = this.Row == 0;
    var bottom = this.Row == this.MbHeight;
    var topOrBottom = top || bottom;
    var leftOrRight = left || right;
    var bottomOrRight = bottom || right;
    var leftAdjacentColumn = this.Column == 1;
    var rightAdjacentColumn = this.Column == this.MbWidth - 1;
    var channels = cf is JxrColorFormat.Yuv420 or JxrColorFormat.Yuv422 ? 1 : this.P.NumChannels;
    var scp = this.SCP;
    var m = this.Mem;
    int j, p;

    if (scp.UseHardTileBoundaries) {
      // jxrlib walks the column positions through TileY and the row positions through TileX
      if (this.Column == 0) {
        this.VertTileBoundary = false;
        this.HtTileY = 0;
      }

      this.OneMBLeftVertTB = this.OneMBRightVertTB = false;
      if (this.HtTileY > 0 && this.HtTileY <= scp.NumSliceMinus1H && this.Column - 1L == scp.TileY[this.HtTileY])
        this.OneMBRightVertTB = true;
      if (this.HtTileY < scp.NumSliceMinus1H && this.Column == scp.TileY[this.HtTileY + 1]) {
        this.VertTileBoundary = true;
        ++this.HtTileY;
      } else
        this.VertTileBoundary = false;
      if (this.HtTileY < scp.NumSliceMinus1H && this.Column + 1L == scp.TileY[this.HtTileY + 1])
        this.OneMBLeftVertTB = true;

      if (this.Row == 0) {
        this.HoriTileBoundary = false;
        this.HtTileX = 0;
      } else if (this.HtMbY != this.Row && this.HtTileX < scp.NumSliceMinus1V && this.Row == scp.TileX[this.HtTileX + 1]) {
        this.HoriTileBoundary = true;
        ++this.HtTileX;
      } else if (this.HtMbY != this.Row)
        this.HoriTileBoundary = false;
    } else {
      this.VertTileBoundary = false;
      this.HoriTileBoundary = false;
      this.OneMBLeftVertTB = false;
      this.OneMBRightVertTB = false;
    }

    this.HtMbX = this.Column;
    this.HtMbY = this.Row;
    var vtb = this.VertTileBoundary;
    var htb = this.HoriTileBoundary;
    var oneLeft = this.OneMBLeftVertTB;
    var oneRight = this.OneMBRightVertTB;

    for (var i = 0; i < channels; ++i) {
      var p0 = this.P0[i];
      var p1 = this.P1[i];

      if (!bottomOrRight) {
        IDCT4x4Stage2(m, p1);
        if (this.P.ScaledArith)
          NormalizeDec(m, p1, i != 0);
      }

      if (ol == JxrOverlap.Two) {
        if ((top || htb) && (left || vtb))
          Post4Alternate(m, p1 + 0, p1 + 64, p1 + 0 + 16, p1 + 64 + 16);
        if ((top || htb) && (right || vtb))
          Post4Alternate(m, p1 - 128, p1 - 64, p1 - 128 + 16, p1 - 64 + 16);
        if ((bottom || htb) && (left || vtb))
          Post4Alternate(m, p0 + 32, p0 + 96, p0 + 32 + 16, p0 + 96 + 16);
        if ((bottom || htb) && (right || vtb))
          Post4Alternate(m, p0 - 96, p0 - 32, p0 - 96 + 16, p0 - 32 + 16);

        if ((leftOrRight || vtb) && !topOrBottom && !htb) {
          if (left || vtb) {
            j = 0;
            Post4Alternate(m, p0 + j + 32, p0 + j + 48, p1 + j + 0, p1 + j + 16);
            Post4Alternate(m, p0 + j + 96, p0 + j + 112, p1 + j + 64, p1 + j + 80);
          }

          if (right || vtb) {
            j = -128;
            Post4Alternate(m, p0 + j + 32, p0 + j + 48, p1 + j + 0, p1 + j + 16);
            Post4Alternate(m, p0 + j + 96, p0 + j + 112, p1 + j + 64, p1 + j + 80);
          }
        }

        if (!leftOrRight) {
          if ((topOrBottom || htb) && !vtb) {
            if (top || htb) {
              p = p1;
              Post4Alternate(m, p - 128, p - 64, p + 0, p + 64);
              Post4Alternate(m, p - 112, p - 48, p + 16, p + 80);
            }

            if (bottom || htb) {
              p = p0 + 32;
              Post4Alternate(m, p - 128, p - 64, p + 0, p + 64);
              Post4Alternate(m, p - 112, p - 48, p + 16, p + 80);
            }
          }

          if (!topOrBottom && !htb && !vtb)
            Post4x4Stage2SplitAlternate(m, p0, p1);
        }
      }

      if (!top)
        for (j = left ? 32 : -96; j < (right ? 32 : 160); j += 64) {
          IDCT4x4Stage1(m, p0 + j + 0);
          IDCT4x4Stage1(m, p0 + j + 16);
        }

      if (!bottom)
        for (j = left ? 0 : -128; j < (right ? 0 : 128); j += 64) {
          IDCT4x4Stage1(m, p1 + j + 0);
          IDCT4x4Stage1(m, p1 + j + 16);
        }

      if (ol == JxrOverlap.None)
        continue;

      if (leftOrRight || vtb) {
        if ((top || htb) && (left || vtb))
          Post4Alternate(m, p1 + 0, p1 + 1, p1 + 2, p1 + 3);
        if ((top || htb) && (right || vtb))
          Post4Alternate(m, p1 - 59, p1 - 60, p1 - 57, p1 - 58);
        if ((bottom || htb) && (left || vtb))
          Post4Alternate(m, p0 + 48 + 10, p0 + 48 + 11, p0 + 48 + 8, p0 + 48 + 9);
        if ((bottom || htb) && (right || vtb))
          Post4Alternate(m, p0 - 1, p0 - 2, p0 - 3, p0 - 4);

        if (left || vtb)
          this._AlteredVerticalEdge(m, p0, p1, 0 + 10, top, bottom, topOrBottom, htb);
        if (right || vtb)
          this._AlteredVerticalEdge(m, p0, p1, -64 + 14, top, bottom, topOrBottom, htb);
      }

      if (top || htb)
        for (j = left ? 0 : -192; j < (right ? -64 : 64); j += 64) {
          if (vtb && j == -64)
            continue;
          p = p1 + j;
          Post4Alternate(m, p + 5, p + 4, p + 64, p + 65);
          Post4Alternate(m, p + 7, p + 6, p + 66, p + 67);
          Post4x4Stage1Alternate(m, p1 + j, 0);
        }

      if (bottom || htb)
        for (j = left ? 0 : -192; j < (right ? -64 : 64); j += 64) {
          if (vtb && j == -64)
            continue;
          Post4x4Stage1Alternate(m, p0 + 16 + j, 0);
          Post4x4Stage1Alternate(m, p0 + 32 + j, 0);
          p = p0 + 48 + j;
          Post4Alternate(m, p + 15, p + 14, p + 74, p + 75);
          Post4Alternate(m, p + 13, p + 12, p + 72, p + 73);
        }

      if (!top && !bottom && !htb)
        for (j = left ? 0 : -192; j < (right ? -64 : 64); j += 64) {
          if (vtb && j == -64)
            continue;
          Post4x4Stage1Alternate(m, p0 + 16 + j, 0);
          Post4x4Stage1Alternate(m, p0 + 32 + j, 0);
          Post4x4Stage1SplitAlternate(m, p0 + 48 + j, p1 + j, 0);
          Post4x4Stage1Alternate(m, p1 + j, 0);
        }
    }

    if (cf == JxrColorFormat.Yuv420)
      for (var i = 0; i < 2; ++i) {
        var p0 = this.P0[1 + i];
        var p1 = this.P1[1 + i];

        if (!bottomOrRight) {
          if (!this.P.ScaledArith)
            DCT2x2dn(m, p1, p1 + 32, p1 + 16, p1 + 48);
          else
            DCT2x2dnDec(m, p1, p1 + 32, p1 + 16, p1 + 48);
        }

        if (ol == JxrOverlap.Two) {
          if ((leftAdjacentColumn || oneRight) && (top || htb))
            m[p1 - 64 + 0] -= m[p1 - 64 + 32];
          if ((rightAdjacentColumn || oneLeft) && (top || htb))
            this.PredBefore[i, 0] = m[p1 + 0];
          if ((right || vtb) && (top || htb))
            m[p1 - 64 + 32] -= this.PredBefore[i, 0];
          if ((leftAdjacentColumn || oneRight) && (bottom || htb))
            m[p0 - 64 + 16] -= m[p0 - 64 + 48];
          if ((rightAdjacentColumn || oneLeft) && (bottom || htb))
            this.PredBefore[i, 1] = m[p0 + 16];
          if ((right || vtb) && (bottom || htb))
            m[p0 - 64 + 48] -= this.PredBefore[i, 1];

          if ((leftOrRight || vtb) && !topOrBottom && !htb) {
            if (left || vtb)
              Post2Alternate(m, p0 + 0 + 16, p1 + 0);
            if (right || vtb)
              Post2Alternate(m, p0 + -32 + 16, p1 + -32);
          }

          if (!leftOrRight) {
            if ((topOrBottom || htb) && !vtb) {
              if (top || htb)
                Post2Alternate(m, p1 - 32, p1);
              if (bottom || htb)
                Post2Alternate(m, p0 + 16 - 32, p0 + 16);
            } else if (!topOrBottom && !htb && !vtb)
              Post2x2Alternate(m, p0 - 16, p0 + 16, p1 - 32, p1);
          }

          if ((leftAdjacentColumn || oneRight) && (top || htb))
            m[p1 - 64 + 0] += m[p1 - 64 + 32];
          if ((rightAdjacentColumn || oneLeft) && (top || htb))
            this.PredAfter[i, 0] = m[p1 + 0];
          if ((right || vtb) && (top || htb))
            m[p1 - 64 + 32] += this.PredAfter[i, 0];
          if ((leftAdjacentColumn || oneRight) && (bottom || htb))
            m[p0 - 64 + 16] += m[p0 - 64 + 48];
          if ((rightAdjacentColumn || oneLeft) && (bottom || htb))
            this.PredAfter[i, 1] = m[p0 + 16];
          if ((right || vtb) && (bottom || htb))
            m[p0 - 64 + 48] += this.PredAfter[i, 1];
        }

        // the left column is transformed one macroblock late so the chroma corner operators can be undone first
        if (!top)
          for (j = left ? 48 : leftAdjacentColumn || oneRight ? -48 : -16; j < (right || vtb ? 16 : 48); j += 32)
            IDCT4x4Stage1(m, p0 + j);

        if (!bottom)
          for (j = left ? 32 : leftAdjacentColumn || oneRight ? -64 : -32; j < (right || vtb ? 0 : 32); j += 32)
            IDCT4x4Stage1(m, p1 + j);

        if (ol == JxrOverlap.None)
          continue;

        if ((top || htb) && (leftAdjacentColumn || oneRight))
          Post4Alternate(m, p1 - 64 + 0, p1 - 64 + 1, p1 - 64 + 2, p1 - 64 + 3);
        if ((top || htb) && (right || vtb))
          Post4Alternate(m, p1 - 27, p1 - 28, p1 - 25, p1 - 26);
        if ((bottom || htb) && (leftAdjacentColumn || oneRight))
          Post4Alternate(m, p0 - 64 + 16 + 10, p0 - 64 + 16 + 11, p0 - 64 + 16 + 8, p0 - 64 + 16 + 9);
        if ((bottom || htb) && (right || vtb))
          Post4Alternate(m, p0 - 1, p0 - 2, p0 - 3, p0 - 4);

        if (!left && !top) {
          if (leftAdjacentColumn || oneRight) {
            if (!bottom && !htb) {
              Post4Alternate(m, p0 - 64 + 26, p0 - 64 + 24, p1 - 64 + 0, p1 - 64 + 2);
              Post4Alternate(m, p0 - 64 + 27, p0 - 64 + 25, p1 - 64 + 1, p1 - 64 + 3);
            }

            Post4Alternate(m, p0 - 64 + 10, p0 - 64 + 8, p0 - 64 + 16, p0 - 64 + 18);
            Post4Alternate(m, p0 - 64 + 11, p0 - 64 + 9, p0 - 64 + 17, p0 - 64 + 19);
          }

          if (bottom || htb) {
            p = p0 + -48;
            Post4Alternate(m, p + 15, p + 14, p + 42, p + 43);
            Post4Alternate(m, p + 13, p + 12, p + 40, p + 41);
            if (!right && !vtb) {
              p = p0 + -16;
              Post4Alternate(m, p + 15, p + 14, p + 42, p + 43);
              Post4Alternate(m, p + 13, p + 12, p + 40, p + 41);
            }
          } else {
            Post4x4Stage1SplitAlternate(m, p0 + -48, p1 - 16 + -48, 32);
            if (!right && !vtb)
              Post4x4Stage1SplitAlternate(m, p0 + -16, p1 - 16 + -16, 32);
          }

          if (right || vtb) {
            if (!bottom && !htb) {
              Post4Alternate(m, p0 - 2, p0 - 4, p1 - 28, p1 - 26);
              Post4Alternate(m, p0 - 1, p0 - 3, p1 - 27, p1 - 25);
            }

            Post4Alternate(m, p0 - 18, p0 - 20, p0 - 12, p0 - 10);
            Post4Alternate(m, p0 - 17, p0 - 19, p0 - 11, p0 - 9);
          } else
            Post4x4Stage1Alternate(m, p0 - 32, 32);

          Post4x4Stage1Alternate(m, p0 - 64, 32);
        }

        if (top || htb) {
          if (!left) {
            p = p1 + -64 + 4;
            Post4Alternate(m, p + 1, p + 0, p + 28, p + 29);
            Post4Alternate(m, p + 3, p + 2, p + 30, p + 31);
          }

          if (!left && !right && !vtb) {
            p = p1 + -32 + 4;
            Post4Alternate(m, p + 1, p + 0, p + 28, p + 29);
            Post4Alternate(m, p + 3, p + 2, p + 30, p + 31);
          }
        }
      }

    if (cf == JxrColorFormat.Yuv422)
      for (var i = 0; i < 2; ++i) {
        var p0 = this.P0[1 + i];
        var p1 = this.P1[1 + i];

        if (!bottomOrRight && this.D.ThumbnailScale < 16) {
          m[p1] -= (m[p1 + 32] + 1) >> 1;
          m[p1 + 32] += m[p1];
          if (!this.P.ScaledArith) {
            DCT2x2dn(m, p1 + 0, p1 + 64, p1 + 16, p1 + 80);
            DCT2x2dn(m, p1 + 32, p1 + 96, p1 + 48, p1 + 112);
          } else {
            DCT2x2dnDec(m, p1 + 0, p1 + 64, p1 + 16, p1 + 80);
            DCT2x2dnDec(m, p1 + 32, p1 + 96, p1 + 48, p1 + 112);
          }
        }

        if (ol == JxrOverlap.Two) {
          if ((leftAdjacentColumn || oneRight) && (top || htb))
            m[p1 - 128 + 0] -= m[p1 - 128 + 64];
          if ((rightAdjacentColumn || oneLeft) && (top || htb))
            this.PredBefore[i, 0] = m[p1 + 0];
          if ((right || vtb) && (top || htb))
            m[p1 - 128 + 64] -= this.PredBefore[i, 0];
          if ((leftAdjacentColumn || oneRight) && (bottom || htb))
            m[p0 - 128 + 48] -= m[p0 - 128 + 112];
          if ((rightAdjacentColumn || oneLeft) && (bottom || htb))
            this.PredBefore[i, 1] = m[p0 + 48];
          if ((right || vtb) && (bottom || htb))
            m[p0 - 128 + 112] -= this.PredBefore[i, 1];

          if (!bottom) {
            if (leftOrRight || vtb) {
              if (!top && !htb) {
                if (left || vtb)
                  Post2Alternate(m, p0 + 48 + 0, p1 + 0);
                if (right || vtb)
                  Post2Alternate(m, p0 + 48 + -64, p1 + -64);
              }

              if (left || vtb)
                Post2Alternate(m, p1 + 16, p1 + 16 + 16);
              if (right || vtb)
                Post2Alternate(m, p1 + -48, p1 + -48 + 16);
            }

            if (!leftOrRight && !vtb) {
              if (top || htb)
                Post2Alternate(m, p1 - 64, p1);
              else
                Post2x2Alternate(m, p0 - 16, p0 + 48, p1 - 64, p1);
              Post2x2Alternate(m, p1 - 48, p1 + 16, p1 - 32, p1 + 32);
            }
          }

          if ((bottom || htb) && !leftOrRight && !vtb)
            Post2Alternate(m, p0 - 16, p0 + 48);

          if ((leftAdjacentColumn || oneRight) && (top || htb))
            m[p1 - 128 + 0] += m[p1 - 128 + 64];
          if ((rightAdjacentColumn || oneLeft) && (top || htb))
            this.PredAfter[i, 0] = m[p1 + 0];
          if ((right || vtb) && (top || htb))
            m[p1 - 128 + 64] += this.PredAfter[i, 0];
          if ((leftAdjacentColumn || oneRight) && (bottom || htb))
            m[p0 - 128 + 48] += m[p0 - 128 + 112];
          if ((rightAdjacentColumn || oneLeft) && (bottom || htb))
            this.PredAfter[i, 1] = m[p0 + 48];
          if ((right || vtb) && (bottom || htb))
            m[p0 - 128 + 112] += this.PredAfter[i, 1];
        }

        if (!top)
          for (j = left ? 112 : leftAdjacentColumn || oneRight ? -80 : -16; j < (right || vtb ? 48 : 112); j += 64)
            IDCT4x4Stage1(m, p0 + j);

        if (!bottom)
          for (j = left ? 64 : leftAdjacentColumn || oneRight ? -128 : -64; j < (right || vtb ? 0 : 64); j += 64) {
            IDCT4x4Stage1(m, p1 + j + 0);
            IDCT4x4Stage1(m, p1 + j + 16);
            IDCT4x4Stage1(m, p1 + j + 32);
          }

        if (ol == JxrOverlap.None)
          continue;

        if ((top || htb) && (leftAdjacentColumn || oneRight))
          Post4Alternate(m, p1 - 128 + 0, p1 - 128 + 1, p1 - 128 + 2, p1 - 128 + 3);
        if ((top || htb) && (right || vtb))
          Post4Alternate(m, p1 - 59, p1 - 60, p1 - 57, p1 - 58);
        if ((bottom || htb) && (leftAdjacentColumn || oneRight))
          Post4Alternate(m, p0 - 128 + 48 + 10, p0 - 128 + 48 + 11, p0 - 128 + 48 + 8, p0 - 128 + 48 + 9);
        if ((bottom || htb) && (right || vtb))
          Post4Alternate(m, p0 - 1, p0 - 2, p0 - 3, p0 - 4);

        if (!top) {
          if (leftAdjacentColumn || oneRight) {
            p = p0 + 32 + 10 - 128;
            Post4Alternate(m, p + 0, p - 2, p + 6, p + 8);
            Post4Alternate(m, p + 1, p - 1, p + 7, p + 9);
          }

          if (right || vtb) {
            p = p0 + -32 + 14;
            Post4Alternate(m, p + 0, p - 2, p + 6, p + 8);
            Post4Alternate(m, p + 1, p - 1, p + 7, p + 9);
          }

          for (j = left ? 0 : -128; j < (right || vtb ? -64 : 0); j += 64)
            Post4x4Stage1Alternate(m, p0 + j + 32, 0);
        }

        if (!bottom) {
          if (leftAdjacentColumn || oneRight) {
            p = p1 + 0 + 10 - 128;
            Post4Alternate(m, p + 0, p - 2, p + 6, p + 8);
            Post4Alternate(m, p + 1, p - 1, p + 7, p + 9);
            p += 16;
            Post4Alternate(m, p + 0, p - 2, p + 6, p + 8);
            Post4Alternate(m, p + 1, p - 1, p + 7, p + 9);
          }

          if (right || vtb) {
            p = p1 + -64 + 14;
            Post4Alternate(m, p + 0, p - 2, p + 6, p + 8);
            Post4Alternate(m, p + 1, p - 1, p + 7, p + 9);
            p += 16;
            Post4Alternate(m, p + 0, p - 2, p + 6, p + 8);
            Post4Alternate(m, p + 1, p - 1, p + 7, p + 9);
          }

          for (j = left ? 0 : -128; j < (right || vtb ? -64 : 0); j += 64) {
            Post4x4Stage1Alternate(m, p1 + j + 0, 0);
            Post4x4Stage1Alternate(m, p1 + j + 16, 0);
          }
        }

        if (topOrBottom || htb) {
          if (top || htb) {
            p = p1 + 5;
            for (j = left ? 0 : -128; j < (right || vtb ? -64 : 0); j += 64) {
              Post4Alternate(m, p + j + 0, p + j - 1, p + j + 59, p + j + 60);
              Post4Alternate(m, p + j + 2, p + j + 1, p + j + 61, p + j + 62);
            }
          }

          if (bottom || htb) {
            p = p0 + 48 + 13;
            for (j = left ? 0 : -128; j < (right || vtb ? -64 : 0); j += 64) {
              Post4Alternate(m, p + j + 0, p + j - 1, p + j + 59, p + j + 60);
              Post4Alternate(m, p + j + 2, p + j + 1, p + j + 61, p + j + 62);
            }
          }
        } else {
          if (leftAdjacentColumn || oneRight) {
            j = 0 + 0 - 128;
            Post4Alternate(m, p0 + j + 48 + 10 + 0, p0 + j + 48 + 10 - 2, p1 + j + 0, p1 + j + 2);
            Post4Alternate(m, p0 + j + 48 + 10 + 1, p0 + j + 48 + 10 - 1, p1 + j + 1, p1 + j + 3);
          }

          if (right || vtb) {
            j = -64 + 4;
            Post4Alternate(m, p0 + j + 48 + 10 + 0, p0 + j + 48 + 10 - 2, p1 + j + 0, p1 + j + 2);
            Post4Alternate(m, p0 + j + 48 + 10 + 1, p0 + j + 48 + 10 - 1, p1 + j + 1, p1 + j + 3);
          }

          for (j = left ? 0 : -128; j < (right || vtb ? -64 : 0); j += 64)
            Post4x4Stage1SplitAlternate(m, p0 + j + 48, p1 + j + 0, 0);
        }
      }
  }

  /// <summary>Vertical 1D edge filters of the luma first stage at a left (j = 10) or right (j = -50) image or tile edge.</summary>
  private void _AlteredVerticalEdge(int[] m, int p0, int p1, int j, bool top, bool bottom, bool topOrBottom, bool htb) {
    int p;
    if (!top) {
      p = p0 + 16 + j;
      Post4Alternate(m, p + 0, p - 2, p + 6, p + 8);
      Post4Alternate(m, p + 1, p - 1, p + 7, p + 9);
      Post4Alternate(m, p + 16, p + 14, p + 22, p + 24);
      Post4Alternate(m, p + 17, p + 15, p + 23, p + 25);
    }

    if (!bottom) {
      p = p1 + j;
      Post4Alternate(m, p + 0, p - 2, p + 6, p + 8);
      Post4Alternate(m, p + 1, p - 1, p + 7, p + 9);
    }

    if (!topOrBottom && !htb) {
      Post4Alternate(m, p0 + 48 + j + 0, p0 + 48 + j - 2, p1 - 10 + j, p1 - 8 + j);
      Post4Alternate(m, p0 + 48 + j + 1, p0 + 48 + j - 1, p1 - 9 + j, p1 - 7 + j);
    }
  }
}
