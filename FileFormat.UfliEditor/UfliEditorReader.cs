using System;
using System.IO;

namespace FileFormat.UfliEditor;

/// <summary>Reads UFLI Editor (.ufl) files from bytes, streams, or file paths.</summary>
public static class UfliEditorReader {

  public static UfliEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("UFLI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static UfliEditorFile FromStream(Stream stream) {
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

  public static UfliEditorFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < UfliEditorFile.LoadAddressSize + UfliEditorFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid UFLI file (expected at least {UfliEditorFile.LoadAddressSize + UfliEditorFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - UfliEditorFile.LoadAddressSize];
    data.Slice(UfliEditorFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
    }

  public static UfliEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < UfliEditorFile.LoadAddressSize + UfliEditorFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid UFLI file (expected at least {UfliEditorFile.LoadAddressSize + UfliEditorFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - UfliEditorFile.LoadAddressSize];
    data.AsSpan(UfliEditorFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
