using System;
using System.IO;

namespace FileFormat.DoodleComp;

/// <summary>Assembles Commodore 64 Doodle Compressed hires file bytes from a DoodleCompFile.</summary>
public static class DoodleCompWriter {

  public static byte[] ToBytes(DoodleCompFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var output = new MemoryStream();

    // Load address (2 bytes, little-endian)
    output.WriteByte((byte)(file.LoadAddress & 0xFF));
    output.WriteByte((byte)(file.LoadAddress >> 8));

    // Combine bitmap + screen data
    var raw = new byte[DoodleCompFile.DecompressedDataSize];
    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, DoodleCompFile.BitmapDataSize)).CopyTo(raw.AsSpan(0));
    file.ScreenRam.AsSpan(0, Math.Min(file.ScreenRam.Length, DoodleCompFile.ScreenRamSize)).CopyTo(raw.AsSpan(DoodleCompFile.BitmapDataSize));

    _Compress(raw, output);

    return output.ToArray();
  }

  private static void _Compress(byte[] data, Stream output) {
    var i = 0;
    while (i < data.Length) {
      var current = data[i];
      var runLength = 1;
      while (i + runLength < data.Length && data[i + runLength] == current && runLength < 255)
        ++runLength;

      if (runLength >= 3 || current == DoodleCompFile.RleEscapeByte) {
        output.WriteByte(DoodleCompFile.RleEscapeByte);
        output.WriteByte((byte)runLength);
        output.WriteByte(current);
      } else {
        for (var j = 0; j < runLength; ++j)
          output.WriteByte(current);
      }

      i += runLength;
    }
  }
}
