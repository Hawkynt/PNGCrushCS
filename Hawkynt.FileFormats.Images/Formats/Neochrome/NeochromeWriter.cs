using System;

namespace FileFormat.Neochrome;

/// <summary>Assembles NEOchrome file bytes from an in-memory representation.</summary>
public static class NeochromeWriter {

  public static byte[] ToBytes(NeochromeFile file) {
    var mode = NeochromeFile.Validate(file, nameof(file));
    var virtualCanvas = unchecked((ushort)file.Flag) == NeochromeFile.VirtualCanvasFlag;
    var storedWidth = virtualCanvas ? (short)640 : (short)320;
    var storedHeight = virtualCanvas ? (short)400 : (short)200;
    var fileName = file.FileName ?? new byte[12];
    var reserved = file.Reserved ?? new short[33];

    var header = new NeochromeHeader(
      file.Flag,
      file.Resolution,
      file.Palette[0], file.Palette[1], file.Palette[2], file.Palette[3],
      file.Palette[4], file.Palette[5], file.Palette[6], file.Palette[7],
      file.Palette[8], file.Palette[9], file.Palette[10], file.Palette[11],
      file.Palette[12], file.Palette[13], file.Palette[14], file.Palette[15],
      fileName,
      file.AnimationLimits,
      unchecked((short)((file.AnimSpeed << 8) | file.AnimDirection)),
      file.AnimSteps,
      file.AnimXOffset,
      file.AnimYOffset,
      file.AnimWidth == 0 ? storedWidth : file.AnimWidth,
      file.AnimHeight == 0 ? storedHeight : file.AnimHeight,
      reserved
    );

    var expectedRaster = checked(((mode.Width + 15) / 16) * mode.Planes * 2 * mode.Height);
    var result = new byte[checked(NeochromeHeader.StructSize + expectedRaster)];
    header.WriteTo(result);
    file.PixelData.CopyTo(result, NeochromeHeader.StructSize);
    return result;
  }
}
