using System;
using System.IO;

namespace FileFormat.Codecs.Roq;

/// <summary>
/// Walks a <c>QUAD_VQ</c> chunk's quadtree and paints one picture from it: vector quantisation over
/// 8x8, 4x4 and 2x2 blocks, motion compensation, and a skip code that means something unusual here.
/// </summary>
/// <remarks>
/// The picture is cut into 16x16 macroblocks, each four 8x8 quadrants in reading order — top left, top
/// right, bottom left, bottom right — and each quadrant carries a two-bit code read from a stream of
/// sixteen-bit words shared across the whole chunk, most significant pair first. <c>SLD</c> subdivides
/// no further and paints straight from the codebook; <c>CCC</c> subdivides the quadrant into four 4x4
/// blocks in the same reading order, each carrying its own two-bit code from the same stream, where
/// <c>SLD</c> now means a 4x4 codebook cell used at its own size rather than doubled, and <c>CCC</c>
/// means something different again — four raw 2x2 cell indices with no code of their own, the walk's
/// only terminal case. <c>FCC</c> is motion compensation at whichever size its level codes, one
/// argument byte packing two signed nibbles that are added to the chunk's own mean vector.
/// <para/>
/// <b>What <c>MOT</c> means, measured rather than guessed.</b> The published description calls it
/// "skip, no change" and every source agrees it costs no argument byte — both true, but neither says
/// skip relative to *what*, and treating it as "leave this block equal to the frame before" reproduces
/// three real files' first two frames exactly and then drifts, worse where the chunk states a nonzero
/// mean vector, in a pattern that only ever heals itself when a later frame recodes the same area
/// fresh. Bisecting one wrong block against ffmpeg's own decode of the frame before it found the true
/// source sixteen samples of a match nowhere near the block sat two frames back, not one — which is
/// what a RoQ encoder's double-buffered picture store does: two picture buffers alternate being the one
/// currently written, and <c>MOT</c> writes nothing at all, so a block a frame does not touch keeps
/// whichever content that same buffer slot held the last time *it* was written, which is two frames
/// earlier whenever the block between was itself untouched, three years earlier in encoder-real-time if
/// every frame since skipped it. <c>FCC</c>, <c>SLD</c> and <c>CCC</c> all write into the buffer being
/// built and all read the *other* one — the most recently completed picture — which is the ordinary,
/// single-step-back reference every other block type in this format uses. The one wrinkle a decoder
/// with two buffers and no third has to handle by hand: the very first picture has no second buffer to
/// have been building into two frames ago, so after it is painted its result is copied into both
/// buffer slots, which is what makes its own <c>MOT</c> blocks — should the first picture ever code one
/// — mean "black," the state nothing has painted yet, exactly as they do for the strip-based formats
/// that share this project's block-vector idiom. Measured against three files spanning 1 338 frames —
/// including the one sample whose accompanying note calls out this exact class of bug in the game's
/// own player — every plane of every frame is bit-identical to ffmpeg's decode of the same file once
/// this reading is used, and every one of them differs, growing without bound, under the "one frame
/// back" reading the published description would suggest.
/// </remarks>
internal static class RoqPictureDecoder {

  private const int _MACROBLOCK = 16;

  internal static void Decode(ReadOnlySpan<byte> data, RoqCodebook codebook, int meanX, int meanY, RoqFrame reference, RoqFrame target) {
    var width = target.Width;
    var height = target.Height;
    var bits = new _CodeReader(data);
    var macroblocksAcross = width / _MACROBLOCK;
    var macroblocksDown = height / _MACROBLOCK;

    for (var macroblockY = 0; macroblockY < macroblocksDown; ++macroblockY) {
      var blockTop = macroblockY * _MACROBLOCK;
      for (var macroblockX = 0; macroblockX < macroblocksAcross; ++macroblockX) {
        var blockLeft = macroblockX * _MACROBLOCK;

        _Quadrant(ref bits, codebook, meanX, meanY, reference, target, blockLeft, blockTop, width, height);
        _Quadrant(ref bits, codebook, meanX, meanY, reference, target, blockLeft + 8, blockTop, width, height);
        _Quadrant(ref bits, codebook, meanX, meanY, reference, target, blockLeft, blockTop + 8, width, height);
        _Quadrant(ref bits, codebook, meanX, meanY, reference, target, blockLeft + 8, blockTop + 8, width, height);
      }
    }
  }

