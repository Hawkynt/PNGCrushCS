using System;
using System.Runtime.CompilerServices;

namespace FileFormat.WebP.Vp8L;

internal enum Vp8LTransformType {
  Predictor = 0,
  CrossColor = 1,
  SubtractGreen = 2,
  ColorIndexing = 3,
}

internal abstract class Vp8LTransform {
  public abstract void InverseTransform(uint[] pixels, int width, int height);
}

// ---------------------------------------------------------------------------
// SubtractGreen: inverse adds green to red and blue
// ---------------------------------------------------------------------------
internal sealed class Vp8LSubtractGreenTransform : Vp8LTransform {
  public override void InverseTransform(uint[] pixels, int width, int height) {
    var count = width * height;
    for (var i = 0; i < count; ++i) {
      var argb = pixels[i];
      var g = (argb >> 8) & 0xFF;
      var r = ((argb >> 16) + g) & 0xFF;
      var b = (argb + g) & 0xFF;
      pixels[i] = (argb & 0xFF00FF00) | (r << 16) | b;
    }
  }
}

// ---------------------------------------------------------------------------
// Predictor: 14 prediction modes applied per block, inverse adds prediction
// ---------------------------------------------------------------------------
internal sealed class Vp8LPredictorTransform : Vp8LTransform {
  private readonly uint[] _transformData;
  private readonly int _blockBits;

  public Vp8LPredictorTransform(uint[] transformData, int blockBits) {
    this._transformData = transformData;
    this._blockBits = blockBits;
  }

