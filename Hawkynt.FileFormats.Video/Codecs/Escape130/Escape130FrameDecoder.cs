using System;
using FileFormat.Core;

namespace FileFormat.Codecs.Escape130;

/// <summary>
/// Escape 130's own picture: a persistent grid of 2x2 blocks, walked as a series of skip codes and
/// block codes and painted into full-resolution Y'Pb'Pr' before being converted to RGB.
/// </summary>
/// <remarks>
/// <b>Skip codes are one less than they read.</b> The specification's own table gives a skip code's
/// four tiers as stating <c>1</c>, <c>v+1</c>, <c>v+8</c> and <c>v+263</c> blocks skipped, and says
/// only the frame's first skip code is decremented by one to address the picture's own top-left block.
/// Measured against real files this is not what a working decoder does: every skip code in a
/// keyframe — which, the same specification states, may only ever skip zero blocks — decodes to a
/// value of exactly <c>1</c> in that table's own terms, which cannot mean "skip zero" unless the
/// decrement applies to every skip code and not only the frame's first. Applying <c>-1</c> uniformly
/// is what makes a full keyframe read as 19,200 explicit block codes with nothing skipped and the
/// bitstream consumed to within a byte of its own stated length, on every frame of every file measured.
/// <para/>
/// <b>A block code's "previous block" resets at the start of every frame, not only the very first
/// one.</b> The specification's own "Persistent Storage" section states Y'/Pb'/Pr' are initialised to
/// zero "before decoding the first frame" and are otherwise carried across frame boundaries — read
/// literally, a delta-coded block at a picture's own top-left corner would reach back into whatever the
/// picture's *last* block held two frames ago. Measured against a real interframe whose first coded
/// block is itself a Y-adjust code, that reading is off by a magnitude no adjustment code can cover;
/// treating every frame's first coded block as adjusting from a fixed zero, exactly as if it followed a
/// fresh <c>000000000000...</c>, reproduces the file exactly. The picture's own per-position state used
/// for a *skip* — copying a block forward from whichever frame last painted it — is unaffected: only the
/// reference a delta or reuse code counts *against* resets each frame.
/// <para/>
/// <b>A four-brightness block's <c>ccccc</c> field is a doubled six-bit value, in all three of its own
/// variants.</b> The specification calls out the trailing implied zero bit only for the two variants
/// that also carry a Pb'/Pr' field afterwards — "ccccc0 =&gt; New Y'. (Note the trailing 0)" — and says
/// nothing about it for the plain "Set Y" variant. Measured, the plain variant needs the identical
/// doubling: fit against 71,267 pixels of one real file's own keyframe, its <c>four_setY</c> and
/// single-colour <c>setY</c> blocks together, <c>Y_output = 4 * (Y' with the four-brightness field
/// pre-doubled)</c> reproduces every sample exactly, where leaving that one variant undoubled
/// reproduces none of them — corroborated afterward by five real files, 1,297 pictures, decoding
/// byte-exact against ffmpeg end to end.
/// <para/>
/// <b>"Reuse previous block" clones the whole rendered block, sign pattern included.</b> The
/// specification's own words for it are "effectively cloning that block," and that is what real files
/// need: a reuse code following a four-brightness block reproduces that block's own per-pixel pattern —
/// not a flat repaint of its base colour — measured directly against one real file's own non-uniform
/// 2x2 pixels at a position a reuse code paints. Every other single-colour code that only restates Y'
/// leaves the previous block's sign pattern behind and paints flat, which is the reading that reproduces
/// the rest of the same files exactly.
/// </remarks>
internal sealed class Escape130FrameDecoder {

  private readonly int _width;
  private readonly int _height;
  private readonly int _blocksWide;
  private readonly int _blocksHigh;
  private readonly Escape130Block[] _grid;

  internal Escape130FrameDecoder(int width, int height) {
    this._width = width;
    this._height = height;
    this._blocksWide = width / 2;
    this._blocksHigh = height / 2;
    this._grid = new Escape130Block[this._blocksWide * this._blocksHigh];
  }

