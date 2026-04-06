using System;
using System.IO;

namespace FileFormat.EmcEditor;

/// <summary>Reads Commodore 64 EMC Editor (.emc) files from bytes, streams, or file paths.</summary>
public static class EmcEditorReader {

  public static EmcEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("EMC Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EmcEditorFile FromStream(Stream stream) {
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

  public static EmcEditorFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static EmcEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < EmcEditorFile.LoadAddressSize + EmcEditorFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid EMC Editor file (expected at least {EmcEditorFile.LoadAddressSize + EmcEditorFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - EmcEditorFile.LoadAddressSize];
    data.AsSpan(EmcEditorFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
