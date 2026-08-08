using System;

namespace FileFormat.HardColorMap;

/// <summary>Assembles Hard Color Map bytes from a <see cref="HardColorMapFile"/>.</summary>
/// <remarks>
/// The file is one fixed-size array from its signature to its playfield, and the reader keeps it
/// whole because every area sits at a stated offset. So writing it is returning it, and the
/// assembling is done where the picture is turned into it.
/// </remarks>
public static class HardColorMapWriter {

  public static byte[] ToBytes(HardColorMapFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var source = file.Data ?? [];
    var data = new byte[HardColorMapFile.FileSize];
    source.AsSpan(0, Math.Min(source.Length, data.Length)).CopyTo(data);

    return data;
  }
}