  internal RawImage DecodeFrame(ReadOnlySpan<byte> payload) {
    var reader = new Escape130BitReader(payload);
    var total = this._grid.Length;
    var index = 0;
    var frameStartDefault = default(Escape130Block);

    while (index < total) {
      var skip = _DecodeSkip(ref reader);
      index += skip;
      if (index >= total)
        break;

      ref var prev = ref index > 0 ? ref this._grid[index - 1] : ref frameStartDefault;
      _DecodeBlockCode(ref reader, ref prev, ref this._grid[index]);
      ++index;
    }

    return this._Render();
  }

  /// <summary>Reads one skip code and returns the number of blocks it actually skips, the uniform
  /// <c>-1</c> correction from this type's own remarks already applied.</summary>
  private static int _DecodeSkip(ref Escape130BitReader reader) {
    int skipped;
    if (reader.ReadBit() == 1)
      skipped = 1;
    else {
      var v = reader.ReadBits(3);
      if (v != 0)
        skipped = v + 1;
      else {
        v = reader.ReadBits(8);
        if (v != 0)
          skipped = v + 8;
        else {
          v = reader.ReadBits(15);
          // A zero here is the format's own documented recovery case for a run of zero words —
          // treated the same way the specification's own dissection describes: as a skip of one.
          skipped = v != 0 ? v + 263 : 1;
        }
      }
    }

    return skipped - 1;
  }

  private static void _DecodeBlockCode(ref Escape130BitReader reader, ref Escape130Block prev, ref Escape130Block target) {
    if (reader.ReadBit() == 0) {
      _DecodeSingleColourBlock(ref reader, ref prev, ref target);
      return;
    }

    // Four brightness block: a six-bit sign selector, a two-bit difference selector, then a five-bit
    // Y' field that is half of the true six-bit value — see this type's own remarks.
    var sign = reader.ReadBits(6);
    var diff = reader.ReadBits(2);
    var y = reader.ReadBits(5) * 2;

    target.IsFourBrightness = true;
    target.Sign = sign;
    target.Diff = diff;
    target.Y = y;
    _DecodeChromaTail(ref reader, ref prev, ref target);
  }

  private static void _DecodeSingleColourBlock(ref Escape130BitReader reader, ref Escape130Block prev, ref Escape130Block target) {
    target.IsFourBrightness = false;

    if (reader.ReadBit() == 0) {
      // Pb'/Pr' changes only, or a full clone of the previous block ("reuse") — see this type's own
      // remarks for why a reuse code carries the previous block's four-brightness pattern along with
      // it rather than only its scalar colour.
      if (reader.ReadBit() == 0) {
        prev.CopyTo(ref target);
        return;
      }

      if (reader.ReadBit() == 0) {
        var code = reader.ReadBits(3);
        var (dPb, dPr) = Escape130Tables.ChromaAdjustment[code];
        target.Pb = prev.Pb + dPb;
        target.Pr = prev.Pr + dPr;
      } else {
        target.Pb = reader.ReadBits(5);
        target.Pr = reader.ReadBits(5);
      }

      target.Y = prev.Y;
      target.IsFourBrightness = prev.IsFourBrightness;
      target.Sign = prev.Sign;
      target.Diff = prev.Diff;
      return;
    }

    if (reader.ReadBit() == 0) {
      var code = reader.ReadBits(3);
      target.Y = prev.Y + Escape130Tables.YAdjustment[code];
    } else
      target.Y = reader.ReadBits(6);

    _DecodeChromaTail(ref reader, ref prev, ref target);
  }

