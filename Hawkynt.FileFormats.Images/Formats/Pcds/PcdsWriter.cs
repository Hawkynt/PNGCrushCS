using System;
using System.IO;
using FileFormat.Pcd;

namespace FileFormat.Pcds;

/// <summary>Assembles a Photo CD whose planes hold the channels themselves.</summary>
/// <remarks>
/// The same pyramid at the same offsets the <c>.pcd</c> writer builds — a reader takes whichever
/// size it wants, so all three are filled — with the colour transform left out. The chrominance
/// planes are still at half resolution each way, which the container fixes and the colour space
/// has no say in, so green and blue come back as the mean of the four pixels each sample stood for.
/// </remarks>
public static class PcdsWriter {

  public static byte[] ToBytes(PcdsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return PcdWriter.Assemble(file.Width, file.Height, file.PixelData, photoYcc: false);
  }

  public static void ToFile(PcdsFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
