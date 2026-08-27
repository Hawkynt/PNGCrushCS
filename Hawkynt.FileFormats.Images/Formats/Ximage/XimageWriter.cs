using System;
using System.Globalization;
using System.Text;

namespace FileFormat.Ximage;

/// <summary>Writes uncompressed Ximage pictures (.xim).</summary>
public static class XimageWriter {

  private const int _VersionAt = 0, _VersionLength = 8;
  private const int _HeaderSizeAt = 8, _HeaderSizeLength = 8;
  private const int _WidthAt = 16, _WidthLength = 8;
  private const int _HeightAt = 24, _HeightLength = 8;
  private const int _ColourCountAt = 32, _ColourCountLength = 8;
  private const int _PlanesAt = 40, _PlanesLength = 3;
  private const int _DepthAt = 52, _DepthLength = 4;
  private const int _AlphaAt = 56, _AlphaLength = 4;
  private const int _RunLengthCodedAt = 60, _RunLengthCodedLength = 4;

  public static byte[] ToBytes(XimageFile file) {
    if (file.Width is < 1 or > XimageFile.MaximumSide || file.Height is < 1 or > XimageFile.MaximumSide)
      throw new ArgumentOutOfRangeException(nameof(file), $"Ximage dimensions must be between 1 and {XimageFile.MaximumSide} pixels per side.");
    if (file.Planes is not (1 or 3))
      throw new ArgumentOutOfRangeException(nameof(file), "Ximage supports one or three 8-bit planes.");
    if (file.PlaneData == null || file.PlaneData.Length < file.Planes)
      throw new ArgumentException("The Ximage picture does not contain all declared planes.", nameof(file));

    var count = checked(file.Width * file.Height);
    for (var p = 0; p < file.Planes; ++p)
      if (file.PlaneData[p] == null || file.PlaneData[p].Length < count)
        throw new ArgumentException($"Ximage plane {p} does not contain enough samples for the declared dimensions.", nameof(file));

    if (file.HasPalette && file.Planes != 1)
      throw new ArgumentException("An Ximage colour table is only meaningful for a single-plane picture.", nameof(file));
    if (file.HasPalette && (file.Palette == null || file.Palette.Length < XimageFile.PaletteEntries * 3))
      throw new ArgumentException("A paletted Ximage picture requires all 256 RGB palette entries.", nameof(file));

    var result = new byte[checked(XimageFile.HeaderSize + count * file.Planes)];
    result.AsSpan(0, XimageFile.PaletteOffset).Fill((byte)' ');

    _WriteField(result, _VersionAt, _VersionLength, XimageFile.Version);
    _WriteField(result, _HeaderSizeAt, _HeaderSizeLength, XimageFile.HeaderSize);
    _WriteField(result, _WidthAt, _WidthLength, file.Width);
    _WriteField(result, _HeightAt, _HeightLength, file.Height);
    _WriteField(result, _ColourCountAt, _ColourCountLength, file.HasPalette ? XimageFile.PaletteEntries : 0);
    _WriteField(result, _PlanesAt, _PlanesLength, file.Planes);
    _WriteField(result, _DepthAt, _DepthLength, 8);
    _WriteField(result, _AlphaAt, _AlphaLength, 0);
    _WriteField(result, _RunLengthCodedAt, _RunLengthCodedLength, 0);

    if (file.HasPalette)
      file.Palette.AsSpan(0, XimageFile.PaletteEntries * 3).CopyTo(result.AsSpan(XimageFile.PaletteOffset));

    var at = XimageFile.HeaderSize;
    for (var p = 0; p < file.Planes; ++p) {
      file.PlaneData[p].AsSpan(0, count).CopyTo(result.AsSpan(at));
      at += count;
    }

    return result;
  }

  private static void _WriteField(Span<byte> output, int at, int length, int value) {
    var text = value.ToString(CultureInfo.InvariantCulture);
    if (text.Length > length)
      throw new ArgumentOutOfRangeException(nameof(value), $"Value {value} does not fit in an Ximage field of {length} characters.");

    Encoding.ASCII.GetBytes(text, output.Slice(at, text.Length));
  }
}
