using System;
using FileFormat.Core;

namespace FileFormat.ZxRgb3;

/// <summary>In-memory representation of a ZX Spectrum RGB3 image (.3).</summary>
/// <remarks>
/// Three display-file bitmaps back to back, one per colour component, each in the Spectrum's usual
/// interleaved scanline order. A pixel's colour is whichever components have their bit set, so the
/// eight corners of the RGB cube are exactly the eight colours available — and unlike an ordinary
/// Spectrum screen there are no attribute cells, so the colour can change every pixel.
/// </remarks>
public readonly record struct ZxRgb3File
  : IImageFormatReader<ZxRgb3File>, IImageToRawImage<ZxRgb3File>,
    IImageFromRawImage<ZxRgb3File>, IImageFormatWriter<ZxRgb3File> {

  /// <summary>Size of one component bitmap.</summary>
  public const int BitmapSize = 6144;

  /// <summary>Component bitmaps stored.</summary>
  public const int ComponentCount = 3;

  /// <summary>Total file size.</summary>
  public const int FileSize = BitmapSize * ComponentCount;

  /// <summary>Colours the three bitmaps can express between them.</summary>
  public const int ColorCount = 8;

  /// <summary>Which RGB channel each stored bitmap drives: blue first, then red, then green.</summary>
  private static ReadOnlySpan<int> ChannelOfComponent => [2, 0, 1];

  static string IImageFormatMetadata<ZxRgb3File>.PrimaryExtension => ".3";
  static string[] IImageFormatMetadata<ZxRgb3File>.FileExtensions => [".3"];
  static ZxRgb3File IImageFormatReader<ZxRgb3File>.FromSpan(ReadOnlySpan<byte> data) => ZxRgb3Reader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxRgb3File>.ToBytes(ZxRgb3File file) => ZxRgb3Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZxRgb3File>.VideoModes => [
    new("RGB3", [(ZxSpectrumGraphics.ScreenWidth, ZxSpectrumGraphics.ScreenHeight)], [ColorCount])
  ];

  /// <summary>The three component bitmaps, concatenated.</summary>
  public byte[] BitmapData { get; init; }

  public static RawImage ToRawImage(ZxRgb3File file) {
    var data = file.BitmapData ?? [];
    var rgb = new byte[ZxSpectrumGraphics.ScreenWidth * ZxSpectrumGraphics.ScreenHeight * 3];

    for (var y = 0; y < ZxSpectrumGraphics.ScreenHeight; ++y) {
      var lineOffset = ZxSpectrumGraphics.LineOffset(y);
      for (var x = 0; x < ZxSpectrumGraphics.ScreenWidth; ++x) {
        var offset = lineOffset + (x >> 3);
        var target = (y * ZxSpectrumGraphics.ScreenWidth + x) * 3;
        for (var component = 0; component < ComponentCount; ++component) {
          var index = component * BitmapSize + offset;
          var set = index < data.Length && ((data[index] >> (~x & 7)) & 1) != 0;
          rgb[target + ChannelOfComponent[component]] = set ? (byte)255 : (byte)0;
        }
      }
    }

    return new() {
      Width = ZxSpectrumGraphics.ScreenWidth,
      Height = ZxSpectrumGraphics.ScreenHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  public static ZxRgb3File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != ZxSpectrumGraphics.ScreenWidth || image.Height != ZxSpectrumGraphics.ScreenHeight)
      throw new ArgumentException(
        $"Expected {ZxSpectrumGraphics.ScreenWidth}x{ZxSpectrumGraphics.ScreenHeight} but got {image.Width}x{image.Height}.", nameof(image));

    // Each component is on or off, so encoding is a threshold per channel and nothing more.
    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var data = new byte[FileSize];

    for (var y = 0; y < ZxSpectrumGraphics.ScreenHeight; ++y) {
      var lineOffset = ZxSpectrumGraphics.LineOffset(y);
      for (var x = 0; x < ZxSpectrumGraphics.ScreenWidth; ++x) {
        var pixel = (y * ZxSpectrumGraphics.ScreenWidth + x) * 4;
        for (var component = 0; component < ComponentCount; ++component) {
          // BGRA order puts blue first, so the channel index maps straight onto the source byte.
          if (bgra.PixelData[pixel + (2 - ChannelOfComponent[component])] < 128)
            continue;

          data[component * BitmapSize + lineOffset + (x >> 3)] |= (byte)(0x80 >> (x & 7));
        }
      }
    }

    return new() { BitmapData = data };
  }
}
