using System;
using FileFormat.Core;

namespace FileFormat.BoogieDownPaint;

/// <summary>In-memory representation of a Boogie Down Paint picture (.bdp).</summary>
/// <remarks>
/// A packed Koala screen — the C64's standard multicolour layout of bitmap, screen and colour
/// memory with one background byte — in whichever of three encodings the program was using when it
/// saved. They are told apart by what the file starts with rather than by a version number, because
/// the earliest of the three has no header at all.
/// </remarks>
public readonly record struct BoogieDownPaintFile
  : IImageFormatReader<BoogieDownPaintFile>, IImageToRawImage<BoogieDownPaintFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 160;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Size of the screen a file unpacks to.</summary>
  public const int UnpackedSize = 10001;

  /// <summary>Offset of the video matrix within the unpacked screen.</summary>
  public const int MatrixOffset = 8000;

  /// <summary>Offset of the colour memory within the unpacked screen.</summary>
  public const int ColorOffset = 9000;

  /// <summary>Offset of the background colour within the unpacked screen.</summary>
  public const int BackgroundOffset = 10000;

  static string IImageFormatMetadata<BoogieDownPaintFile>.PrimaryExtension => ".bdp";
  static string[] IImageFormatMetadata<BoogieDownPaintFile>.FileExtensions => [".bdp"];
  static BoogieDownPaintFile IImageFormatReader<BoogieDownPaintFile>.FromSpan(ReadOnlySpan<byte> data)
    => BoogieDownPaintReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<BoogieDownPaintFile>.VideoModes => [
    new("Boogie Down Paint", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The unpacked screen.</summary>
  public byte[] ScreenData { get; init; }

  public static RawImage ToRawImage(BoogieDownPaintFile file) {
    var data = file.ScreenData ?? [];

    return Commodore64Graphics.DecodeMulticolor(
      data, data.AsSpan(MatrixOffset), data.AsSpan(ColorOffset), data[BackgroundOffset], Width, Height);
  }
}
