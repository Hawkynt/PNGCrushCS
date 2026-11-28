using System;
using System.Collections.Generic;

namespace FileFormat.Wic;

/// <summary>Turns JWIC planes into pixels and back.</summary>
/// <remarks>
/// A colour picture travels as a luma plane the size of the coefficient matrix followed by two
/// chroma planes half as wide, one chroma sample for each horizontal pair of pixels. The colour
/// conversion is NTSC YIQ (I and Q in the second and third planes), evaluated in double precision
/// and stored single precision; the decoders clamp to 0..255 and truncate. All three planes share
/// one coder stream and one set of contexts.
/// </remarks>
internal static class WicCodec {

  private const uint _SIGN = 0x80000000u;
  private const uint _MAGNITUDE = 0x7FFFFFFFu;

  /// <summary>Decodes the coder stream after the header into Gray8 or Rgb24 pixels of the picture size.</summary>
  public static byte[] Decode(WicHeader header, byte[] data) {
    var coder = new WicQmCoder.Decoder(data, WicHeader.Size);
    var width = header.MatrixWidth;
    var height = header.MatrixHeight;

    if (!header.IsColour) {
      var grey = _ToFloat(WicZerotree.Decode(coder, width, height, header.LumaLevels, header.TopPlane(0)));
      WicWavelet.Inverse(grey, width, height, header.Transform);
      var pixels = new byte[header.Width * header.Height];
      for (var y = 0; y < header.Height; ++y)
        for (var x = 0; x < header.Width; ++x)
          pixels[y * header.Width + x] = _ToByte(grey[y * width + x]);

      return pixels;
    }

    var chromaWidth = width / 2;
    var luma = WicZerotree.Decode(coder, width, height, header.LumaLevels, header.TopPlane(0));
    var inPhase = WicZerotree.Decode(coder, chromaWidth, height, header.ChromaLevels, header.TopPlane(1));
    var quadrature = WicZerotree.Decode(coder, chromaWidth, height, header.ChromaLevels, header.TopPlane(2));

    var y0 = _ToFloat(luma);
    var i0 = _ToFloat(inPhase);
    var q0 = _ToFloat(quadrature);
    WicWavelet.Inverse(y0, width, height, header.Transform);
    WicWavelet.Inverse(i0, chromaWidth, height, header.Transform);
    WicWavelet.Inverse(q0, chromaWidth, height, header.Transform);

    // The chroma planes are read as one run of width*height/2 samples indexed by pixel number / 2,
    // which pairs each pixel with its horizontal neighbour because the matrix width is even.
    var redFirst = header.Generation == WicGeneration.Version100;
    var rgb = new byte[header.Width * header.Height * 3];
    for (var y = 0; y < header.Height; ++y)
      for (var x = 0; x < header.Width; ++x) {
        var index = y * width + x;
        double luminance = y0[index], i = i0[index >> 1], q = q0[index >> 1];
        var first = _ToByte((float)(luminance + 0.95617 * i + 0.62143 * q));
        var green = _ToByte((float)(luminance - 0.27269 * i - 0.64681 * q));
        var third = _ToByte((float)(luminance - 1.10374 * i + 1.70062 * q));
        var target = (y * header.Width + x) * 3;
        rgb[target] = redFirst ? first : third;
        rgb[target + 1] = green;
        rgb[target + 2] = redFirst ? third : first;
      }

    return rgb;
  }

