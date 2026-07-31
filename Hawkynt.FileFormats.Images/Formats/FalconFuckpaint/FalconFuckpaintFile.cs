using System;
using FileFormat.Core;

namespace FileFormat.FalconFuckpaint;

/// <summary>In-memory representation of a Falcon Fuckpaint picture (.pi4, .pi7, .pi9).</summary>
/// <remarks>
/// A Falcon painting program that writes its 256-colour palette and then eight bitplanes, with no
/// header and no signature at all: the length alone says which of three sizes it is. It has nothing
/// to do with the Commodore 64 program of the same name, which is a pair of interlaced multicolour
/// screens; the two share a name and nothing else.
/// <para/>
/// The extensions are borrowed from the Atari TT and Graphics 9 formats, so only content
/// distinguishes them.
/// </remarks>
public readonly record struct FalconFuckpaintFile
  : IImageFormatReader<FalconFuckpaintFile>, IImageToRawImage<FalconFuckpaintFile> {

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 256;

  /// <summary>Bytes the palette occupies: four a colour, of which the third is unused.</summary>
  public const int PaletteSize = ColorCount * 4;

  /// <summary>Bitplanes a pixel is built from.</summary>
  public const int Bitplanes = 8;

  static string IImageFormatMetadata<FalconFuckpaintFile>.PrimaryExtension => ".pi4";
  static string[] IImageFormatMetadata<FalconFuckpaintFile>.FileExtensions => [".pi4", ".pi7", ".pi9"];
  static FalconFuckpaintFile IImageFormatReader<FalconFuckpaintFile>.FromSpan(ReadOnlySpan<byte> data)
    => FalconFuckpaintReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<FalconFuckpaintFile>.VideoModes => [
    new("Falcon", [(320, 200), (320, 240), (640, 480)], [ColorCount])
  ];

  /// <summary>The whole file: the palette and then the bitplanes.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  public static RawImage ToRawImage(FalconFuckpaintFile file) {
    var data = file.Data ?? [];

    // Eight planes of one bit make a row exactly as many bytes as the picture is wide.
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = AtariStGraphics.UnpackBitplanes(data, PaletteSize, file.Width, Bitplanes, file.Width, file.Height),
      Palette = AtariStGraphics.ReadFalconPalette(data, 0, ColorCount),
      PaletteCount = ColorCount,
    };
  }
}