  public override void InverseTransform(uint[] pixels, int width, int height) {
    var blockSize = 1 << this._blockBits;
    var blocksPerRow = _DivRoundUp(width, blockSize);

    // First pixel: add black predictor (no change since predictor is 0x00000000 for color channels)
    // Actually mode 0 predicts 0xFF000000, but only alpha=0xFF; RGB=0
    // The first pixel row uses left predictor after the first pixel
    // First pixel: predict as 0xFF000000
    pixels[0] = _AddPixels(pixels[0], 0xFF000000);

    // Rest of first row: use left predictor
    for (var x = 1; x < width; ++x)
      pixels[x] = _AddPixels(pixels[x], pixels[x - 1]);

    // Remaining rows
    for (var y = 1; y < height; ++y) {
      var rowOffset = y * width;
      var blockY = y >> this._blockBits;

      // First pixel of row: use top predictor
      pixels[rowOffset] = _AddPixels(pixels[rowOffset], pixels[rowOffset - width]);

      for (var x = 1; x < width; ++x) {
        var blockX = x >> this._blockBits;
        var transformIdx = (blockY * blocksPerRow) + blockX;
        var mode = (int)((this._transformData[transformIdx] >> 8) & 0xFF); // green channel = mode

        var left = pixels[rowOffset + x - 1];
        var top = pixels[rowOffset + x - width];
        var topLeft = pixels[rowOffset + x - width - 1];
        var topRight = x + 1 < width ? pixels[rowOffset + x - width + 1] : top;

        var predicted = _Predict(mode, left, top, topRight, topLeft);
        pixels[rowOffset + x] = _AddPixels(pixels[rowOffset + x], predicted);
      }
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static uint _Predict(int mode, uint left, uint top, uint topRight, uint topLeft) => mode switch {
    0 => 0xFF000000,
    1 => left,
    2 => top,
    3 => topRight,
    4 => topLeft,
    5 => _Average(left, topRight),
    6 => _Average(left, top),
    7 => _Average(left, topLeft),
    8 => _Average(top, topRight),
    9 => _Average(top, topLeft),
    10 => _Average(_Average(left, topLeft), _Average(top, topRight)),
    11 => _Select(left, top, topLeft),
    12 => _ClampAddSubtractFull(left, top, topLeft),
    13 => _ClampAddSubtractHalf(_Average(left, top), topLeft),
    _ => 0xFF000000
  };

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static uint _AddPixels(uint a, uint b) {
    var aAlpha = (a >> 24) + (b >> 24);
    var aRed = ((a >> 16) & 0xFF) + ((b >> 16) & 0xFF);
    var aGreen = ((a >> 8) & 0xFF) + ((b >> 8) & 0xFF);
    var aBlue = (a & 0xFF) + (b & 0xFF);
    return ((aAlpha & 0xFF) << 24)
           | ((aRed & 0xFF) << 16)
           | ((aGreen & 0xFF) << 8)
           | (aBlue & 0xFF);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static uint _Average(uint a, uint b) {
    var avgA = (((a >> 24) & 0xFF) + ((b >> 24) & 0xFF)) >> 1;
    var avgR = (((a >> 16) & 0xFF) + ((b >> 16) & 0xFF)) >> 1;
    var avgG = (((a >> 8) & 0xFF) + ((b >> 8) & 0xFF)) >> 1;
    var avgB = ((a & 0xFF) + (b & 0xFF)) >> 1;
    return ((uint)avgA << 24) | ((uint)avgR << 16) | ((uint)avgG << 8) | (uint)avgB;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static uint _Select(uint left, uint top, uint topLeft) {
    var dLeft = _ManhattanDistance(top, topLeft);
    var dTop = _ManhattanDistance(left, topLeft);
    return dLeft <= dTop ? left : top;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _ManhattanDistance(uint a, uint b) {
    var da = Math.Abs((int)((a >> 24) & 0xFF) - (int)((b >> 24) & 0xFF));
    var dr = Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF));
    var dg = Math.Abs((int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF));
    var db = Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF));
    return da + dr + dg + db;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static uint _ClampAddSubtractFull(uint left, uint top, uint topLeft) {
    var a = _Clamp((int)((left >> 24) & 0xFF) + (int)((top >> 24) & 0xFF) - (int)((topLeft >> 24) & 0xFF));
    var r = _Clamp((int)((left >> 16) & 0xFF) + (int)((top >> 16) & 0xFF) - (int)((topLeft >> 16) & 0xFF));
    var g = _Clamp((int)((left >> 8) & 0xFF) + (int)((top >> 8) & 0xFF) - (int)((topLeft >> 8) & 0xFF));
    var b = _Clamp((int)(left & 0xFF) + (int)(top & 0xFF) - (int)(topLeft & 0xFF));
    return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static uint _ClampAddSubtractHalf(uint avg, uint topLeft) {
    var a = _Clamp((int)((avg >> 24) & 0xFF) + ((int)((avg >> 24) & 0xFF) - (int)((topLeft >> 24) & 0xFF)) / 2);
    var r = _Clamp((int)((avg >> 16) & 0xFF) + ((int)((avg >> 16) & 0xFF) - (int)((topLeft >> 16) & 0xFF)) / 2);
    var g = _Clamp((int)((avg >> 8) & 0xFF) + ((int)((avg >> 8) & 0xFF) - (int)((topLeft >> 8) & 0xFF)) / 2);
    var b = _Clamp((int)(avg & 0xFF) + ((int)(avg & 0xFF) - (int)(topLeft & 0xFF)) / 2);
    return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _Clamp(int val) => val < 0 ? 0 : val > 255 ? 255 : val;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _DivRoundUp(int num, int den) => (num + den - 1) / den;
}

// ---------------------------------------------------------------------------
// CrossColor: inverse applies color transform deltas
// ---------------------------------------------------------------------------
internal sealed class Vp8LCrossColorTransform : Vp8LTransform {
  private readonly uint[] _transformData;
  private readonly int _blockBits;

  public Vp8LCrossColorTransform(uint[] transformData, int blockBits) {
    this._transformData = transformData;
    this._blockBits = blockBits;
  }

  public override void InverseTransform(uint[] pixels, int width, int height) {
    var blockSize = 1 << this._blockBits;
    var blocksPerRow = _DivRoundUp(width, blockSize);

    for (var y = 0; y < height; ++y) {
      var blockY = y >> this._blockBits;
      for (var x = 0; x < width; ++x) {
        var blockX = x >> this._blockBits;
        var transformIdx = blockY * blocksPerRow + blockX;
        var transformPixel = this._transformData[transformIdx];

        // Extract multipliers from the transform pixel (ARGB layout)
        // green_to_red is in blue channel (bits 0-7)
        // green_to_blue is in green channel (bits 8-15)
        // red_to_blue is in red channel (bits 16-23)
        var greenToRed = (byte)(transformPixel & 0xFF);
        var greenToBlue = (byte)((transformPixel >> 8) & 0xFF);
        var redToBlue = (byte)((transformPixel >> 16) & 0xFF);

        var idx = y * width + x;
        var argb = pixels[idx];
        var g = (int)((argb >> 8) & 0xFF);
        var r = (int)((argb >> 16) & 0xFF);
        var b = (int)(argb & 0xFF);

        r = (r + _ColorTransformDelta(greenToRed, g)) & 0xFF;
        b = (b + _ColorTransformDelta(greenToBlue, g) + _ColorTransformDelta(redToBlue, r)) & 0xFF;

        pixels[idx] = (argb & 0xFF00FF00) | ((uint)r << 16) | (uint)b;
      }
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _ColorTransformDelta(byte t, int c)
    => ((sbyte)t * (sbyte)(byte)c) >> 5;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _DivRoundUp(int num, int den) => (num + den - 1) / den;
}

// ---------------------------------------------------------------------------
// ColorIndexing: palette mode; green channel indexes into a color table
// ---------------------------------------------------------------------------
internal sealed class Vp8LColorIndexingTransform : Vp8LTransform {
  private readonly uint[] _palette;
  private readonly int _bitsPerPixel;

  /// <summary>The effective width of the encoded image (packed pixels).</summary>
  public int EncodedWidth { get; }

  /// <summary>The original image width before packing.</summary>
  public int OriginalWidth { get; }

  public Vp8LColorIndexingTransform(uint[] palette, int originalWidth) {
    this._palette = palette;
    this.OriginalWidth = originalWidth;

    var paletteSize = palette.Length;
    if (paletteSize <= 2)
      this._bitsPerPixel = 1;
    else if (paletteSize <= 4)
      this._bitsPerPixel = 2;
    else if (paletteSize <= 16)
      this._bitsPerPixel = 4;
    else
      this._bitsPerPixel = 8;

    var pixelsPerByte = 8 / this._bitsPerPixel;
    this.EncodedWidth = pixelsPerByte > 1 ? _DivRoundUp(originalWidth, pixelsPerByte) : originalWidth;
  }

  public override void InverseTransform(uint[] pixels, int width, int height) {
    // width here is the original width, pixels array has been decoded at EncodedWidth
    // We need to unpack from EncodedWidth*height to width*height

    if (this._bitsPerPixel >= 8) {
      // No packing, just index lookup
      var count = width * height;
      for (var i = 0; i < count; ++i) {
        var index = (int)((pixels[i] >> 8) & 0xFF); // green channel
        pixels[i] = index < this._palette.Length ? this._palette[index] : 0;
      }
      return;
    }

    var pixelsPerBundledPixel = 8 / this._bitsPerPixel;
    var mask = (1 << this._bitsPerPixel) - 1;

    // Unpack in-place, working backwards to avoid overwriting data we still need
    for (var y = height - 1; y >= 0; --y) {
      var srcRow = y * this.EncodedWidth;
      var dstRow = y * width;

      for (var x = width - 1; x >= 0; --x) {
        var srcX = x / pixelsPerBundledPixel;
        var bitOffset = (x % pixelsPerBundledPixel) * this._bitsPerPixel;
        var bundledPixel = pixels[srcRow + srcX];
        var index = (int)((bundledPixel >> 8) & 0xFF); // green channel holds packed indices
        var paletteIndex = (index >> bitOffset) & mask;
        pixels[dstRow + x] = paletteIndex < this._palette.Length ? this._palette[paletteIndex] : 0;
      }
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _DivRoundUp(int num, int den) => (num + den - 1) / den;
}