  /// <summary>Encodes Gray8 or Rgb24 pixels into a complete file.</summary>
  public static byte[] Encode(WicHeader template, byte[] pixels, float bitsPerPixel) {
    var width = template.MatrixWidth;
    var height = template.MatrixHeight;
    var output = new List<byte>(Math.Max(64, (int)Math.Min(int.MaxValue / 2, (long)(bitsPerPixel * width * height / 8) + 64)));
    output.AddRange(new byte[WicHeader.Size]);
    var coder = new WicQmCoder.Encoder(output);

    WicHeader header;
    if (!template.IsColour) {
      var matrix = new float[width * height];
      for (var y = 0; y < template.Height; ++y)
        for (var x = 0; x < template.Width; ++x)
          matrix[y * width + x] = pixels[y * template.Width + x];

      var levels = WicWavelet.Forward(matrix, 0, width, height, template.Transform);
      var coefficients = _ToSignMagnitude(matrix, 0, width * height);
      var tree = WicZerotree.TreeBitLengths(coefficients, width, height, levels);
      header = template with {
        LumaLevels = levels,
        ChromaLevels = 0,
        TopPlanes = (uint)WicZerotree.TopPlane(tree, width, height, levels),
      };

      // The translators compute the budget in double precision from a float target.
      var budget = (int)(bitsPerPixel * ((float)(width * height) / 8.0));
      WicZerotree.Encode(coder, coefficients, tree, width, height, levels, budget);
    } else {
      var chromaWidth = width / 2;
      var planes = _ToYiq(template, pixels);
      var lumaLevels = WicWavelet.Forward(planes, 0, width, height, template.Transform);
      var chromaLevels = WicWavelet.Forward(planes, width * height, chromaWidth, height, template.Transform);
      WicWavelet.Forward(planes, width * height + chromaWidth * height, chromaWidth, height, template.Transform);

      var luma = _ToSignMagnitude(planes, 0, width * height);
      var inPhase = _ToSignMagnitude(planes, width * height, chromaWidth * height);
      var quadrature = _ToSignMagnitude(planes, width * height + chromaWidth * height, chromaWidth * height);
      var lumaTree = WicZerotree.TreeBitLengths(luma, width, height, lumaLevels);
      var inPhaseTree = WicZerotree.TreeBitLengths(inPhase, chromaWidth, height, chromaLevels);
      var quadratureTree = WicZerotree.TreeBitLengths(quadrature, chromaWidth, height, chromaLevels);
      header = template with {
        LumaLevels = lumaLevels,
        ChromaLevels = chromaLevels,
        TopPlanes = (uint)WicZerotree.TopPlane(lumaTree, width, height, lumaLevels)
          | (uint)WicZerotree.TopPlane(inPhaseTree, chromaWidth, height, chromaLevels) << 8
          | (uint)WicZerotree.TopPlane(quadratureTree, chromaWidth, height, chromaLevels) << 16,
      };

      // Cumulative byte budgets: luma stops at (256 - 2c)/256 of the total, the first chroma plane at
      // (256 - c)/256, the second at all of it. The arithmetic is single precision, as written.
      var scaled = bitsPerPixel * width * height;
      var lumaBudget = (int)(scaled * (float)(uint)(256 - 2 * template.ChromaBudgetScale) / 2048);
      var inPhaseBudget = (int)(scaled * (float)(uint)(256 - template.ChromaBudgetScale) / 2048);
      var quadratureBudget = (int)(scaled / 8);
      WicZerotree.Encode(coder, luma, lumaTree, width, height, lumaLevels, lumaBudget);
      WicZerotree.Encode(coder, inPhase, inPhaseTree, chromaWidth, height, chromaLevels, inPhaseBudget);
      WicZerotree.Encode(coder, quadrature, quadratureTree, chromaWidth, height, chromaLevels, quadratureBudget);
    }

    coder.Finish();
    var bytes = output.ToArray();
    header.WriteTo(bytes);
    return bytes;
  }

  /// <summary>Splits the padded picture into Y, I and Q planes laid end to end.</summary>
  private static float[] _ToYiq(WicHeader header, byte[] rgb) {
    var width = header.MatrixWidth;
    var count = width * header.MatrixHeight;
    var planes = new float[count * 2];
    var redFirst = header.Generation == WicGeneration.Version100;
    for (var index = 0; index < count; ++index) {
      var x = index % width;
      var y = index / width;
      double first = 0, green = 0, third = 0;
      if (x < header.Width && y < header.Height) {
        var source = (y * header.Width + x) * 3;
        first = rgb[source + (redFirst ? 0 : 2)];
        green = rgb[source + 1];
        third = rgb[source + (redFirst ? 2 : 0)];
      }

      planes[index] = (float)(0.299 * first + 0.587 * green + 0.114 * third);
      var i = 0.596 * first - 0.274 * green - 0.322 * third;
      var q = 0.211 * first - 0.523 * green + 0.312 * third;
      var pair = count + (index >> 1);
      if ((index & 1) == 0) {
        planes[pair] = (float)i;
        planes[pair + count / 2] = (float)q;
      } else {
        planes[pair] = (float)((planes[pair] + i) / 2);
        planes[pair + count / 2] = (float)((planes[pair + count / 2] + q) / 2);
      }
    }

    return planes;
  }

  private static uint[] _ToSignMagnitude(float[] values, int offset, int count) {
    var result = new uint[count];
    for (var i = 0; i < count; ++i) {
      var whole = (int)values[offset + i];
      result[i] = whole < 0 ? _SIGN | (uint)-whole : (uint)whole;
    }

    return result;
  }

  private static float[] _ToFloat(uint[] coefficients) {
    var result = new float[coefficients.Length];
    for (var i = 0; i < coefficients.Length; ++i) {
      var value = coefficients[i];
      result[i] = (value & _SIGN) != 0 ? -(int)(value & _MAGNITUDE) : (int)value;
    }

    return result;
  }

  private static byte _ToByte(float value) => value > 255 ? (byte)255 : value < 0 ? (byte)0 : (byte)value;
}
