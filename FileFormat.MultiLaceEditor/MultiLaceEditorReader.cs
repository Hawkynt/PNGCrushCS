using System;
using System.IO;

namespace FileFormat.MultiLaceEditor;

/// <summary>Reads Multi-Lace Editor (.mle) files from bytes, streams, or file paths.</summary>
public static class MultiLaceEditorReader {

  public static MultiLaceEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Multi-Lace Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MultiLaceEditorFile FromStream(Stream stream) {
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

  public static MultiLaceEditorFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < MultiLaceEditorFile.LoadAddressSize + MultiLaceEditorFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Multi-Lace Editor file (expected at least {MultiLaceEditorFile.LoadAddressSize + MultiLaceEditorFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - MultiLaceEditorFile.LoadAddressSize];
    data.Slice(MultiLaceEditorFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
    }

  public static MultiLaceEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MultiLaceEditorFile.LoadAddressSize + MultiLaceEditorFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Multi-Lace Editor file (expected at least {MultiLaceEditorFile.LoadAddressSize + MultiLaceEditorFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - MultiLaceEditorFile.LoadAddressSize];
    data.AsSpan(MultiLaceEditorFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
