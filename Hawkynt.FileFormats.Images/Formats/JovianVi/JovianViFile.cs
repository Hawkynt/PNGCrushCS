using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.JovianVi;

/// <summary>In-memory representation of a Jovian Logic VI image.</summary>
/// <remarks>
/// A sixteen-byte header, a 256-entry palette and one byte a pixel — the shape of a VGA screen
/// captured whole, which is what the card this came with did. The palette holds the six bits a VGA
/// digital-to-analogue converter accepted rather than eight, so its entries never exceed 63 and are
/// scaled rather than shifted on the way out.
/// <para/>
/// The header states where the palette and the pixels each begin rather than leaving both implied,
/// which is what lets a file carry something between them.
/// </remarks>
[FormatMagicBytes([0x56, 0x49])]
public readonly record struct JovianViFile
  : IImageFormatReader<JovianViFile>, IImageToRawImage<JovianViFile>,
    IImageFromRawImage<JovianViFile>, IImageFormatWriter<JovianViFile> {

  /// <summary>The two letters every file starts with, and the version that follows them.</summary>
  public const string Signature = "VI";

  /// <summary>Bytes before anything the header points at.</summary>
  public const int HeaderSize = 16;

  /// <summary>Colours the palette holds.</summary>
  public const int PaletteColors = 256;

  /// <summary>Bytes the palette takes.</summary>
  public const int PaletteSize = PaletteColors * 3;

  /// <summary>The largest value a palette channel holds, the converter having six bits.</summary>
  public const int ChannelMax = 63;

  static string IImageFormatMetadata<JovianViFile>.PrimaryExtension => ".vi";
  static string[] IImageFormatMetadata<JovianViFile>.FileExtensions => [".vi"];
  static JovianViFile IImageFormatReader<JovianViFile>.FromSpan(ReadOnlySpan<byte> data) => JovianViReader.FromSpan(data);
  static byte[] IImageFormatWriter<JovianViFile>.ToBytes(JovianViFile file) => JovianViWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<JovianViFile>.VideoModes => [
    new("Default", [(320, 200)], [PaletteColors])
  ];

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>The version character the third byte carries.</summary>
  public byte Version { get; init; }

  /// <summary>The palette as stored, three six-bit channels an entry.</summary>
  public byte[] Palette { get; init; }

  /// <summary>One index a pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Widens a stored channel to eight bits.</summary>
  /// <remarks>
  /// By scaling, not by repeating the top bits: 51 of 63 is 206 rather than the 207 repetition
  /// gives, and the reference decoder produces the former.
  /// </remarks>
  internal static byte Expand(byte value) => (byte)((int)(value & ChannelMax) * 255 / ChannelMax);

  /// <summary>Narrows an eight-bit channel to what the converter accepts.</summary>
  internal static byte Reduce(byte value) => (byte)(value * ChannelMax / 255);

  public static RawImage ToRawImage(JovianViFile file) {
    var palette = new byte[PaletteSize];
    var stored = file.Palette ?? [];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = Expand(i < stored.Length ? stored[i] : (byte)0);

    var pixels = new byte[file.Width * file.Height];
    var data = file.PixelData ?? [];
    data.AsSpan(0, Math.Min(data.Length, pixels.Length)).CopyTo(pixels);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = PaletteColors,
    };
  }

  public static JovianViFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.EnsureIndexedAtMost(PaletteColors);
    var palette = new byte[PaletteSize];
    var source = indexed.Palette ?? [];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = Reduce(i < source.Length ? source[i] : (byte)0);

    return new() {
      Width = indexed.Width,
      Height = indexed.Height,
      Version = (byte)'0',
      Palette = palette,
      PixelData = indexed.PixelData[..],
    };
  }
}
