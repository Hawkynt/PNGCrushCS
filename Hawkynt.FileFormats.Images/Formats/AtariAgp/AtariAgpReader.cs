using System;
using System.IO;

namespace FileFormat.AtariAgp;

/// <summary>Reads Atari 8-bit AGP images from bytes, streams, or file paths.</summary>
public static class AtariAgpReader {

  public static AtariAgpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari AGP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariAgpFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static AtariAgpFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != AtariAgpFile.FileSize)
      throw new InvalidDataException(
        $"An AGP file is {AtariAgpFile.FileSize} bytes whatever mode it holds; this one is {data.Length}.");

    var mode = (AtariAgpMode)data[0];
    if (mode is not (AtariAgpMode.Graphics8 or AtariAgpMode.Graphics9 or AtariAgpMode.Graphics10
        or AtariAgpMode.Graphics11 or AtariAgpMode.Graphics15))
      throw new InvalidDataException($"AGP does not have a mode {data[0]}.");

    return new() {
      Mode = mode,
      Registers = data.Slice(1, AtariAgpFile.RegisterCount).ToArray(),
      Bitmap = data[AtariAgpFile.BitmapOffset..].ToArray(),
    };
  }

  public static AtariAgpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
