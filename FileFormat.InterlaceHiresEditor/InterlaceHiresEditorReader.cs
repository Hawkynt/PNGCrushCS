using System;
using System.IO;

namespace FileFormat.InterlaceHiresEditor;

/// <summary>Reads Interlace Hires Editor (.ihe) files from bytes, streams, or file paths.</summary>
public static class InterlaceHiresEditorReader {

  public static InterlaceHiresEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Interlace Hires Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static InterlaceHiresEditorFile FromStream(Stream stream) {
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

  public static InterlaceHiresEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < InterlaceHiresEditorFile.LoadAddressSize + InterlaceHiresEditorFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Interlace Hires Editor file (expected at least {InterlaceHiresEditorFile.LoadAddressSize + InterlaceHiresEditorFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - InterlaceHiresEditorFile.LoadAddressSize];
    data.AsSpan(InterlaceHiresEditorFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
