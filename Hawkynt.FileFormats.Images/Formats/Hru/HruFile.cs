using System;
using FileFormat.Core;

namespace FileFormat.Hru;

/// <summary>In-memory representation of an HRU picture (.hru).</summary>
/// <remarks>
/// A GIF with its signature replaced. Twenty-eight fixed bytes stand where <c>GIF87a</c> would, and
/// after them the file is a GIF: the seven-byte logical screen descriptor, the global colour table
/// the descriptor asks for, ten bytes where the image descriptor belongs, the code size, the
/// sub-block chain and the trailer.
/// <para/>
/// Those twenty-eight bytes are a constant, not a header — the same run of them opens every one of
/// these, which is what makes them usable as a signature.
/// <para/>
/// The ten bytes standing in for the image descriptor are the one thing that is not GIF. They do not
/// begin with the separator, and read as a descriptor they give a size that is not the one the
/// screen descriptor gives and not the one the picture turns out to be. So they are not read: the
/// size comes from the screen descriptor, and the check that it is the right size is that the coded
/// data unpacks to exactly that many pixels and no more.
/// </remarks>
public readonly record struct HruFile
  : IImageFormatReader<HruFile>, IImageToRawImage<HruFile>,
    IImageFromRawImage<HruFile>, IImageFormatWriter<HruFile> {

  /// <summary>The twenty-eight bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [
    0x35, 0x4B, 0x50, 0x35, 0x31, 0x5D, 0x2A, 0x67, 0x72, 0x72, 0x80, 0x83, 0x85, 0x63,
    0x7A, 0x7D, 0x6B, 0x43, 0x6A, 0x55, 0x49, 0x53, 0x64, 0x4F, 0x51, 0x61, 0x30, 0x0D,
  ];

  /// <summary>How long that run is, and so where the GIF part starts.</summary>
  public const int MagicSize = 28;

  /// <summary>Width, height, flags, background and aspect.</summary>
  public const int ScreenDescriptorSize = 7;

  /// <summary>The slot the image descriptor would occupy, which this file does not fill with one.</summary>
  public const int ImageDescriptorSize = 10;

  static string IImageFormatMetadata<HruFile>.PrimaryExtension => ".hru";
  static string[] IImageFormatMetadata<HruFile>.FileExtensions => [".hru"];
  static HruFile IImageFormatReader<HruFile>.FromSpan(ReadOnlySpan<byte> data) => HruReader.FromSpan(data);
  static byte[] IImageFormatWriter<HruFile>.ToBytes(HruFile file) => HruWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<HruFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>One palette index per pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The global colour table, as RGB triplets.</summary>
  public byte[] Palette { get; init; }

  /// <summary>How many entries that table has.</summary>
  public int PaletteCount { get; init; }

  public static RawImage ToRawImage(HruFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = file.PaletteCount,
    };
  }

  /// <summary>Reduces the picture to the indexed one the global colour table addresses.</summary>
  public static HruFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.EnsureIndexedAtMost(256);
    return new() {
      Width = indexed.Width,
      Height = indexed.Height,
      PixelData = indexed.PixelData,
      Palette = indexed.Palette ?? new byte[768],
      PaletteCount = indexed.PaletteCount > 0 ? indexed.PaletteCount : 256,
    };
  }
}