  /// <summary>One 8x8 quadrant of a macroblock: skip, motion, one codebook cell doubled to fill it,
  /// or a subdivision into four 4x4 blocks.</summary>
  private static void _Quadrant(
    ref _CodeReader bits, RoqCodebook codebook, int meanX, int meanY,
    RoqFrame reference, RoqFrame target, int x, int y, int width, int height) {
    switch (bits.NextCode()) {
      case 0: // MOT: leave the target's own stale content in place.
        return;
      case 1: {
        var (dx, dy) = _MotionVector(bits.NextByte(), meanX, meanY);
        _CopyBlock(reference, target, x, y, 8, dx, dy, width, height);
        return;
      }
      case 2: {
        var index = bits.NextByte();
        if (index >= codebook.Cb4Count)
          throw new InvalidDataException($"A RoQ SLD code names 4x4 cell {index}, and the codebook states only {codebook.Cb4Count}.");
        _PaintCb4Upsampled(codebook, target, x, y, index);
        return;
      }
      default: // CCC: subdivide into four 4x4 blocks, each with its own code.
        _SubBlock(ref bits, codebook, meanX, meanY, reference, target, x, y, width, height);
        _SubBlock(ref bits, codebook, meanX, meanY, reference, target, x + 4, y, width, height);
        _SubBlock(ref bits, codebook, meanX, meanY, reference, target, x, y + 4, width, height);
        _SubBlock(ref bits, codebook, meanX, meanY, reference, target, x + 4, y + 4, width, height);
        return;
    }
  }

  /// <summary>One 4x4 block reached by subdividing an 8x8 quadrant: skip, motion, one codebook cell
  /// at its own size, or four raw 2x2 cells with no code of their own.</summary>
  private static void _SubBlock(
    ref _CodeReader bits, RoqCodebook codebook, int meanX, int meanY,
    RoqFrame reference, RoqFrame target, int x, int y, int width, int height) {
    switch (bits.NextCode()) {
      case 0:
        return;
      case 1: {
        var (dx, dy) = _MotionVector(bits.NextByte(), meanX, meanY);
        _CopyBlock(reference, target, x, y, 4, dx, dy, width, height);
        return;
      }
      case 2: {
        var index = bits.NextByte();
        if (index >= codebook.Cb4Count)
          throw new InvalidDataException($"A RoQ SLD code names 4x4 cell {index}, and the codebook states only {codebook.Cb4Count}.");
        _PaintCb4Native(codebook, target, x, y, index);
        return;
      }
      default:
        _PaintCb2Checked(codebook, target, x, y, bits.NextByte());
        _PaintCb2Checked(codebook, target, x + 2, y, bits.NextByte());
        _PaintCb2Checked(codebook, target, x, y + 2, bits.NextByte());
        _PaintCb2Checked(codebook, target, x + 2, y + 2, bits.NextByte());
        return;
    }
  }

  /// <summary>The motion vector an <c>FCC</c> code states: two signed nibbles, high nibble first,
  /// each added to the chunk's own mean vector.</summary>
  private static (int Dx, int Dy) _MotionVector(byte argument, int meanX, int meanY) {
    var dx = meanX + ((argument >> 4) & 0xF) - 8;
    var dy = meanY + (argument & 0xF) - 8;
    return (dx, dy);
  }

  /// <summary>Copies an <c>n</c>x<c>n</c> block, all three planes, from the reference picture to the
  /// target at a motion-shifted position.</summary>
  private static void _CopyBlock(RoqFrame reference, RoqFrame target, int x, int y, int n, int dx, int dy, int width, int height) {
    var sourceX = x - dx;
    var sourceY = y - dy;
    if (sourceX < 0 || sourceY < 0 || sourceX + n > width || sourceY + n > height)
      throw new InvalidDataException(
        $"A RoQ FCC code at ({x},{y}) points {n}x{n} pixels of motion to ({sourceX},{sourceY}), outside "
        + $"the {width}x{height} picture. Nothing measured this against exercises a vector reaching off "
        + "the edge of the picture, so it is not read as clamped or as wrapping.");

    for (var row = 0; row < n; ++row) {
      var targetOffset = (y + row) * width + x;
      var sourceOffset = (sourceY + row) * width + sourceX;
      Array.Copy(reference.Y, sourceOffset, target.Y, targetOffset, n);
      Array.Copy(reference.Cb, sourceOffset, target.Cb, targetOffset, n);
      Array.Copy(reference.Cr, sourceOffset, target.Cr, targetOffset, n);
    }
  }

  /// <summary>Paints a 2x2 area directly from one 2x2 codebook cell.</summary>
  private static void _PaintCb2(RoqCodebook codebook, RoqFrame target, int x, int y, int index) {
    var cell = codebook.Cb2(index);
    var width = target.Width;
    var y0 = y * width + x;
    var y1 = (y + 1) * width + x;
    target.Y[y0] = cell[0];
    target.Y[y0 + 1] = cell[1];
    target.Y[y1] = cell[2];
    target.Y[y1 + 1] = cell[3];
    target.Cb[y0] = target.Cb[y0 + 1] = target.Cb[y1] = target.Cb[y1 + 1] = cell[4];
    target.Cr[y0] = target.Cr[y0 + 1] = target.Cr[y1] = target.Cr[y1 + 1] = cell[5];
  }

