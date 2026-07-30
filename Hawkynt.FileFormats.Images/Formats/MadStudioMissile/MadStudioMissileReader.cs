using System;
using System.IO;

namespace FileFormat.MadStudioMissile;

/// <summary>Reads Mad Studio missiles from bytes, streams, or file paths.</summary>
public static class MadStudioMissileReader {

  public static MadStudioMissileFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Missile not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MadStudioMissileFile FromStream(Stream stream) {
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

  public static MadStudioMissileFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MadStudioMissileFile.RowOffset + 1
        || data.Length > MadStudioMissileFile.RowOffset + MadStudioMissileFile.MaxHeight)
      throw new InvalidDataException($"Not a Mad Studio missile: {data.Length} bytes.");

    var height = data[0];
    if (MadStudioMissileFile.RowOffset + height != data.Length)
      throw new InvalidDataException($"Declared height {height} does not match {data.Length} bytes.");

    var rows = data[MadStudioMissileFile.RowOffset..].ToArray();

    // Only two bits of each row reach the screen; anything else means this is not the format.
    foreach (var row in rows)
      if (row > 3)
        throw new InvalidDataException("Not a Mad Studio missile: a row uses more than two bits.");

    return new() { Color = data[1], Rows = rows };
  }

  public static MadStudioMissileFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
