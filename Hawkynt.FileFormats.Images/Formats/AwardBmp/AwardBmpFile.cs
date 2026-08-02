using System;
using FileFormat.Core;

namespace FileFormat.AwardBmp;

/// <summary>In-memory representation of an Award BIOS bitmap logo (AWBM).</summary>
/// <remarks>
/// The second of the two things a <c>.epa</c> file can be, and no relation to the first: where the
/// older one is a screenful of text-mode character cells, this is a real bitmap — sixteen colours in
/// four bitplanes, with the palette at the end behind an <c>RGB </c> marker rather than in the
/// header.
/// <para/>
/// Because both use the same extension, the signature is what tells them apart; a file beginning
/// <c>AWBM</c> is this one.
/// <para/>
/// The layout was read off a real file and checked against XnView, which names the format and agrees
/// on its size.
/// </remarks>
[FormatMagicBytes([0x41, 0x57, 0x42, 0x4D])]
public readonly record struct AwardBmpFile : IImageFormatReader<AwardBmpFile>, IImageToRawImage<AwardBmpFile>, IImageFromRawImage<AwardBmpFile>, IImageFormatWriter<AwardBmpFile> {

  /// <summary>The four letters every one of these begins with.</summary>
  internal static ReadOnlySpan<byte> Signature => "AWBM"u8;

  /// <summary>The four letters that stand between the picture and its palette.</summary>
  internal static ReadOnlySpan<byte> PaletteMarker => "RGB "u8;

  /// <summary>Bitplanes the format spends, which is what makes it sixteen colours.</summary>
  internal const int Planes = 4;

  /// <summary>Entries the palette holds.</summary>
  internal const int PaletteCount = 16;

  /// <summary>Bytes one row of one plane takes.</summary>
  internal static int StrideOf(int width) => (width + 7) / 8;

  /// <summary>The length a file of the given size has.</summary>
  internal static int SizeOf(int width, int height)
    => Signature.Length + 4 + StrideOf(width) * Planes * height + PaletteMarker.Length + PaletteCount * 3;

  static string IImageFormatMetadata<AwardBmpFile>.PrimaryExtension => ".epa";
  static string[] IImageFormatMetadata<AwardBmpFile>.FileExtensions => [".epa", ".awbm"];
  static AwardBmpFile IImageFormatReader<AwardBmpFile>.FromSpan(ReadOnlySpan<byte> data) => AwardBmpReader.FromSpan(data);
  static byte[] IImageFormatWriter<AwardBmpFile>.ToBytes(AwardBmpFile file) => AwardBmpWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AwardBmpFile>.VideoModes => [
    new("Default", [(136, 126)], [16])
  ];

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>One index a pixel, none above fifteen.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Sixteen colours, three bytes each, already widened from the six bits the file states.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(AwardBmpFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData,
    Palette = file.Palette,
    PaletteCount = PaletteCount,
  };

  public static AwardBmpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.EnsureIndexedAtMost(PaletteCount);
    var palette = new byte[PaletteCount * 3];
    var source = indexed.Palette;
    if (source != null)
      source.AsSpan(0, Math.Min(source.Length, palette.Length)).CopyTo(palette);

    // The file states each channel in six bits, so anything finer than that cannot survive the trip
    // and is rounded here rather than on the way back out.
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(palette[i] >> 2 << 2 | palette[i] >> 6);

    var pixels = new byte[image.Width * image.Height];
    for (var i = 0; i < pixels.Length && i < indexed.PixelData.Length; ++i)
      pixels[i] = (byte)(indexed.PixelData[i] & 15);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixels,
      Palette = palette,
    };
  }
}
