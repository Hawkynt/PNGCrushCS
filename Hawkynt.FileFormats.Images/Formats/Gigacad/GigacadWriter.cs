using System;

namespace FileFormat.Gigacad;

/// <summary>Assembles GigaCAD picture bytes from a GigacadFile.</summary>
/// <remarks>
/// This wrote the Atari shape of 32000 bytes whatever it was given, so a Commodore picture — which
/// is what both samples are, and the only length RECOIL accepts at this extension — came back out
/// as something nothing reads.
/// </remarks>
public static class GigacadWriter {

  public static byte[] ToBytes(GigacadFile file) {
    ArgumentNullException.ThrowIfNull(file.PixelData);

    if (file.Width != 320 || file.Height != 200) {
      var atari = new byte[GigacadFile.ExpectedFileSize];
      file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, GigacadFile.ExpectedFileSize)).CopyTo(atari);
      return atari;
    }

    // Two bytes of load address, then the screen a cell at a time.
    var result = new byte[GigacadFile.CommodoreFileSize];
    var cells = GigacadFile.RowsToCells(
      file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, GigacadFile.CommodoreScreenSize)), 320, 200);
    cells.AsSpan(0, GigacadFile.CommodoreScreenSize).CopyTo(result.AsSpan(2));

    return result;
  }
}