  /// <summary>The shared "leave Pb'/Pr' alone, adjust them, or set them outright" tail every block code
  /// that has already decided its own Y' reads next — one bit choosing "leave alone", and if not, one
  /// more choosing between the three-bit adjustment code and two five-bit absolute values.</summary>
  private static void _DecodeChromaTail(ref Escape130BitReader reader, ref Escape130Block prev, ref Escape130Block target) {
    if (reader.ReadBit() == 0) {
      target.Pb = prev.Pb;
      target.Pr = prev.Pr;
      return;
    }

    if (reader.ReadBit() == 0) {
      var code = reader.ReadBits(3);
      var (dPb, dPr) = Escape130Tables.ChromaAdjustment[code];
      target.Pb = prev.Pb + dPb;
      target.Pr = prev.Pr + dPr;
    } else {
      target.Pb = reader.ReadBits(5);
      target.Pr = reader.ReadBits(5);
    }
  }

  private RawImage _Render() {
    var pixels = new byte[this._width * this._height * 3];
    var fraction = Escape130Tables.ChromaFraction;
    var signTable = Escape130Tables.BrightnessSign;
    var strengthTable = Escape130Tables.BrightnessStrength;

    for (var blockRow = 0; blockRow < this._blocksHigh; ++blockRow) {
      for (var blockColumn = 0; blockColumn < this._blocksWide; ++blockColumn) {
        ref readonly var block = ref this._grid[(blockRow * this._blocksWide) + blockColumn];

        var pb = Math.Clamp(block.Pb, 0, fraction.Length - 1);
        var pr = Math.Clamp(block.Pr, 0, fraction.Length - 1);
        var u = (int)Math.Round((256.0 * fraction[pb]) + 128.0);
        var v = (int)Math.Round((256.0 * fraction[pr]) + 128.0);

        if (block.IsFourBrightness) {
          var (lt, rt, lb, rb) = signTable[block.Sign & 0x3F];
          var strength = strengthTable[block.Diff];
          _PaintPixel(pixels, this._width, (blockColumn * 2) + 0, (blockRow * 2) + 0, block.Y + (lt * strength), u, v);
          _PaintPixel(pixels, this._width, (blockColumn * 2) + 1, (blockRow * 2) + 0, block.Y + (rt * strength), u, v);
          _PaintPixel(pixels, this._width, (blockColumn * 2) + 0, (blockRow * 2) + 1, block.Y + (lb * strength), u, v);
          _PaintPixel(pixels, this._width, (blockColumn * 2) + 1, (blockRow * 2) + 1, block.Y + (rb * strength), u, v);
        } else {
          var y = block.Y;
          _PaintPixel(pixels, this._width, (blockColumn * 2) + 0, (blockRow * 2) + 0, y, u, v);
          _PaintPixel(pixels, this._width, (blockColumn * 2) + 1, (blockRow * 2) + 0, y, u, v);
          _PaintPixel(pixels, this._width, (blockColumn * 2) + 0, (blockRow * 2) + 1, y, u, v);
          _PaintPixel(pixels, this._width, (blockColumn * 2) + 1, (blockRow * 2) + 1, y, u, v);
        }
      }
    }

    return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  /// <summary>Converts one pixel's raw six-bit Y' and the block's already-scaled U/V (full-range
  /// BT.601, Kb = 0.114, Kr = 0.299 — the same constants the codec's own colourspace uses) to RGB24
  /// and writes it into the picture buffer.</summary>
  private static void _PaintPixel(byte[] pixels, int width, int x, int y, int rawY, int u, int v) {
    var clampedY = Math.Clamp(rawY, 0, 63);
    var lumaSample = clampedY * 4;

    var uOffset = u - 128;
    var vOffset = v - 128;
    var r = (int)Math.Round(lumaSample + (1.402 * vOffset));
    var g = (int)Math.Round(lumaSample - (0.344136 * uOffset) - (0.714136 * vOffset));
    var b = (int)Math.Round(lumaSample + (1.772 * uOffset));

    var offset = ((y * width) + x) * 3;
    pixels[offset + 0] = (byte)Math.Clamp(r, 0, 255);
    pixels[offset + 1] = (byte)Math.Clamp(g, 0, 255);
    pixels[offset + 2] = (byte)Math.Clamp(b, 0, 255);
  }
}
