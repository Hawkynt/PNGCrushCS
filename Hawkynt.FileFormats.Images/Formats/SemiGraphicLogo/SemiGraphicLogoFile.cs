using System;
using FileFormat.Core;

namespace FileFormat.SemiGraphicLogo;

/// <summary>In-memory representation of a Semi-Graphic logos Editor screen (.sge).</summary>
/// <remarks>
/// A plain Atari 8-bit text screen — 960 character codes and nothing else, not even a header — so
/// the picture exists only once the machine's own character set is applied to it.
/// <para/>
/// The editor patches four characters of that set before drawing: two become solid halves so that a
/// logo can be built out of blocks the stock font has no shape for. Those four bytes are the whole
/// of what makes it a drawing program rather than a text editor.
/// </remarks>
public readonly record struct SemiGraphicLogoFile
  : IImageFormatReader<SemiGraphicLogoFile>, IImageToRawImage<SemiGraphicLogoFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Character cells across.</summary>
  public const int Columns = Width / 8;

  /// <summary>Total file size: one code per cell.</summary>
  public const int FileSize = Columns * (Height / 8);

  static string IImageFormatMetadata<SemiGraphicLogoFile>.PrimaryExtension => ".sge";
  static string[] IImageFormatMetadata<SemiGraphicLogoFile>.FileExtensions => [".sge"];
  static SemiGraphicLogoFile IImageFormatReader<SemiGraphicLogoFile>.FromSpan(ReadOnlySpan<byte> data)
    => SemiGraphicLogoReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SemiGraphicLogoFile>.VideoModes => [
    new("Atari 8-bit", [(Width, Height)], [2])
  ];

  /// <summary>The character codes.</summary>
  public byte[] Characters { get; init; }

  public static RawImage ToRawImage(SemiGraphicLogoFile file) {
    var font = CharacterRoms.Atari8.ToArray();

    // Two characters become a solid left half and two a solid right half, giving the editor the
    // block shapes the stock set has not got.
    for (var i = 0; i < 4; ++i) {
      font[1004 + i] = font[728 + i] = 15;
      font[1000 + i] = font[732 + i] = 240;
    }

    var frame = new byte[Width * Height];
    CharacterRoms.DecodeGraphics0(file.Characters ?? [], 0, Columns, font, frame, Width, Height);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }
}
