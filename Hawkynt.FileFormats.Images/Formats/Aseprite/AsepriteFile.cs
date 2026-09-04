using System;
using FileFormat.Core;

namespace FileFormat.Aseprite;

/// <summary>In-memory representation of an Aseprite sprite.</summary>
/// <remarks>
/// The pixels are the sprite's first frame with its layers already composed, which is the picture
/// the sprite shows. A sprite's later frames are its animation and a raster has nowhere to put them.
/// </remarks>
[FormatDetectionPriority(300)]
[FormatMimeType("image/x-aseprite", "application/x-aseprite")]
public readonly record struct AsepriteFile : IImageFormatReader<AsepriteFile>, IImageToRawImage<AsepriteFile>, IImageFromRawImage<AsepriteFile>, IImageFormatWriter<AsepriteFile> {

  static string IImageFormatMetadata<AsepriteFile>.PrimaryExtension => ".aseprite";
  static string[] IImageFormatMetadata<AsepriteFile>.FileExtensions => [".aseprite", ".ase"];
  static AsepriteFile IImageFormatReader<AsepriteFile>.FromSpan(ReadOnlySpan<byte> data) => AsepriteReader.FromSpan(data);
  static byte[] IImageFormatWriter<AsepriteFile>.ToBytes(AsepriteFile file) => AsepriteWriter.ToBytes(file);

  /// <summary>
  /// Recognises a sprite by the magic its header states, which sits behind the file size rather than
  /// at the front.
  /// </summary>
  /// <remarks>
  /// The stated size is checked against the header length alone, not against the data handed in: a
  /// caller may be probing the first bytes of a longer file. Requiring the two magics — the file's
  /// at offset four and the first frame's at 132 — keeps four bytes from claiming any file whose
  /// fifth and sixth bytes happen to read 0xA5E0.
  /// </remarks>
  static bool? IImageFormatMetadata<AsepriteFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 134
       && header[4] == 0xE0 && header[5] == 0xA5
       && header[132] == 0xFA && header[133] == 0xF1
      ? true : null;

  public int Width { get; init; }
  public int Height { get; init; }
  public AsepriteColorDepth ColorDepth { get; init; }

  /// <summary>The composed first frame, in the layout <see cref="ColorDepth"/> states.</summary>
  public byte[] PixelData { get; init; }

  public byte[]? Palette { get; init; }
  public int PaletteColorCount { get; init; }

  /// <summary>The palette entry an indexed sprite treats as nothing at all.</summary>
  public byte TransparentIndex { get; init; }

  /// <summary>How many frames the sprite states, of which the first is the picture here.</summary>
  public int FrameCount { get; init; }

  public static RawImage ToRawImage(AsepriteFile file) => file.ColorDepth switch {
    AsepriteColorDepth.Indexed => new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData,
      Palette = file.Palette,
      PaletteCount = file.PaletteColorCount,
    },
    AsepriteColorDepth.Grayscale => new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.GrayAlpha16,
      PixelData = file.PixelData,
    },
    _ => new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgba32,
      PixelData = file.PixelData,
    },
  };

  public static AsepriteFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    image = image.EnsureAnyFormat(PixelFormat.Rgba32, PixelFormat.Indexed8, PixelFormat.GrayAlpha16);

    var depth = image.Format switch {
      PixelFormat.Indexed8 => AsepriteColorDepth.Indexed,
      PixelFormat.GrayAlpha16 => AsepriteColorDepth.Grayscale,
      _ => AsepriteColorDepth.Rgba,
    };

    // Every index of a picture converted here is meant to be drawn. The writer says so by marking
    // its layer as the background, on which the nominated index is a colour like any other, so the
    // nomination itself can stay at the zero Aseprite's own sprites carry.
    var paletteCount = image.PaletteCount > 0 ? image.PaletteCount : (image.Palette?.Length ?? 0) / 3;

    return new AsepriteFile {
      Width = image.Width,
      Height = image.Height,
      ColorDepth = depth,
      PixelData = image.PixelData,
      Palette = image.Palette,
      PaletteColorCount = paletteCount,
      TransparentIndex = 0,
      FrameCount = 1,
    };
  }
}
