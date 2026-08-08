using System;
using FileFormat.Core;

namespace FileFormat.SamarHiresMap;

/// <summary>
/// In-memory representation of a SAMAR Hi-res Interlace with Map of Colours picture (.shc).
/// </summary>
/// <remarks>
/// Two Atari 8-bit high-resolution screens shown on alternate fields, each with a colour register
/// that changes as the beam crosses the picture. The register cannot be reloaded at arbitrary
/// points — the processor has only so many cycles between the bytes ANTIC is fetching — so the
/// positions where it changes are fixed, six per line, and the two fields change at different ones
/// so that between them the picture has twelve colour zones rather than six.
/// <para/>
/// The extension is shared with the MSX2+ YJK format, which is unrelated.
/// </remarks>
public readonly record struct SamarHiresMapFile
  : IImageFormatReader<SamarHiresMapFile>, IImageToRawImage<SamarHiresMapFile>,
    IImageFromRawImage<SamarHiresMapFile>, IImageFormatWriter<SamarHiresMapFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Where the second field's bitmap starts.</summary>
  public const int SecondBitmapOffset = Width * Height / 8;

  /// <summary>Where the first field's colour map starts.</summary>
  public const int FirstColorOffset = SecondBitmapOffset * 2;

  /// <summary>Where the second field's colour map starts.</summary>
  public const int SecondColorOffset = 16640;

  /// <summary>Total file size.</summary>
  public const int FileSize = 17920;

  /// <summary>Where along a line the first field reloads its colour register.</summary>
  public static ReadOnlySpan<int> FirstFieldChanges => [94, 166, 214, 262, 306];

  /// <summary>Where along a line the second field reloads its colour register.</summary>
  public static ReadOnlySpan<int> SecondFieldChanges => [46, 142, 190, 238, 286];

  static string IImageFormatMetadata<SamarHiresMapFile>.PrimaryExtension => ".shc";
  static string[] IImageFormatMetadata<SamarHiresMapFile>.FileExtensions => [".shc"];
  static SamarHiresMapFile IImageFormatReader<SamarHiresMapFile>.FromSpan(ReadOnlySpan<byte> data)
    => SamarHiresMapReader.FromSpan(data);
  static byte[] IImageFormatWriter<SamarHiresMapFile>.ToBytes(SamarHiresMapFile file)
    => SamarHiresMapWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SamarHiresMapFile>.VideoModes => [
    new("Atari 8-bit", [(Width, Height)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(SamarHiresMapFile file) {
    var data = file.Data ?? [];

    var first = _DecodeField(data, 0, FirstColorOffset, FirstFieldChanges);
    var second = _DecodeField(data, SecondBitmapOffset, SecondColorOffset, SecondFieldChanges);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(Atari8BitGraphics.ApplyPalette(first), Atari8BitGraphics.ApplyPalette(second)),
    };
  }

  /// <summary>Colour zones one field has on one line, being one more than it has changes.</summary>
  public const int ZonesPerLine = 6;

  /// <summary>Where the first field's zones begin and, last, where the line ends.</summary>
  private static ReadOnlySpan<int> _FirstFieldStarts => [0, 94, 166, 214, 262, 306, Width];

  /// <summary>Where the second field's zones begin and, last, where the line ends.</summary>
  private static ReadOnlySpan<int> _SecondFieldStarts => [0, 46, 142, 190, 238, 286, Width];

  /// <summary>
  /// Builds a picture from any image, sampling it to the 320x192 the two fields display.
  /// </summary>
  /// <remarks>
  /// A pixel's colour comes from four things at once: the colour register each field holds where the
  /// beam is, and whether the pixel is lit in each. A lit pixel keeps its register's hue and loses
  /// its luminance, an unlit one keeps both — and the two fields are then averaged, so a pixel shows
  /// one of four blends of the two registers' pairs.
  /// <para/>
  /// The two fields change registers at different points, which is what gives a line twelve colour
  /// zones from six apiece — and also what makes the choice coupled: a first-field zone spans parts
  /// of two second-field ones, so neither can be settled without the other. It is settled by taking
  /// the best line that could be drawn if both fields agreed everywhere, then letting each field in
  /// turn improve against the other now fixed. Where a line really can be drawn with one register
  /// throughout, the first pass finds it and the others leave it alone, so such a line comes back
  /// exactly as it went in.
  /// </remarks>
  public static SamarHiresMapFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).EnsureFormat(PixelFormat.Rgb24).PixelData;
    var palette = Atari8BitGraphics.Palette;
    var data = new byte[FileSize];

    var first = new int[ZonesPerLine];
    var second = new int[ZonesPerLine];
    var otherLit = new int[Width];
    var otherUnlit = new int[Width];

    for (var y = 0; y < Height; ++y) {
      var row = y * Width * 3;

      // Both fields taken to agree, which is the line the registers could draw on their own.
      for (var zone = 0; zone < ZonesPerLine; ++zone)
        first[zone] = _ChooseRegister(
          rgb, row, palette, _FirstFieldStarts[zone], _FirstFieldStarts[zone + 1], null, null);

      _SpreadZones(first, _FirstFieldStarts, otherLit, otherUnlit, palette);
      for (var zone = 0; zone < ZonesPerLine; ++zone)
        second[zone] = _ChooseRegister(
          rgb, row, palette, _SecondFieldStarts[zone], _SecondFieldStarts[zone + 1], otherLit, otherUnlit);

      _SpreadZones(second, _SecondFieldStarts, otherLit, otherUnlit, palette);
      for (var zone = 0; zone < ZonesPerLine; ++zone)
        first[zone] = _ChooseRegister(
          rgb, row, palette, _FirstFieldStarts[zone], _FirstFieldStarts[zone + 1], otherLit, otherUnlit);

      for (var zone = 0; zone < ZonesPerLine; ++zone) {
        data[FirstColorOffset + y * ZonesPerLine + zone] = (byte)first[zone];
        data[SecondColorOffset + y * ZonesPerLine + zone] = (byte)second[zone];
      }

      _WriteBits(rgb, row, palette, data, y, first, second);
    }

    return new() { Data = data };
  }

  /// <summary>Lays one field's zone registers out per pixel, as the two colours each can show.</summary>
  private static void _SpreadZones(
    ReadOnlySpan<int> zones, ReadOnlySpan<int> starts, Span<int> lit, Span<int> unlit, ReadOnlySpan<byte> palette) {
    for (var zone = 0; zone < ZonesPerLine; ++zone)
    for (var x = starts[zone]; x < starts[zone + 1]; ++x) {
      lit[x] = _Color(palette, zones[zone] & 240);
      unlit[x] = _Color(palette, zones[zone] & 254);
    }
  }

  /// <summary>
  /// The register that describes one zone with the least error, the other field being either fixed
  /// or taken to hold the same one.
  /// </summary>
  private static int _ChooseRegister(
    ReadOnlySpan<byte> rgb, int row, ReadOnlySpan<byte> palette, int from, int to,
    int[]? otherLit, int[]? otherUnlit) {
    var best = 0;
    var bestError = long.MaxValue;

    // The lowest bit of a register survives neither of the two masks, so only even ones differ.
    for (var candidate = 0; candidate < 256; candidate += 2) {
      var mine = (_Color(palette, candidate & 240), _Color(palette, candidate & 254));
      long error = 0;

      for (var x = from; x < to; ++x) {
        var theirs = otherLit == null ? mine : (otherLit[x], otherUnlit![x]);
        error += _BestBlend(rgb, row + x * 3, mine, theirs).Error;
      }

      if (error >= bestError)
        continue;

      bestError = error;
      best = candidate;
    }

    return best;
  }

  /// <summary>Settles each pixel's two lit bits, both fields' registers now being decided.</summary>
  private static void _WriteBits(
    ReadOnlySpan<byte> rgb, int row, ReadOnlySpan<byte> palette, byte[] data, int y,
    ReadOnlySpan<int> first, ReadOnlySpan<int> second) {
    var firstZone = 0;
    var secondZone = 0;

    for (var x = 0; x < Width; ++x) {
      while (x >= _FirstFieldStarts[firstZone + 1])
        ++firstZone;

      while (x >= _SecondFieldStarts[secondZone + 1])
        ++secondZone;

      var mine = (_Color(palette, first[firstZone] & 240), _Color(palette, first[firstZone] & 254));
      var theirs = (_Color(palette, second[secondZone] & 240), _Color(palette, second[secondZone] & 254));
      var choice = _BestBlend(rgb, row + x * 3, mine, theirs);

      var at = y * Width + x;
      var bit = (byte)(1 << (~x & 7));
      if (choice.Mine)
        data[at >> 3] |= bit;

      if (choice.Theirs)
        data[SecondBitmapOffset + (at >> 3)] |= bit;
    }
  }

  /// <summary>
  /// Which of the four blends of two registers' pairs a pixel comes closest to, and how far off it
  /// still is.
  /// </summary>
  private static (long Error, bool Mine, bool Theirs) _BestBlend(
    ReadOnlySpan<byte> rgb, int at, (int Lit, int Unlit) mine, (int Lit, int Unlit) theirs) {
    var best = (Error: long.MaxValue, Mine: false, Theirs: false);

    for (var m = 0; m < 2; ++m)
    for (var t = 0; t < 2; ++t) {
      var error = _Distance(rgb, at, _Average(m == 0 ? mine.Unlit : mine.Lit, t == 0 ? theirs.Unlit : theirs.Lit));
      if (error >= best.Error)
        continue;

      best = (error, m != 0, t != 0);
    }

    return best;
  }

  /// <summary>One GTIA colour byte as packed 0xRRGGBB.</summary>
  private static int _Color(ReadOnlySpan<byte> palette, int value)
    => (palette[value * 3] << 16) | (palette[value * 3 + 1] << 8) | palette[value * 3 + 2];

  /// <summary>Two colours as the display's alternating fields average them, rounding down.</summary>
  private static int _Average(int left, int right)
    => ((left & right) + (((left ^ right) >> 1) & 0x7F7F7F)) & 0xFFFFFF;

  /// <summary>Squared distance between a pixel and a packed colour.</summary>
  private static long _Distance(ReadOnlySpan<byte> rgb, int at, int color) {
    int dr = rgb[at] - ((color >> 16) & 0xFF);
    int dg = rgb[at + 1] - ((color >> 8) & 0xFF);
    int db = rgb[at + 2] - (color & 0xFF);

    return dr * dr + dg * dg + db * db;
  }

  /// <summary>Draws one field into GTIA colour bytes.</summary>
  private static byte[] _DecodeField(ReadOnlySpan<byte> data, int bitmap, int color, ReadOnlySpan<int> changes) {
    var frame = new byte[Width * Height];

    for (var y = 0; y < Height; ++y) {
      for (var x = 0; x < Width; ++x) {
        foreach (var change in changes) {
          if (x == change)
            ++color;
        }

        var at = y * Width + x;
        var lit = (_At(data, bitmap + (at >> 3)) >> (~x & 7) & 1) != 0;

        // A lit pixel keeps the register's hue but loses its luminance; an unlit one keeps both,
        // less the bit the hardware ignores.
        frame[at] = (byte)(_At(data, color) & (lit ? 240 : 254));
      }

      // The last zone of a line runs into the first of the next, so the register steps once more.
      ++color;
    }

    return frame;
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
