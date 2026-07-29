using System;
using System.IO;

namespace FileFormat.AtariTxs;

/// <summary>Reads Atari 8-bit .txs textures from bytes, streams, or file paths.</summary>
public static class AtariTxsReader {

  public static AtariTxsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Texture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariTxsFile FromStream(Stream stream) {
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

  public static AtariTxsFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != AtariTxsFile.FileSize)
      throw new InvalidDataException($"A texture is {AtariTxsFile.FileSize} bytes, got {data.Length}.");
    if (!data[..AtariTxsFile.Header.Length].SequenceEqual(AtariTxsFile.Header))
      throw new InvalidDataException("Not a texture: the load segment header does not match.");

    var values = new byte[AtariTxsFile.StoredSize * AtariTxsFile.StoredSize];
    data[AtariTxsFile.Header.Length..].CopyTo(values);

    // Every value is a colour, and only sixteen exist; anything larger is a different format.
    foreach (var value in values)
      if (value > 15)
        throw new InvalidDataException($"Not a texture: {value} is not one of the sixteen colours.");

    return new() { Values = values };
  }

  public static AtariTxsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
