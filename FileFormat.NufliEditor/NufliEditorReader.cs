using System;
using System.IO;

namespace FileFormat.NufliEditor;

/// <summary>Reads NUFLI Editor (.nuf/.nup) files from bytes, streams, or file paths.</summary>
public static class NufliEditorReader {

  public static NufliEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NUFLI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NufliEditorFile FromStream(Stream stream) {
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

  public static NufliEditorFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static NufliEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < NufliEditorFile.LoadAddressSize + NufliEditorFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid NUFLI file (expected at least {NufliEditorFile.LoadAddressSize + NufliEditorFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - NufliEditorFile.LoadAddressSize];
    data.AsSpan(NufliEditorFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
