using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Dxf;

/// <summary>Writes ASCII DXF files and builds a bounded vector approximation of an arbitrary raster.</summary>
/// <remarks>
/// DXF is a vector exchange format rather than a raster container. The raster authoring path therefore
/// composites transparency onto white paper, maps each source pixel to one of the nine AutoCAD Color
/// Index colours whose RGB values are fixed by Autodesk's DXF reference, joins equal horizontal runs,
/// and emits each run as a filled SOLID entity.
/// <para/>
/// Sources larger than 512 pixels on either side are sampled down with aspect ratio preserved. That
/// bounds the adversarial checkerboard case to at most 262144 SOLID entities while still producing an
/// ordinary interoperable DXF file instead of a private embedded raster payload.
/// </remarks>
public static class DxfWriter {

  private const int _MaximumRasterSide = 512;
  private const int _MinimumGroupCode = -5;
  private const int _MaximumGroupCode = 1071;

  // Index zero is the white paper and therefore emits no entity. Indices one through nine are the
  // colours whose RGB values Autodesk fixes for those ACI indices and are the same values rendered by
  // DxfRenderer.
  private static readonly Rgba32[] _Colours = [
    Rgba32.White,
    new(255, 0, 0),
    new(255, 255, 0),
    new(0, 255, 0),
    new(0, 255, 255),
    new(0, 0, 255),
    new(255, 0, 255),
    Rgba32.Black,
    new(65, 65, 65),
    new(128, 128, 128)
  ];

  /// <summary>Builds an ASCII DXF drawing that approximates <paramref name="image"/> with filled SOLID runs.</summary>
  public static DxfFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentOutOfRangeException(nameof(image), $"A DXF drawing needs a positive image size, not {image.Width}x{image.Height}.");
    if (!image.HasEnoughPixelData)
      throw new InvalidDataException($"The {image.Width}x{image.Height} {image.Format} source does not contain enough pixel data.");

    image = _Bound(image);
    var pixels = image.ToRgba32();
    var width = image.Width;
    var height = image.Height;
    var pairs = new List<DxfPair>();

    _Add(pairs,
      (0, "SECTION"), (2, "HEADER"),
      (9, "$ACADVER"), (1, "AC1009"),
      (9, "$EXTMIN"), (10, "0"), (20, "0"), (30, "0"),
      (9, "$EXTMAX"), (10, width.ToString(CultureInfo.InvariantCulture)), (20, height.ToString(CultureInfo.InvariantCulture)), (30, "0"),
      (0, "ENDSEC"),
      (0, "SECTION"), (2, "ENTITIES")
    );

    for (var y = 0; y < height; ++y) {
      var x = 0;
      while (x < width) {
        var colour = _NearestColour(pixels, (y * width + x) * 4);
        var start = x++;
        while (x < width && _NearestColour(pixels, (y * width + x) * 4) == colour)
          ++x;

        if (colour == 0)
          continue;

        // RawImage row zero is the top; DXF's world y axis grows upwards. SOLID stores the third
        // and fourth vertices as the far pair, so a rectangle is written lower-left, lower-right,
        // upper-left, upper-right.
        var bottom = height - y - 1;
        var top = bottom + 1;
        _Add(pairs,
          (0, "SOLID"),
          (8, "0"),
          (62, colour.ToString(CultureInfo.InvariantCulture)),
          (10, start.ToString(CultureInfo.InvariantCulture)), (20, bottom.ToString(CultureInfo.InvariantCulture)), (30, "0"),
          (11, x.ToString(CultureInfo.InvariantCulture)), (21, bottom.ToString(CultureInfo.InvariantCulture)), (31, "0"),
          (12, start.ToString(CultureInfo.InvariantCulture)), (22, top.ToString(CultureInfo.InvariantCulture)), (32, "0"),
          (13, x.ToString(CultureInfo.InvariantCulture)), (23, top.ToString(CultureInfo.InvariantCulture)), (33, "0")
        );
      }
    }

    _Add(pairs, (0, "ENDSEC"), (0, "EOF"));
    return new() { Pairs = pairs };
  }

  /// <summary>Serializes the pair model as canonical CRLF-delimited ASCII DXF text.</summary>
  public static byte[] ToBytes(DxfFile file) {
    if (file.Pairs == null)
      throw new InvalidDataException("A DXF drawing with no group codes cannot be written.");

    var text = new StringBuilder(file.Pairs.Count * 12);
    foreach (var pair in file.Pairs) {
      if (pair.Code is < _MinimumGroupCode or > _MaximumGroupCode)
        throw new InvalidDataException($"Group code {pair.Code} is outside the {_MinimumGroupCode} to {_MaximumGroupCode} range the DXF reference defines.");

      var value = pair.Value ?? throw new InvalidDataException($"Group code {pair.Code} has no value.");
      if (value.Contains('\r') || value.Contains('\n'))
        throw new InvalidDataException($"Group code {pair.Code} contains a line break in its value.");

      _ValidateLatin1(value);
      text.Append(pair.Code.ToString(CultureInfo.InvariantCulture).PadLeft(3))
        .Append("\r\n")
        .Append(value)
        .Append("\r\n");
    }

    var result = Encoding.Latin1.GetBytes(text.ToString());
    _ = DxfReader.FromSpan(result);
    return result;
  }

  private static RawImage _Bound(RawImage image) {
    if (image.Width <= _MaximumRasterSide && image.Height <= _MaximumRasterSide)
      return image;

    var scale = Math.Min((double)_MaximumRasterSide / image.Width, (double)_MaximumRasterSide / image.Height);
    var width = Math.Max(1, (int)Math.Round(image.Width * scale));
    var height = Math.Max(1, (int)Math.Round(image.Height * scale));
    return image.SampleTo(width, height);
  }

  private static int _NearestColour(byte[] pixels, int offset) {
    var alpha = pixels[offset + 3];
    var red = _CompositeOnWhite(pixels[offset], alpha);
    var green = _CompositeOnWhite(pixels[offset + 1], alpha);
    var blue = _CompositeOnWhite(pixels[offset + 2], alpha);

    var best = 0;
    var bestDistance = int.MaxValue;
    for (var index = 0; index < _Colours.Length; ++index) {
      var colour = _Colours[index];
      var dr = red - colour.R;
      var dg = green - colour.G;
      var db = blue - colour.B;
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;

      best = index;
      bestDistance = distance;
    }

    return best;
  }

  private static byte _CompositeOnWhite(byte component, byte alpha)
    => (byte)((component * alpha + 255 * (255 - alpha) + 127) / 255);

  private static void _ValidateLatin1(string text) {
    foreach (var c in text)
      if (c > byte.MaxValue)
        throw new InvalidDataException("ASCII DXF is an 8-bit text stream and cannot represent this character.");
  }

  private static void _Add(List<DxfPair> pairs, params (int Code, string Value)[] values) {
    foreach (var (code, value) in values)
      pairs.Add(new(code, value));
  }
}
