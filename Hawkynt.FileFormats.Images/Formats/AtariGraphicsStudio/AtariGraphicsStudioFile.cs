using System;
using FileFormat.Core;

namespace FileFormat.AtariGraphicsStudio;

/// <summary>In-memory representation of an Atari Graphics Studio picture (.ags).</summary>
/// <remarks>
/// Two unrelated screens behind one header, chosen by a mode byte. Both spend their pixels on
/// height rather than width, which is what the editor was for: a picture meant to be redisplayed
/// under program control rather than shown once.
/// <para/>
/// Mode 11 stores two Graphics 15 fields with a set of colour registers each and interleaves them
/// by scanline, so alternate rows draw from different colours — not two frames blended, but one
/// picture with twice the palette down its height. Mode 19 stores a Graphics 9 luminance field and
/// draws every row of it four times, trading vertical resolution for a file a quarter the size.
/// </remarks>
public readonly record struct AtariGraphicsStudioFile
  : IImageFormatReader<AtariGraphicsStudioFile>, IImageToRawImage<AtariGraphicsStudioFile> {

  /// <summary>The text every file starts with.</summary>
  public const string Signature = "AGS";

  /// <summary>Offset of the colour registers.</summary>
  public const int ColorsOffset = 7;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 16;

  /// <summary>The mode byte of the two-field Graphics 15 form.</summary>
  public const byte InterleavedMode = 11;

  /// <summary>The mode byte of the quadrupled Graphics 9 form.</summary>
  public const byte QuadrupledMode = 19;

  static string IImageFormatMetadata<AtariGraphicsStudioFile>.PrimaryExtension => ".ags";
  static string[] IImageFormatMetadata<AtariGraphicsStudioFile>.FileExtensions => [".ags"];
  static AtariGraphicsStudioFile IImageFormatReader<AtariGraphicsStudioFile>.FromSpan(ReadOnlySpan<byte> data)
    => AtariGraphicsStudioReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariGraphicsStudioFile>.VideoModes => [
    new("Atari Graphics Studio", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Which of the two screens the file holds.</summary>
  public byte Mode { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  public static RawImage ToRawImage(AtariGraphicsStudioFile file) {
    var data = file.Data ?? [];
    int width = file.Width, height = file.Height;
    var frame = new byte[width * height];

    if (file.Mode == InterleavedMode) {
      var stride = width >> 3;
      Atari8BitGraphics.DecodeGr15Into(
        data, BitmapOffset, stride, frame, 0, width * 2, width, height / 2,
        Atari8BitGraphics.ReadPf012Bak(data, ColorsOffset));
      Atari8BitGraphics.DecodeGr15Into(
        data, BitmapOffset + (width * height >> 4), stride, frame, width, width * 2, width, height / 2,
        Atari8BitGraphics.ReadPf012Bak(data, ColorsOffset + 4));
    } else
      // Every stored row is drawn four times, which is where the mode's height comes from.
      for (var y = 0; y < 4; ++y)
        Atari8BitGraphics.DecodeGr9Into(
          data, BitmapOffset, width >> 3, frame, y * width, width * 4, width, height / 4, 0);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }
}