  private static void _PaintCb2Checked(RoqCodebook codebook, RoqFrame target, int x, int y, byte index) {
    if (index >= codebook.Cb2Count)
      throw new InvalidDataException($"A RoQ terminal code names 2x2 cell {index}, and the codebook states only {codebook.Cb2Count}.");
    _PaintCb2(codebook, target, x, y, index);
  }

  /// <summary>Paints a 4x4 block directly from one 4x4 codebook cell: the four 2x2 cells it names,
  /// each at its own unscaled position.</summary>
  private static void _PaintCb4Native(RoqCodebook codebook, RoqFrame target, int x, int y, int index) {
    var cell = codebook.Cb4(index);
    _PaintCb2Checked(codebook, target, x, y, cell[0]);
    _PaintCb2Checked(codebook, target, x + 2, y, cell[1]);
    _PaintCb2Checked(codebook, target, x, y + 2, cell[2]);
    _PaintCb2Checked(codebook, target, x + 2, y + 2, cell[3]);
  }

  /// <summary>Paints an 8x8 block from one 4x4 codebook cell, each of its sixteen samples stretched
  /// over a 2x2 square.</summary>
  private static void _PaintCb4Upsampled(RoqCodebook codebook, RoqFrame target, int x, int y, int index) {
    var cell = codebook.Cb4(index);
    var width = target.Width;

    Span<byte> nativeY = stackalloc byte[16];
    Span<byte> nativeCb = stackalloc byte[4];
    Span<byte> nativeCr = stackalloc byte[4];

    for (var quadrant = 0; quadrant < 4; ++quadrant) {
      if (cell[quadrant] >= codebook.Cb2Count)
        throw new InvalidDataException($"A RoQ SLD code's 4x4 cell names 2x2 cell {cell[quadrant]}, and the codebook states only {codebook.Cb2Count}.");

      var sub = codebook.Cb2(cell[quadrant]);
      var ox = (quadrant & 1) * 2;
      var oy = (quadrant >> 1) * 2;
      nativeY[oy * 4 + ox] = sub[0];
      nativeY[oy * 4 + ox + 1] = sub[1];
      nativeY[(oy + 1) * 4 + ox] = sub[2];
      nativeY[(oy + 1) * 4 + ox + 1] = sub[3];
      nativeCb[(oy / 2) * 2 + ox / 2] = sub[4];
      nativeCr[(oy / 2) * 2 + ox / 2] = sub[5];
    }

    for (var row = 0; row < 4; ++row)
    for (var column = 0; column < 4; ++column) {
      var sampleY = nativeY[row * 4 + column];
      var sampleCb = nativeCb[(row / 2) * 2 + column / 2];
      var sampleCr = nativeCr[(row / 2) * 2 + column / 2];
      for (var dy = 0; dy < 2; ++dy) {
        var offset = (y + row * 2 + dy) * width + x + column * 2;
        target.Y[offset] = sampleY;
        target.Y[offset + 1] = sampleY;
        target.Cb[offset] = sampleCb;
        target.Cb[offset + 1] = sampleCb;
        target.Cr[offset] = sampleCr;
        target.Cr[offset + 1] = sampleCr;
      }
    }
  }

  /// <summary>
  /// Walks the shared stream of two-bit codes, sixteen bits at a time, most significant pair first,
  /// and the argument bytes that follow some of them from the same stream.
  /// </summary>
  private ref struct _CodeReader {
    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private uint _word;
    private int _bitsLeft;

    internal _CodeReader(ReadOnlySpan<byte> data) {
      this._data = data;
      this._position = 0;
    }

    internal int NextCode() {
      if (this._bitsLeft == 0) {
        if (this._position + 2 > this._data.Length)
          throw new InvalidDataException(
            "A RoQ QUAD_VQ chunk's code stream ends before the picture's blocks are all accounted for.");

        this._word = (uint)(this._data[this._position] | (this._data[this._position + 1] << 8));
        this._position += 2;
        this._bitsLeft = 16;
      }

      this._bitsLeft -= 2;
      return (int)((this._word >> this._bitsLeft) & 3);
    }

    internal byte NextByte() {
      if (this._position >= this._data.Length)
        throw new InvalidDataException("A RoQ QUAD_VQ chunk ends where a block's own argument byte should be.");

      return this._data[this._position++];
    }
  }
}
