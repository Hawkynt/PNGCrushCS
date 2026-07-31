using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ComputerEyesSt;

/// <summary>Which of the three pictures a ComputerEyes ST file holds.</summary>
public enum ComputerEyesStKind {

  /// <summary>320 by 200 in colour, six bits a channel in three separate planes.</summary>
  Color,

  /// <summary>640 by 200 in fifteen-bit colour, drawn at 640 by 400.</summary>
  HighResolutionColor,

  /// <summary>640 by 400 in grey.</summary>
  Grey,
}

/// <summary>In-memory representation of a ComputerEyes ST capture (.ce3).</summary>
/// <remarks>
/// The output of a video digitiser, which is why it is stored the way it is: column by column
/// rather than row by row, because the hardware sampled one column of the picture per television
/// frame and wrote it out as it arrived. The three modes differ in more than depth — one is three
/// separate planes of six-bit channels, one is fifteen-bit words, and one is grey in a range that
/// stops at 191 — so little is shared between them beyond the four-byte signature.
/// <para/>
/// It is not the Atari 8-bit ComputerEyes format, which shares the name and most of an extension.
/// </remarks>
public readonly record struct ComputerEyesStFile
  : IImageFormatReader<ComputerEyesStFile>, IImageToRawImage<ComputerEyesStFile> {

  /// <summary>Rows the digitiser captured, whatever the picture is drawn at.</summary>
  public const int CapturedHeight = 200;

  static string IImageFormatMetadata<ComputerEyesStFile>.PrimaryExtension => ".ce3";
  static string[] IImageFormatMetadata<ComputerEyesStFile>.FileExtensions => [".ce3"];
  static ComputerEyesStFile IImageFormatReader<ComputerEyesStFile>.FromSpan(ReadOnlySpan<byte> data)
    => ComputerEyesStReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ComputerEyesStFile>.VideoModes => [
    new("ComputerEyes", [(320, 200), (640, 400)], [262144])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Which of the three pictures it is.</summary>
  public ComputerEyesStKind Kind { get; init; }

  public static RawImage ToRawImage(ComputerEyesStFile file) {
    var data = file.Data ?? [];

    return file.Kind switch {
      ComputerEyesStKind.Color => _Color(data),
      ComputerEyesStKind.HighResolutionColor => _HighResolutionColor(data),
      _ => _Grey(data),
    };
  }

  /// <summary>Three planes of six-bit channels, each stored a column at a time.</summary>
  private static RawImage _Color(ReadOnlySpan<byte> data) {
    const int width = 320, height = CapturedHeight, plane = width * height;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var source = 22 + x * height + y;
      var target = (y * width + x) * 3;

      for (var channel = 0; channel < 3; ++channel) {
        var value = data[source + plane * channel];

        // Six bits a channel, so a byte using the top two is not a capture at all.
        if (value > 63)
          throw new InvalidDataException($"A colour channel holds {value}, which is more than six bits.");

        rgb[target + channel] = ChannelScaling.Expand6(value);
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Fifteen-bit words, one captured row drawn as two of the picture.</summary>
  private static RawImage _HighResolutionColor(ReadOnlySpan<byte> data) {
    const int width = 640, height = 400;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < CapturedHeight; ++y)
    for (var x = 0; x < width; ++x) {
      var source = (11 + x * CapturedHeight + y) << 1;
      var word = (data[source] << 8) | data[source + 1];

      // The top bit belongs to no channel, so a word that sets it is not a capture.
      if (word >= 32768)
        throw new InvalidDataException("A pixel sets a bit the format has no channel for.");

      var target = ((y << 1) * width + x) * 3;
      rgb[target] = rgb[target + width * 3] = ChannelScaling.Expand5((word >> 10) & 31);
      rgb[target + 1] = rgb[target + width * 3 + 1] = ChannelScaling.Expand5((word >> 5) & 31);
      rgb[target + 2] = rgb[target + width * 3 + 2] = ChannelScaling.Expand5(word & 31);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>
  /// Grey at 640 by 400, a column's two fields stored one after the other so that all its even rows
  /// precede all its odd ones.
  /// </summary>
  private static RawImage _Grey(ReadOnlySpan<byte> data) {
    const int width = 640, height = 400;
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var value = data[22 + x * height + (y & 1) * CapturedHeight + (y >> 1)];

      // The digitiser's range stops at 191, so reaching a byte is a scale and not a shift.
      if (value > 191)
        throw new InvalidDataException($"A grey level of {value} is past the digitiser's range.");

      pixels[y * width + x] = (byte)(value * 4 / 3);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };
  }
}
