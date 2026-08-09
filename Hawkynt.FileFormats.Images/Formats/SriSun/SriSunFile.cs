using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SriSun;

/// <summary>In-memory representation of a SriSun picture (.ssi).</summary>
/// <remarks>
/// Nothing has ever been published about this format. What it is, is settled by the reader XnView
/// carries for it: eight letters, four bytes of which two are checked and one is the depth, the
/// size as two big-endian words, and then a header padded out to 256 bytes with the rows following
/// it uncompressed. Files built to that layout in every depth the reader accepts — one, four, eight,
/// sixteen and twenty-four bits — are read by XnView at the size and depth they were built with, and
/// the pixels it hands back are the ones written, byte for byte.
/// <para/>
/// There is no colour table anywhere in the file and the reader never looks for one, so the shallow
/// depths are greys: a bit set is white, a four-bit sample comes back as itself times seventeen and
/// an eight-bit sample as itself. Sixteen bits is five bits a channel in a little-endian word, and
/// twenty-four is red, green and blue in that order. Each of those five was checked by handing
/// XnView a picture built here and comparing the pixels it returned against the ones encoded.
/// </remarks>
public readonly record struct SriSunFile
  : IImageFormatReader<SriSunFile>, IImageToRawImage<SriSunFile> {

  /// <summary>The eight letters a SriSun picture opens with.</summary>
  public static ReadOnlySpan<byte> Magic => "srisunim"u8;

  /// <summary>Where the byte that has to be zero for the picture to be readable stands.</summary>
  /// <remarks>
  /// XnView refuses anything else here with "Data type is not supported", so whatever other data
  /// types the format once had, this is the only one there is a reading of.
  /// </remarks>
  internal const int DataTypeAt = 9;

  /// <summary>Where the bits a pixel takes stand.</summary>
  internal const int DepthAt = 10;

  /// <summary>Where the byte that has to be two stands.</summary>
  internal const int MarkerAt = 11;

  /// <summary>The value that byte has to hold.</summary>
  internal const int Marker = 2;

  /// <summary>Where the size stands, each as a big-endian word.</summary>
  internal const int WidthAt = 12, HeightAt = 14;

  /// <summary>How long the header is, whatever it fills the rest of it with.</summary>
  public const int HeaderSize = 256;

  /// <summary>The largest side accepted, a word being all the format has to state one.</summary>
  public const int MaximumSide = 65535;

  static string IImageFormatMetadata<SriSunFile>.PrimaryExtension => ".ssi";
  static string[] IImageFormatMetadata<SriSunFile>.FileExtensions => [".ssi"];
  static SriSunFile IImageFormatReader<SriSunFile>.FromSpan(ReadOnlySpan<byte> data) => SriSunReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SriSunFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2, 16, 256, 65536, 16777216])
  ];

  /// <summary>How wide the picture is.</summary>
  public int Width { get; init; }

  /// <summary>How tall it is.</summary>
  public int Height { get; init; }

  /// <summary>Bits one pixel takes: 1, 4, 8, 16 or 24.</summary>
  public int Depth { get; init; }

  /// <summary>The rows as they lie, top row first, each padded out to a whole byte.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>How many bytes one row takes at a given width and depth.</summary>
  public static int StrideOf(int width, int depth) => (width * depth + 7) / 8;

  public static RawImage ToRawImage(SriSunFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture was read.");

    var width = file.Width;
    var height = file.Height;
    var stride = StrideOf(width, file.Depth);
    var rgb = new byte[(long)width * height * 3];

    for (var y = 0; y < height; ++y) {
      var row = y * stride;
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        byte red, green, blue;
        switch (file.Depth) {
          case 24:
            red = file.PixelData[row + x * 3];
            green = file.PixelData[row + x * 3 + 1];
            blue = file.PixelData[row + x * 3 + 2];
            break;
          case 16: {
            // Five bits a channel in a little-endian word, the top bit unused, expanded the way
            // XnView expands it — by the exact fraction rather than by repeating the high bits.
            var word = file.PixelData[row + x * 2] | (file.PixelData[row + x * 2 + 1] << 8);
            red = _FiveToEight((word >> 10) & 0x1F);
            green = _FiveToEight((word >> 5) & 0x1F);
            blue = _FiveToEight(word & 0x1F);
            break;
          }
          case 8:
            red = green = blue = file.PixelData[row + x];
            break;
          case 4: {
            var nibble = (file.PixelData[row + (x >> 1)] >> (x % 2 == 0 ? 4 : 0)) & 0x0F;
            red = green = blue = (byte)(nibble * 17);
            break;
          }
          default: {
            var bit = (file.PixelData[row + (x >> 3)] >> (7 - (x & 7))) & 1;
            red = green = blue = (byte)(bit == 0 ? 0 : 255);
            break;
          }
        }

        rgb[at] = red;
        rgb[at + 1] = green;
        rgb[at + 2] = blue;
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Widens one of the five-bit channels the way the format's own reader does.</summary>
  private static byte _FiveToEight(int value) => (byte)(value * 255 / 31);
}
