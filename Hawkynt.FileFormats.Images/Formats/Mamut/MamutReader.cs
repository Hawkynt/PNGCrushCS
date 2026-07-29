using System;
using System.IO;

namespace FileFormat.Mamut;

/// <summary>Reads Mamut (.bkg) files from bytes, streams, or file paths.</summary>
public static class MamutReader {

  public static MamutFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Mamut file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MamutFile FromStream(Stream stream) {
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

  public static MamutFile FromSpan(ReadOnlySpan<byte> data) {
    // The format carries no signature; its fixed size is the only thing identifying it.
    if (data.Length != MamutFile.FileSize)
      throw new InvalidDataException(
        $"A Mamut file is exactly {MamutFile.FileSize} bytes, got {data.Length}.");

    var bitmap = new byte[MamutFile.BitmapDataSize];
    data[..MamutFile.BitmapDataSize].CopyTo(bitmap);

    return new() { BitmapData = bitmap };
  }

  public static MamutFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
