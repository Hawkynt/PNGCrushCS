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

  /// <summary>The bitmap as stored, at the mode's stride.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Sixteen V9938 palette entries, two bytes each. Only Screen 5 has one.</summary>
  public byte[]? Palette { get; init; }

  public int Width => FixedWidth;
  public int Height => FixedHeight;

  /// <summary>Bytes a row of this file takes.</summary>
  public int Stride => this.IsScreen8 ? Screen8Stride : Screen5Stride;

  /// <summary>Which mode a file of a given length is.</summary>
  internal static bool ScreenEightAt(int length) => length switch {
    HeaderSize + FixedHeight * Screen5Stride => false,
    HeaderSize + FixedHeight * Screen8Stride => true,
    _ => throw new InvalidDataException(
      $"A Graph Saurus screen is {HeaderSize + FixedHeight * Screen5Stride} or "
      + $"{HeaderSize + FixedHeight * Screen8Stride} bytes; this one is {length}."),
  };

  public static RawImage ToRawImage(GraphSaurusFile file) => file.IsScreen8
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
