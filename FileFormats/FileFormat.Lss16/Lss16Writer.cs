using System;
using System.IO;

namespace FileFormat.Lss16;

/// <summary>Assembles Syslinux LSS16 file bytes from an <see cref="Lss16File"/>.</summary>
public static class Lss16Writer {

  public static byte[] ToBytes(Lss16File file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.Width, file.Height, file.Palette, file.PixelData);
  }

  internal static byte[] Assemble(int width, int height, byte[] palette, byte[] pixelData) {
    using var ms = new MemoryStream();

    ms.Write(Lss16File.Magic, 0, Lss16File.Magic.Length);

    ms.WriteByte((byte)(width & 0xFF));
    ms.WriteByte((byte)((width >> 8) & 0xFF));
    ms.WriteByte((byte)(height & 0xFF));
    ms.WriteByte((byte)((height >> 8) & 0xFF));

    var paletteBytes = new byte[Lss16File.PaletteSize];
    palette.AsSpan(0, Math.Min(palette.Length, Lss16File.PaletteSize)).CopyTo(paletteBytes);
    ms.Write(paletteBytes, 0, Lss16File.PaletteSize);

    _EncodePixels(ms, width, height, pixelData);

    return ms.ToArray();
  }

  /// <summary>Encodes pixel data using the LSS16 nybble-based RLE scheme.</summary>
  private static void _EncodePixels(MemoryStream ms, int width, int height, byte[] pixelData) {
    var pendingNybble = -1;

    void WriteNybble(int nybble) {
      if (pendingNybble < 0) {
        pendingNybble = nybble & 0x0F;
      } else {
        ms.WriteByte((byte)(pendingNybble | ((nybble & 0x0F) << 4)));
        pendingNybble = -1;
      }
    }

    void FlushNybble() {
      if (pendingNybble >= 0) {
        ms.WriteByte((byte)pendingNybble);
        pendingNybble = -1;
      }
    }

    for (var y = 0; y < height; ++y) {
      byte previousPixel = 0;
      pendingNybble = -1;
      var x = 0;
      var rowOffset = y * width;

      while (x < width) {
        var pixel = x < pixelData.Length - rowOffset ? pixelData[rowOffset + x] : (byte)0;

        if (pixel != previousPixel) {
          WriteNybble(pixel);
          previousPixel = pixel;
          ++x;
        } else {
          var runStart = x;
          while (x < width && pixelData[rowOffset + x] == previousPixel)
            ++x;

          var runCount = x - runStart;

          while (runCount > 0) {
            if (runCount <= 15) {
              WriteNybble(previousPixel);
              WriteNybble(runCount);
              runCount = 0;
            } else {
              var chunk = Math.Min(runCount, 255 + 16);
              WriteNybble(previousPixel);
              WriteNybble(0);
              var encoded = chunk - 16;
              WriteNybble((encoded >> 4) & 0x0F);
              WriteNybble(encoded & 0x0F);
              runCount -= chunk;
            }
          }
        }
      }

      FlushNybble();
    }
  }
}
