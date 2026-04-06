using System;
using System.IO;

namespace FileFormat.FliEditor;

/// <summary>Reads FLI Editor (.fed) files from bytes, streams, or file paths.</summary>
public static class FliEditorReader {

  public static FliEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLI Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FliEditorFile FromStream(Stream stream) {
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

  public static FliEditorFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static FliEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FliEditorFile.LoadAddressSize + FliEditorFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid FLI Editor file (expected at least {FliEditorFile.LoadAddressSize + FliEditorFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - FliEditorFile.LoadAddressSize];
    data.AsSpan(FliEditorFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
