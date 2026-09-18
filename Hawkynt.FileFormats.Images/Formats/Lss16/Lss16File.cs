using System;
using FileFormat.Core;

namespace FileFormat.Lss16;

/// <summary>In-memory representation of a Syslinux LSS16 splash screen image.</summary>
[FormatMagicBytes([0x3D, 0xF3, 0x13, 0x14])]
[VerifiedBy(ConformanceOracle.XnView)]
public readonly record struct Lss16File : IImageFormatReader<Lss16File>, IImageToRawImage<Lss16File>, IImageFromRawImage<Lss16File>, IImageFormatWriter<Lss16File> {

  /// <summary>Magic bytes identifying an LSS16 file: 0x3D 0xF3 0x13 0x14.</summary>
  internal static readonly byte[] Magic = [0x3D, 0xF3, 0x13, 0x14];

  /// <summary>Header size: 4 bytes magic + 2 bytes width + 2 bytes height + 48 bytes palette = 56 bytes.</summary>
  internal const int HeaderSize = 56;

  /// <summary>Number of palette entries (always 16 for 4-bit indexed).</summary>
  internal const int PaletteEntryCount = 16;

  /// <summary>Bytes per palette entry (R, G, B).</summary>
  internal const int BytesPerPaletteEntry = 3;

  /// <summary>Total palette size in bytes.</summary>
  internal const int PaletteSize = PaletteEntryCount * BytesPerPaletteEntry;

  /// <summary>Default 16-entry grayscale ramp (6-bit VGA scale, 0-63) used when the file carries no palette.</summary>
  private static readonly byte[] _DefaultPalette = _MakeGrayRamp(PaletteEntryCount);

  private static byte[] _MakeGrayRamp(int entries) {
    var p = new byte[entries * BytesPerPaletteEntry];
    for (var i = 0; i < entries; ++i) {
      var v = entries == 1 ? (byte)32 : (byte)(i * 63 / (entries - 1));
      p[i * 3] = v; p[i * 3 + 1] = v; p[i * 3 + 2] = v;
    }
    return p;
  }

  static string IImageFormatMetadata<Lss16File>.PrimaryExtension => ".lss";
  static string[] IImageFormatMetadata<Lss16File>.FileExtensions => [".lss", ".16"];
  static Lss16File IImageFormatReader<Lss16File>.FromSpan(ReadOnlySpan<byte> data) => Lss16Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<Lss16File>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 16)])];
  static byte[] IImageFormatWriter<Lss16File>.ToBytes(Lss16File file) => Lss16Writer.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>16-entry color table, 3 bytes per entry (R, G, B), 6-bit VGA values (0-63).</summary>
  public byte[] Palette { get; init; }

  /// <summary>Pixel data, one byte per pixel, values 0-15 (4-bit color index stored in a full byte).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(Lss16File file) {

    var srcPalette = file.Palette is { Length: > 0 } sp ? sp : _DefaultPalette;
    var expandedPalette = new byte[PaletteSize];
    var copyLen = Math.Min(PaletteSize, srcPalette.Length);
    for (var i = 0; i < copyLen; ++i) {
      // The full six-bit value is full intensity, so the scale is 255/63 and not a shift by two —
      // the latter stops three levels short of white, which is where XnView puts it.
      var val = srcPalette[i] * 255 / 63;
      expandedPalette[i] = (byte)(val > 255 ? 255 : val);
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = expandedPalette,
      PaletteCount = PaletteEntryCount,
    };
  }

  /// <summary>Encodes a picture as an LSS16 splash screen, reducing it to sixteen colours first.</summary>
  /// <remarks>
  /// The colour table lives in the file, so the sixteen colours are the picture's own rather than a
  /// fixed ramp. Quantising against <see cref="_DefaultPalette"/> is what used to happen here and it
  /// was wrong twice over: that ramp is stated in the six-bit scale the file stores, so as an
  /// eight-bit palette it is sixteen shades of near-black, and every picture handed to it came out
  /// as the one entry nearest white — which the file then halved again on its way to six bits.
  /// </remarks>
  public static Lss16File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureIndexedAtMost(PaletteEntryCount);
    if (image.PaletteCount > PaletteEntryCount)
      throw new ArgumentException($"LSS16 supports at most {PaletteEntryCount} palette entries, got {image.PaletteCount}.", nameof(image));

    var palette = new byte[PaletteSize];
    if (image.Palette is not { Length: > 0 }) {
      // Indices with no table to read them by. The ramp is already on the file's scale.
      _DefaultPalette.CopyTo(palette, 0);
    } else {
      // The file states its colours on the VGA's six-bit scale, so the eight-bit ones are scaled
      // down here and back up in ToRawImage.
      var srcPaletteBytes = Math.Min(image.Palette.Length, image.PaletteCount * BytesPerPaletteEntry);
      for (var i = 0; i < srcPaletteBytes; ++i)
        palette[i] = (byte)(image.Palette[i] * 63 / 255);
    }

    var pixelData = image.PixelData[..];
    for (var i = 0; i < pixelData.Length; ++i)
      if (pixelData[i] >= PaletteEntryCount)
        pixelData[i] = 0;

    return new() {
      Width = image.Width,
      Height = image.Height,
      Palette = palette,
      PixelData = pixelData,
    };
  }
}
