using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ximage;

/// <summary>In-memory representation of an Ximage picture (.xim).</summary>
/// <remarks>
/// The header is text: eleven decimal numbers in fixed-width fields with no separators, then four
/// free-text fields, and the whole of it padded out to 256 bytes. A colour table of 256 red, green
/// and blue triplets follows, which brings the header to the 1024 bytes its second field states, and
/// the picture begins there.
/// <para/>
/// What makes it unlike the other planar formats here is that the planes are whole: all the rows of
/// the first, then all the rows of the second. Three planes are red, green and blue in that order;
/// one plane is an index into the colour table where the file states it has one and a grey where it
/// does not. Rows are either stored flat or run-length coded as a count byte one less than the run
/// and the byte to repeat.
/// <para/>
/// All of that comes from the reader XnView carries for the name. Files built here — one plane flat,
/// three planes flat, and one plane coded — are read by it at the size and depth they were built
/// with, and the pixels it hands back are the ones encoded, through the colour table where there is
/// one.
/// </remarks>
public readonly record struct XimageFile
  : IImageFormatReader<XimageFile>, IImageToRawImage<XimageFile> {

  /// <summary>The version the reader accepts, and the only one there is a reading of.</summary>
  public const int Version = 3;

  /// <summary>How long the header is, colour table and all, which the file states for itself.</summary>
  public const int HeaderSize = 1024;

  /// <summary>Where the colour table stands inside that header.</summary>
  public const int PaletteOffset = 256;

  /// <summary>How many entries it has, of three bytes each.</summary>
  public const int PaletteEntries = 256;

  /// <summary>The largest side the reader accepts.</summary>
  public const int MaximumSide = 16000;

  static string IImageFormatMetadata<XimageFile>.PrimaryExtension => ".xim";
  static string[] IImageFormatMetadata<XimageFile>.FileExtensions => [".xim"];
  static XimageFile IImageFormatReader<XimageFile>.FromSpan(ReadOnlySpan<byte> data) => XimageReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<XimageFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [256, 16777216])
  ];

  /// <summary>How wide the picture is.</summary>
  public int Width { get; init; }

  /// <summary>How tall it is.</summary>
  public int Height { get; init; }

  /// <summary>How many planes it has: one or three.</summary>
  public int Planes { get; init; }

  /// <summary>Whether the file states a colour table for its single plane to index.</summary>
  public bool HasPalette { get; init; }

  /// <summary>The planes already unpacked, one array of width times height bytes each.</summary>
  public byte[][] PlaneData { get; init; }

  /// <summary>The colour table as 256 red, green and blue triplets.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(XimageFile file) {
    if (file.PlaneData == null)
      throw new InvalidOperationException("No picture was read.");

    var count = file.Width * file.Height;

    if (file.Planes == 1 && file.HasPalette)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed8,
        PixelData = file.PlaneData[0][..],
        Palette = file.Palette,
        PaletteCount = PaletteEntries,
      };

    var rgb = new byte[(long)count * 3];
    if (file.Planes == 1) {
      var plane = file.PlaneData[0];
      for (var i = 0; i < count; ++i)
        rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = plane[i];
    } else
      for (var p = 0; p < 3; ++p) {
        var plane = file.PlaneData[p];
        for (var i = 0; i < count; ++i)
          rgb[i * 3 + p] = plane[i];
      }

    return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
