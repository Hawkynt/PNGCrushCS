using System;

namespace FileFormat.Neochrome;

/// <summary>Assembles NEOchrome file bytes from an in-memory representation.</summary>
public static class NeochromeWriter {

  private const int _FileSize = NeochromeHeader.StructSize + 32000;

  public static byte[] ToBytes(NeochromeFile file) => Assemble(
    file.Flag,
    file.Palette,
    file.AnimSpeed,
    file.AnimDirection,
    file.AnimSteps,
    file.AnimXOffset,
    file.AnimYOffset,
    file.AnimWidth,
    file.AnimHeight,
    file.PixelData
  );

  internal static byte[] Assemble(
    short flag,
    short[] palette,
    byte animSpeed,
    byte animDirection,
    short animSteps,
    short animXOffset,
    short animYOffset,
    short animWidth,
    short animHeight,
    byte[] pixelData
  ) {
    var result = new byte[_FileSize];
    var span = result.AsSpan();

    var header = new NeochromeHeader(
      flag,
      0, // Resolution always 0 for low-res
      palette[0], palette[1], palette[2], palette[3],
      palette[4], palette[5], palette[6], palette[7],
      palette[8], palette[9], palette[10], palette[11],
      palette[12], palette[13], palette[14], palette[15],
      animSpeed,
      animDirection,
      animSteps,
      animXOffset,
      animYOffset,
      animWidth,
      animHeight
    );
    header.WriteTo(span);

    pixelData.AsSpan(0, Math.Min(32000, pixelData.Length)).CopyTo(result.AsSpan(NeochromeHeader.StructSize));

    return result;
  }
}
