using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.GraphSaurus;

/// <summary>In-memory representation of a Graph Saurus MSX2 screen dump.</summary>
/// <remarks>
/// Graph Saurus saves what the video chip holds, behind the seven-byte BSAVE header that says where
/// in memory it belongs — which is what makes the file a file rather than a memory image. The
/// screen mode follows from the length: Screen 5 spends four bits a pixel on a sixteen-colour
/// palette, Screen 8 spends eight on a fixed green-red-blue three-three-two.
/// <para/>
/// What was here before was 54272 bytes with no header at all, always read as Screen 8, and with
/// red and green the wrong way round in the byte. Under <c>.sr5</c>, which is Screen 5's name, that
/// is the wrong depth as well as the wrong layout.
/// </remarks>
public readonly record struct GraphSaurusFile : IImageFormatReader<GraphSaurusFile>, IImageToRawImage<GraphSaurusFile>, IImageFromRawImage<GraphSaurusFile>, IImageFormatWriter<GraphSaurusFile> {

  static string IImageFormatMetadata<GraphSaurusFile>.PrimaryExtension => ".sr5";
  static string[] IImageFormatMetadata<GraphSaurusFile>.FileExtensions => [".sr5", ".grs", ".sr8", ".srs"];
  static GraphSaurusFile IImageFormatReader<GraphSaurusFile>.FromSpan(ReadOnlySpan<byte> data) => GraphSaurusReader.FromSpan(data);

  /// <summary>
  /// Reads a named file, which is the only way the palette beside it and the Screen 12 name are seen.
  /// </summary>
  /// <remarks>
  /// Only the by-bytes entry was wired up, so the registry could never reach the reader that takes a
  /// name — the companion palette went unread and a .srs came back as Screen 8.
  /// </remarks>
  static GraphSaurusFile IImageFormatReader<GraphSaurusFile>.FromFile(FileInfo file) => GraphSaurusReader.FromFile(file);
  static VideoMode[] IImageFormatMetadata<GraphSaurusFile>.VideoModes => [
    new("Screen 5", [(256, 212)], [16]),
    new("Screen 8", [(256, 212)], [256]),
  ];
  static byte[] IImageFormatWriter<GraphSaurusFile>.ToBytes(GraphSaurusFile file) => GraphSaurusWriter.ToBytes(file);

  /// <summary>The palette lives beside the picture rather than in it.</summary>
  internal const string CompanionExtension = ".pl5";

  /// <summary>Fixed image width.</summary>
  public const int FixedWidth = 256;

  /// <summary>Fixed image height.</summary>
  public const int FixedHeight = 212;

  /// <summary>The BSAVE header every Graph Saurus file starts with.</summary>
  public const int HeaderSize = 7;

  /// <summary>Bytes a Screen 5 row takes: four bits a pixel.</summary>
  public const int Screen5Stride = FixedWidth / 2;

  /// <summary>Bytes a Screen 8 row takes: one byte a pixel.</summary>
  public const int Screen8Stride = FixedWidth;

  /// <summary>How many colours a palette entry file holds.</summary>
  internal const int PaletteColors = 16;

  /// <summary>Whether the file spends eight bits a pixel rather than four.</summary>
  public bool IsScreen8 { get; init; }

  /// <summary>
  /// Whether the picture is a Screen 12 one, whose bytes are the V9958's YJK rather than indices.
  /// </summary>
  /// <remarks>
  /// It is exactly as long as a Screen 8 picture, so the length cannot tell them apart and the
  /// extension is what does: <c>.srs</c> against <c>.sr8</c>. Read as Screen 8 the sample came out in
  /// 256 colours where RECOIL draws 2269, which is the giveaway — YJK carries far more colour than a
  /// byte of index can.
  /// </remarks>
  public bool IsYjk { get; init; }

  /// <summary>The bitmap as stored, at the mode's stride.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Sixteen V9938 palette entries, two bytes each. Only Screen 5 has one.</summary>
  public byte[]? Palette { get; init; }

  public int Width => FixedWidth;
  public int Height => FixedHeight;

  /// <summary>Bytes a row of this file takes.</summary>
  public int Stride => this.IsScreen8 ? Screen8Stride : Screen5Stride;

  /// <summary>Which mode a file of a given length is.</summary>
  /// <summary>
  /// Which screen a file of this length holds, taking a length at or above a screen's as that screen.
  /// </summary>
  /// <remarks>
  /// Requiring the length exactly refused two samples that carry a byte or so past the end of the
  /// picture — a BSAVE file states where its data stops and nothing says the file may not go on a
  /// little further. Both are read by every other tool.
  /// </remarks>
  internal static bool ScreenEightAt(int length) {
    var five = HeaderSize + FixedHeight * Screen5Stride;
    var eight = HeaderSize + FixedHeight * Screen8Stride;

    if (length >= eight && length < eight + TrailingSlack)
      return true;
    if (length >= five && length < five + TrailingSlack)
      return false;

    throw new InvalidDataException($"A Graph Saurus screen is {five} or {eight} bytes; this one is {length}.");
  }

  /// <summary>
  /// How far past the end of the picture a file may go and still be one of these.
  /// </summary>
  /// <remarks>
  /// Two samples carry a byte or so beyond it and were refused for being the wrong length. Anything
  /// further off is not padding but a different picture, and a length halfway between the two
  /// screens is still refused — which is what the test for it asks.
  /// </remarks>
  private const int TrailingSlack = 128;

  public static RawImage ToRawImage(GraphSaurusFile file) => file.IsYjk
    ? _DecodeYjk(file)
    : file.IsScreen8
    ? new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = MsxGraphics.Screen8Palette(),
      PaletteCount = 256,
    }
    : new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = PackedRows.Unpack(file.PixelData, FixedWidth, FixedHeight, 4, Screen5Stride),
      Palette = MsxGraphics.PaletteToRgb(file.Palette ?? MsxGraphics.Msx2DefaultPalette, PaletteColors),
      PaletteCount = PaletteColors,
    };

  /// <summary>Writes a Screen 5 picture: sixteen colours the file chooses, at four bits a pixel.</summary>
  /// <remarks>
  /// Screen 8's byte a pixel reaches more colours but only the 256 the chip fixes, none of them a
  /// clean primary; Screen 5 gets sixteen of the 512 the palette can name. For a picture that came
  /// from elsewhere the free choice is worth more than the count.
  /// </remarks>
  /// <summary>Decodes a Screen 12 picture, four pixels at a time sharing one pair of chroma values.</summary>
  private static RawImage _DecodeYjk(GraphSaurusFile file) {
    var pixels = file.PixelData ?? [];
    var rgb = new byte[FixedWidth * FixedHeight * 3];

    for (var y = 0; y < FixedHeight; ++y) {
      var at = y * Screen8Stride;
      if (at + Screen8Stride > pixels.Length)
        break;

      MsxGraphics.DecodeYjkRow(
        pixels.AsSpan(at, Screen8Stride), FixedWidth, usePalette: false, default,
        rgb.AsSpan(y * FixedWidth * 3, FixedWidth * 3));
    }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  public static GraphSaurusFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.SampleTo(FixedWidth, FixedHeight).EnsureIndexedAtMost(PaletteColors);
    var stored = MsxGraphics.PaletteFromRgb(indexed.Palette ?? [], indexed.PaletteCount, PaletteColors);

    return new() {
      IsScreen8 = false,
      PixelData = PackedRows.Pack(indexed.PixelData, FixedWidth, FixedHeight, 4, Screen5Stride),
      Palette = stored,
    };
  }

  /// <summary>Writes the palette file, without which the picture draws in the default sixteen.</summary>
  static void IImageFormatWriter<GraphSaurusFile>.WriteCompanions(GraphSaurusFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    if (file.Palette is not { Length: > 0 } palette)
      return;

    var stored = new byte[PaletteColors * MsxGraphics.PaletteEntrySize];
    palette.AsSpan(0, Math.Min(palette.Length, stored.Length)).CopyTo(stored);
    File.WriteAllBytes(Path.ChangeExtension(target.FullName, CompanionExtension), stored);
  }
}
