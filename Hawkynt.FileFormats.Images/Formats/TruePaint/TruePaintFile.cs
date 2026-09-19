using System;
using FileFormat.Core;

namespace FileFormat.TruePaint;

/// <summary>In-memory representation of a True Paint interlace multicolor image (.mci).</summary>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct TruePaintFile : IImageFormatReader<TruePaintFile>, IImageToRawImage<TruePaintFile>, IImageFromRawImage<TruePaintFile>, IImageFormatWriter<TruePaintFile> {

  static string IImageFormatMetadata<TruePaintFile>.PrimaryExtension => ".mci";
  static string[] IImageFormatMetadata<TruePaintFile>.FileExtensions => [".mci"];
  static TruePaintFile IImageFormatReader<TruePaintFile>.FromSpan(ReadOnlySpan<byte> data) => TruePaintReader.FromSpan(data);
  static byte[] IImageFormatWriter<TruePaintFile>.ToBytes(TruePaintFile file) => TruePaintWriter.ToBytes(file);

  /// <summary>
  /// The width of the picture the two fields make between them, always 320.
  /// </summary>
  /// <remarks>
  /// One multicolour field is 160 pixels across, but the second is displayed half a multicolour
  /// pixel to the right of the first, so between them they place a boundary every 320th of the
  /// screen. That is what makes the format worth having over a plain multicolour screen and it is
  /// what <see cref="ToRawImage"/> reproduces.
  /// </remarks>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Multicolour pixels across one field.</summary>
  internal const int CodedWidth = FixedWidth / 2;

  /// <summary>Character cells across the screen.</summary>
  internal const int CellsPerRow = 40;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of a bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of a screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>
  /// Where each section sits in the file, which is where the C64 memory it is loaded into puts it.
  /// </summary>
  /// <remarks>
  /// The file is a straight image of memory from <c>$9C00</c> upwards, so every section lands on the
  /// address the VIC-II wants it at and the gaps between them are the bytes those alignments leave
  /// over: the first video matrix at <c>$9C00</c>, the two bitmaps at <c>$A000</c> and <c>$C000</c>,
  /// the second video matrix at <c>$E000</c> and colour RAM at <c>$E400</c>. Laying the five
  /// sections out end to end instead — which is what this did before — gives a file of exactly the
  /// right length that no True Paint reader can follow, and <c>recoil2png</c> rebuilt one 142 levels
  /// a channel away from the picture that went in.
  /// </remarks>
  internal const int ScreenRam1Offset = 2; // $9C00

  /// <inheritdoc cref="ScreenRam1Offset"/>
  internal const int BackgroundOffset = 1002; // $9FE8

  /// <inheritdoc cref="ScreenRam1Offset"/>
  internal const int BorderOffset = 1003; // $9FE9

  /// <inheritdoc cref="ScreenRam1Offset"/>
  internal const int BitmapData1Offset = 1026; // $A000

  /// <inheritdoc cref="ScreenRam1Offset"/>
  internal const int BitmapData2Offset = 9218; // $C000

  /// <inheritdoc cref="ScreenRam1Offset"/>
  internal const int ScreenRam2Offset = 17410; // $E000

  /// <inheritdoc cref="ScreenRam1Offset"/>
  internal const int ColorRamOffset = 18434; // $E400

  /// <summary>Expected total file size, which is colour RAM's end at <c>$E7E8</c>.</summary>
  public const int ExpectedFileSize = ColorRamOffset + ColorRamSize; // 19434

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian, typically $9C00).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>First multicolor bitmap data (8000 bytes).</summary>
  public byte[] BitmapData1 { get; init; }

  /// <summary>First screen RAM (1000 bytes).</summary>
  public byte[] ScreenRam1 { get; init; }

  /// <summary>Second multicolor bitmap data (8000 bytes).</summary>
  public byte[] BitmapData2 { get; init; }

  /// <summary>Second screen RAM (1000 bytes).</summary>
  public byte[] ScreenRam2 { get; init; }

  /// <summary>Color RAM shared by both bitmaps (1000 bytes).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Border color index (0-15).</summary>
  public byte BorderColor { get; init; }

  /// <summary>Converts this True Paint image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  /// <remarks>
  /// The screen alternates between two multicolour fields faster than the eye separates them, so
  /// what is seen is their average — and the second field is drawn one 320th of the screen to the
  /// right of the first, which is the half a multicolour pixel that gives the format its extra
  /// resolution. Column zero therefore has no second field to average with and takes the background
  /// register in its place, which is where the picture runs off the left of the shifted field.
  /// </remarks>
  public static RawImage ToRawImage(TruePaintFile file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];
    var background = Commodore64Graphics.HexColors[file.BackgroundColor & 0x0F];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var color1 = _FieldColour(file.BitmapData1, file.ScreenRam1, file.ColorRam, file.BackgroundColor, x, y);
        var color2 = x == 0
          ? background
          : _FieldColour(file.BitmapData2, file.ScreenRam2, file.ColorRam, file.BackgroundColor, x - 1, y);

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF)) / 2);
        rgb[offset + 1] = (byte)((((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF)) / 2);
        rgb[offset + 2] = (byte)(((color1 & 0xFF) + (color2 & 0xFF)) / 2);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>What one field shows at a point on the 320-pixel screen.</summary>
  private static int _FieldColour(byte[] bitmap, byte[] screenRam, byte[] colorRam, byte background, int x, int y) {
    var cellIndex = (y >> 3) * CellsPerRow + (x >> 3);
    var bits = (bitmap[cellIndex * 8 + (y & 7)] >> (~x & 6)) & 3;
    var colorIndex = bits switch {
      1 => (screenRam[cellIndex] >> 4) & 0x0F,
      2 => screenRam[cellIndex] & 0x0F,
      3 => colorRam[cellIndex] & 0x0F,
      _ => background & 0x0F,
    };

    return Commodore64Graphics.HexColors[colorIndex];
  }

  /// <summary>Creates a True Paint picture from a <see cref="RawImage"/>, sampling it onto the VIC-II's multicolour screen.</summary>
  /// <remarks>
  /// True Paint holds two multicolour screens that the machine alternates between, and
  /// <see cref="ToRawImage"/> reproduces that by averaging the two. Writing the same screen into
  /// both fields is what a still picture wants: the average of a colour with itself is that colour,
  /// so every column both fields agree on comes back exactly as it went in. Mixing two different
  /// fields would buy extra apparent colours at the cost of never reproducing the original, which is
  /// a dithering decision and not one an encoder should make silently.
  /// <para/>
  /// Within one field the hardware allows four colours per 4x8 cell: a shared background register
  /// plus the two screen nibbles and the colour RAM nibble.
  /// </remarks>
  public static TruePaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // The columns the fields agree on are the odd ones — an even column is the seam where the
    // shifted field still shows the multicolour pixel to its left — so those are the ones read off
    // the picture, and a picture this format produced comes back through it unchanged.
    var bgra = image.SampleTo(FixedWidth, FixedHeight).ToBgra32();
    var indices = new byte[CodedWidth * FixedHeight];
    for (var y = 0; y < FixedHeight; ++y)
      for (var x = 0; x < CodedWidth; ++x) {
        var offset = (y * FixedWidth + x * 2 + 1) * 4;
        indices[y * CodedWidth + x] =
          (byte)Commodore64Graphics.FindNearestColorIndex(bgra[offset + 2], bgra[offset + 1], bgra[offset]);
      }

    Span<int> frequency = stackalloc int[16];
    foreach (var index in indices)
      ++frequency[index];

    var background = 0;
    for (var i = 1; i < 16; ++i)
      if (frequency[i] > frequency[background])
        background = i;

    var bitmapData = new byte[BitmapDataSize];
    var screenRam = new byte[ScreenRamSize];
    var colorRam = new byte[ColorRamSize];
    Span<int> cellColors = stackalloc int[3];

    for (var cellY = 0; cellY < FixedHeight / 8; ++cellY)
      for (var cellX = 0; cellX < 40; ++cellX) {
        var cellIndex = cellY * 40 + cellX;
        _PickCellColors(indices, cellX, cellY, (byte)background, cellColors);

        screenRam[cellIndex] = (byte)((cellColors[0] << 4) | cellColors[1]);
        colorRam[cellIndex] = (byte)cellColors[2];

        for (var row = 0; row < 8; ++row) {
          byte packed = 0;
          for (var column = 0; column < 4; ++column) {
            var color = indices[(cellY * 8 + row) * CodedWidth + cellX * 4 + column];
            var pattern = _PickPattern(color, (byte)background, cellColors);
            packed |= (byte)(pattern << ((3 - column) * 2));
          }

          bitmapData[cellIndex * 8 + row] = packed;
        }
      }

    return new() {
      LoadAddress = 0x9C00,
      BitmapData1 = bitmapData,
      ScreenRam1 = screenRam,
      BitmapData2 = bitmapData[..],
      ScreenRam2 = screenRam[..],
      ColorRam = colorRam,
      BackgroundColor = (byte)background,
      BorderColor = (byte)background,
    };
  }

  /// <summary>Fills <paramref name="cellColors"/> with the three commonest colours in the cell that are not the background.</summary>
  private static void _PickCellColors(byte[] indices, int cellX, int cellY, byte background, Span<int> cellColors) {
    Span<int> frequency = stackalloc int[16];
    for (var row = 0; row < 8; ++row)
      for (var column = 0; column < 4; ++column)
        ++frequency[indices[(cellY * 8 + row) * CodedWidth + cellX * 4 + column]];

    frequency[background] = -1;
    for (var slot = 0; slot < 3; ++slot) {
      var best = 0;
      for (var i = 1; i < 16; ++i)
        if (frequency[i] > frequency[best])
          best = i;

      cellColors[slot] = frequency[best] > 0 ? best : background;
      if (frequency[best] > 0)
        frequency[best] = -1;
    }
  }

  /// <summary>Picks the two-bit pattern whose register holds the colour, or the nearest one it does hold.</summary>
  private static int _PickPattern(byte color, byte background, ReadOnlySpan<int> cellColors) {
    if (color == background)
      return 0;

    for (var slot = 0; slot < 3; ++slot)
      if (cellColors[slot] == color)
        return slot + 1;

    var bestPattern = 0;
    var bestDistance = _Distance(color, background);
    for (var slot = 0; slot < 3; ++slot) {
      var distance = _Distance(color, (byte)cellColors[slot]);
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      bestPattern = slot + 1;
    }

    return bestPattern;
  }

  /// <summary>Squared RGB distance between two VIC-II palette entries.</summary>
  private static int _Distance(byte left, byte right) {
    var a = Commodore64Graphics.HexColors[left];
    var b = Commodore64Graphics.HexColors[right];
    var dr = ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF);
    var dg = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF);
    var db = (a & 0xFF) - (b & 0xFF);
    return dr * dr + dg * dg + db * db;
  }

}
