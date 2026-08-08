using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CanvasRaster;

/// <summary>In-memory representation of a Canvas raster picture (.ful).</summary>
/// <remarks>
/// An Atari ST picture whose palette changes every four scanlines, stored the way the program held
/// it in memory rather than in any arranged order: a table of flags saying which of the fifty
/// bands have a palette of their own, and then those palettes — written backwards, the first band's
/// last, because the routine that loaded them counted down.
/// <para/>
/// The bitmap is stored twice over. A list of runs fills the parts of the screen that repeat, each
/// naming where it starts and how many groups of planes to copy there; whatever the runs did not
/// touch then follows in scan order. Which is to say the format compresses only what repeats and
/// pays full price for the rest, rather than choosing between the two.
/// </remarks>
public readonly record struct CanvasRasterFile
  : IImageFormatReader<CanvasRasterFile>, IImageToRawImage<CanvasRasterFile>,
    IImageFromRawImage<CanvasRasterFile>, IImageFormatWriter<CanvasRasterFile> {

  /// <summary>Bands a picture is divided into, each four scanlines tall.</summary>
  public const int BandCount = 50;

  /// <summary>Scanlines one band covers.</summary>
  public const int BandHeight = 4;

  /// <summary>Colours one band's palette names.</summary>
  public const int ColorCount = 16;

  /// <summary>Pixels across the mode Canvas drew in.</summary>
  public const int Width = 320;

  /// <summary>Rows the picture holds.</summary>
  public const int Height = 200;

  /// <summary>Bytes one row of the bitmap occupies: four planes interleaved by the word.</summary>
  public const int Stride = Width / 2;

  /// <summary>Size of the unpacked bitmap.</summary>
  public const int BitmapSize = Stride * Height;

  /// <summary>Bytes one band's palette occupies: sixteen colours of three bytes.</summary>
  public const int PaletteSize = 48;

  /// <summary>Where the palettes end, the first band's being the last of them.</summary>
  public const int PaletteEnd = 896;

  /// <summary>Bytes between the palettes and the picture's own header.</summary>
  public const int HeaderGap = 608;

  /// <summary>Groups of planes a picture holds: 16000 for every mode.</summary>
  public const int GroupCount = 16000;

  static string IImageFormatMetadata<CanvasRasterFile>.PrimaryExtension => ".ful";
  static string[] IImageFormatMetadata<CanvasRasterFile>.FileExtensions => [".ful"];
  static CanvasRasterFile IImageFormatReader<CanvasRasterFile>.FromSpan(ReadOnlySpan<byte> data)
    => CanvasRasterReader.FromSpan(data);
  static byte[] IImageFormatWriter<CanvasRasterFile>.ToBytes(CanvasRasterFile file)
    => CanvasRasterWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CanvasRasterFile>.VideoModes => [
    new("Atari ST", [(320, 200), (640, 400)], [16])
  ];

  /// <summary>The whole file, which the palettes are read out of a band at a time.</summary>
  public byte[] Data { get; init; }

  /// <summary>The unpacked bitmap.</summary>
  public byte[] Bitmap { get; init; }

  /// <summary>Where the palette of the last band that has one starts.</summary>
  public int PaletteCursor { get; init; }

  /// <summary>Bitplanes a pixel is built from: four, two or one.</summary>
  public int Bitplanes { get; init; }

  /// <summary>Which of the three screen modes it is.</summary>
  public int Mode { get; init; }

  public static RawImage ToRawImage(CanvasRasterFile file) {
    var data = file.Data ?? [];
    var bitmap = file.Bitmap ?? [];

    var width = file.Mode == 0 ? 320 : 640;
    var height = file.Mode == 0 ? 200 : 400;

    // Every mode reads 320 pixels per plane per source row; what differs is how many planes there
    // are, so a row of the file covers twice the screen for each plane it gives up.
    var sourceWidth = 320 << file.Mode;
    var stride = ((sourceWidth + 15) >> 4 << 1) * file.Bitplanes;

    var rgb = new byte[width * height * 3];
    var palette = new byte[16 * 3];
    var cursor = file.PaletteCursor;

    for (var y = 0; y < 200; ++y) {
      // A band's palette is read once, on the first of its four rows, and only if it has one.
      if ((y & 3) == 0 && _HasPalette(data, y >> 2)) {
        cursor -= PaletteSize;

        // The first band always names all sixteen colours; later ones in the wider modes name only
        // the four those modes can show at once.
        var colors = width == 320 || y == 0 ? 16 : 4;
        for (var c = 0; c < colors; ++c) {
          var entry = cursor + c * 3;
          var target = AtariStGraphics.VdiToHardwareIndex(c, colors == 16 ? 4 : 2) * 3;

          for (var channel = 0; channel < 3; ++channel)
            palette[target + channel] = ChannelScaling.Expand3(_At(data, entry + channel) & 7);
        }
      }

      for (var x = 0; x < sourceWidth; ++x) {
        var index = _PlanePixel(bitmap, y * stride, x, file.Bitplanes) * 3;

        // A row of the file is one screen row in the narrow mode and two in the wide ones, laid
        // out end to end — so the source runs on past the screen's width and wraps to the next.
        var target = file.Mode == 0 ? y * width + x : (y * width * 2) + x;
        if (target >= width * height)
          continue;

        rgb[target * 3] = palette[index];
        rgb[target * 3 + 1] = palette[index + 1];
        rgb[target * 3 + 2] = palette[index + 2];

        // The narrow mode shows every row once; the others show each of theirs twice, which for
        // the widest one means the second half of a source row lands on the row it doubled into.
        if (file.Mode == 0 || target + width >= width * height)
          continue;

        rgb[(target + width) * 3] = palette[index];
        rgb[(target + width) * 3 + 1] = palette[index + 1];
        rgb[(target + width) * 3 + 2] = palette[index + 2];
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Whether a band carries a palette of its own; two bytes of 255 say it does not.</summary>
  internal static bool _HasPalette(ReadOnlySpan<byte> data, int band)
    => _At(data, band * 2) != 255 || _At(data, band * 2 + 1) != 255;

  /// <summary>Reads one pixel from planes interleaved a word at a time.</summary>
  private static int _PlanePixel(ReadOnlySpan<byte> bitmap, int rowOffset, int x, int bitplanes) {
    var at = rowOffset + ((x >> 3) & ~1) * bitplanes + ((x >> 3) & 1);
    var bit = ~x & 7;
    var index = 0;

    for (var plane = bitplanes; --plane >= 0;) {
      var source = at + plane * 2;
      index = (index << 1) | (source >= 0 && source < bitmap.Length ? (bitmap[source] >> bit) & 1 : 0);
    }

    return index;
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

  /// <summary>The stored slot a hardware colour number is written in, which is the VDI order reversed.</summary>
  internal static int VdiSlotFor(int hardwareIndex) {
    for (var slot = 0; slot < ColorCount; ++slot)
      if (AtariStGraphics.VdiToHardwareIndex(slot, 4) == hardwareIndex)
        return slot;

    return hardwareIndex;
  }

  /// <summary>Encodes a picture as the narrow mode, giving every band of four rows its own palette.</summary>
  /// <remarks>
  /// Every band gets one. The alternative — one palette for the whole screen and forty-nine bands
  /// saying they have none — is a shorter file showing sixteen colours where the format holds eight
  /// hundred, and the palette per band is the only reason this format exists rather than being a
  /// plain ST screen.
  /// <para/>
  /// Nothing is compressed. The run list fills the parts of a screen that repeat and the rest is
  /// paid for in full afterwards, so a picture with no runs at all is a legal file and the shortest
  /// route to one; what the runs would save is bytes on disk, not colours on screen.
  /// </remarks>
  public static CanvasRasterFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var indices = new byte[Width * Height];

    // Every band carries a palette, so the palettes end where a file with fifty of them ends.
    var cursor = PaletteEnd + PaletteSize * (BandCount - 1);
    var headerAt = cursor + HeaderGap;
    var runsAt = headerAt + 34;
    var bitmapAt = runsAt + 12;
    var data = new byte[bitmapAt + BitmapSize];

    // The run list contributes nothing, so it is the terminator alone.
    data[runsAt] = 255;
    data[runsAt + 1] = 255;

    for (var band = 0; band < BandCount; ++band) {
      var top = band * BandHeight;
      var slice = new byte[Width * BandHeight * 3];
      rgb.PixelData.AsSpan(top * Width * 3, slice.Length).CopyTo(slice);

      var quantized = new RawImage {
        Width = Width, Height = BandHeight, Format = PixelFormat.Rgb24, PixelData = slice,
      }.EnsureIndexedAtMost(ColorCount);

      quantized.PixelData.AsSpan(0, Width * BandHeight).CopyTo(indices.AsSpan(top * Width));

      // The palettes are written backwards, the first band's last, because the routine that loaded
      // them counted down.
      var palette = quantized.Palette ?? [];
      for (var colour = 0; colour < ColorCount; ++colour) {
        var target = cursor - (band + 1) * PaletteSize + VdiSlotFor(colour) * 3;
        for (var channel = 0; channel < 3; ++channel) {
          var at = colour * 3 + channel;
          data[target + channel] = (byte)(at < palette.Length ? (palette[at] * 7 + 127) / 255 : 0);
        }
      }
    }

    var bitmap = AtariStGraphics.PackBitplanes(indices, Stride, 4, Width, Height);
    bitmap.CopyTo(data, bitmapAt);

    return new() { Data = data, Bitmap = bitmap, PaletteCursor = cursor, Bitplanes = 4, Mode = 0 };
  }
}
