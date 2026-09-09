using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Hpgl;

/// <summary>Writes HP-GL plots and builds a bounded plot from an arbitrary raster image.</summary>
/// <remarks>
/// HP-GL names pens rather than colours. The raster authoring path therefore reduces each source
/// pixel to the eight-pen palette the reader already models, composites transparency onto the white
/// page, joins equal adjacent pen indices into one horizontal run, and paints each run with a filled
/// rectangle. This is deliberately a plot, not a private raster extension hidden inside one.
/// <para/>
/// A source wider or taller than 512 pixels is sampled down with its aspect ratio intact. Even the
/// adversarial case where every neighbouring pixel chooses a different pen then stays bounded to a
/// little over half a million drawing instructions instead of turning a photograph into millions of
/// tiny plotter commands.
/// </remarks>
public static class HpglWriter {

  /// <summary>Ten plotter units are a quarter millimetre, close to one pixel at the renderer's 96 dpi.</summary>
  private const int _PlotterUnitsPerPixel = 10;

  /// <summary>Bounds the raster approximation and therefore the number of emitted vector primitives.</summary>
  private const int _MaximumRasterSide = 512;

  /// <summary>The label terminator HP-GL starts with before a DT instruction changes it.</summary>
  private const char _DefaultLabelTerminator = '\u0003';

  /// <summary>Builds an HP-GL plot that approximates <paramref name="image"/> with the plotter palette.</summary>
  public static HpglFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentOutOfRangeException(nameof(image), $"An HP-GL plot needs a positive image size, not {image.Width}x{image.Height}.");
    if (!image.HasEnoughPixelData)
      throw new InvalidDataException($"The {image.Width}x{image.Height} {image.Format} source does not contain enough pixel data.");

    image = _Bound(image);
    var pixels = image.ToRgba32();
    var width = image.Width;
    var height = image.Height;
    var instructions = new List<HpglInstruction>();

    // Give even an all-white picture a real extent. SP0 selects no physical pen; the reader's page
    // model treats that as white, while a real pen plotter simply traverses the already-white paper.
    instructions.Add(new("IN", [], string.Empty));
    instructions.Add(new("SP", [0], string.Empty));
    instructions.Add(new("PU", [0, 0], string.Empty));
    instructions.Add(new("RA", [width * _PlotterUnitsPerPixel, height * _PlotterUnitsPerPixel], string.Empty));

    var currentPen = 0;
    for (var y = 0; y < height; ++y) {
      var x = 0;
      while (x < width) {
        var pen = _NearestPen(pixels, (y * width + x) * 4);
        var start = x++;
        while (x < width && _NearestPen(pixels, (y * width + x) * 4) == pen)
          ++x;

        if (pen == 0)
          continue;

        if (pen != currentPen) {
          instructions.Add(new("SP", [pen], string.Empty));
          currentPen = pen;
        }

        // RawImage row zero is the top; HP-GL's y axis grows upwards.
        var bottom = (height - y - 1) * _PlotterUnitsPerPixel;
        instructions.Add(new("PU", [start * _PlotterUnitsPerPixel, bottom], string.Empty));
        instructions.Add(new("RA", [x * _PlotterUnitsPerPixel, bottom + _PlotterUnitsPerPixel], string.Empty));
      }
    }

    instructions.Add(new("SP", [0], string.Empty));
    return new() { Instructions = instructions };
  }

  /// <summary>Serializes the instruction model as canonical semicolon-terminated HP-GL text.</summary>
  public static byte[] ToBytes(HpglFile file) {
    if (file.Instructions == null)
      throw new InvalidDataException("An HP-GL plot with no instructions cannot be written.");

    var text = new StringBuilder();
    var labelTerminator = _DefaultLabelTerminator;

    foreach (var instruction in file.Instructions) {
      var mnemonic = instruction.Mnemonic;
      if (mnemonic is not { Length: 2 } || !char.IsAsciiLetter(mnemonic[0]) || !char.IsAsciiLetter(mnemonic[1]))
        throw new InvalidDataException($"'{mnemonic}' is not a two-letter HP-GL mnemonic.");

      mnemonic = mnemonic.ToUpperInvariant();
      text.Append(mnemonic);

      if (mnemonic == "LB") {
        var label = instruction.Text ?? string.Empty;
        _ValidateLatin1(label);
        if (label.Contains(labelTerminator))
          throw new InvalidDataException("An HP-GL label contains its active label terminator.");

        text.Append(label).Append(labelTerminator);
        continue;
      }

      var numbers = instruction.Numbers ?? [];
      for (var i = 0; i < numbers.Length; ++i) {
        var number = numbers[i];
        if (!double.IsFinite(number))
          throw new InvalidDataException($"HP-GL instruction {mnemonic} contains the non-finite number {number}.");

        if (i > 0)
          text.Append(',');
        text.Append(number.ToString("G17", CultureInfo.InvariantCulture));
      }

      var raw = instruction.Text ?? string.Empty;
      if (raw.Length > 0) {
        _ValidateLatin1(raw);
        text.Append(raw);
        if (mnemonic == "DT")
          labelTerminator = raw[0];
      }

      text.Append(';');
    }

    return Encoding.Latin1.GetBytes(text.ToString());
  }

  private static RawImage _Bound(RawImage image) {
    if (image.Width <= _MaximumRasterSide && image.Height <= _MaximumRasterSide)
      return image;

    var scale = Math.Min((double)_MaximumRasterSide / image.Width, (double)_MaximumRasterSide / image.Height);
    var width = Math.Max(1, (int)Math.Round(image.Width * scale));
    var height = Math.Max(1, (int)Math.Round(image.Height * scale));
    return image.SampleTo(width, height);
  }

  /// <summary>Returns the nearest modelled pen after compositing one RGBA pixel onto white paper.</summary>
  private static int _NearestPen(byte[] pixels, int offset) {
    var alpha = pixels[offset + 3];
    var red = _CompositeOnWhite(pixels[offset], alpha);
    var green = _CompositeOnWhite(pixels[offset + 1], alpha);
    var blue = _CompositeOnWhite(pixels[offset + 2], alpha);

    var best = 0;
    var bestDistance = int.MaxValue;
    for (var pen = 0; pen < HpglFile.Pens.Length; ++pen) {
      var colour = HpglFile.Pens[pen];
      var dr = red - colour.R;
      var dg = green - colour.G;
      var db = blue - colour.B;
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;

      best = pen;
      bestDistance = distance;
    }

    return best;
  }

  private static byte _CompositeOnWhite(byte component, byte alpha)
    => (byte)((component * alpha + 255 * (255 - alpha) + 127) / 255);

  private static void _ValidateLatin1(string text) {
    foreach (var c in text)
      if (c > byte.MaxValue)
        throw new InvalidDataException("HP-GL text is an 8-bit stream and cannot represent this character.");
  }
}
