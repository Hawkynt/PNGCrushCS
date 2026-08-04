using System;
using System.IO;

namespace FileFormat.SyberiaTexture;

/// <summary>Reads Syberia textures from bytes, streams, or file paths.</summary>
public static class SyberiaTextureReader {

  public static SyberiaTextureFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Syberia texture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SyberiaTextureFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static SyberiaTextureFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 12)
      throw new InvalidDataException($"Data too small for a Syberia texture (got {data.Length} bytes).");

    // What is left of the JFIF block: the terminating nought of its name, a version of 1.x, and a
    // units byte of 0, 1 or 2. Anything else is not one of these.
    if (data[0] != 0x00 || data[1] != 0x01 || data[3] > 0x02)
      throw new InvalidDataException("Not a Syberia texture: it does not begin part-way through a JFIF block.");

    var restored = new byte[SyberiaTextureFile.MissingHead.Length + data.Length];
    SyberiaTextureFile.MissingHead.CopyTo(restored);
    data.CopyTo(restored.AsSpan(SyberiaTextureFile.MissingHead.Length));

    return new() { Restored = restored };
  }

  public static SyberiaTextureFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
