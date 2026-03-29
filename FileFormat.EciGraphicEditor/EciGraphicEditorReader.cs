using System;
using System.IO;

namespace FileFormat.EciGraphicEditor;

/// <summary>Reads ECI Graphic Editor (.eci/.ecp) files from bytes, streams, or file paths.</summary>
public static class EciGraphicEditorReader {

  public static EciGraphicEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ECI Graphic Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EciGraphicEditorFile FromStream(Stream stream) {
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

  public static EciGraphicEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < EciGraphicEditorFile.LoadAddressSize + EciGraphicEditorFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid ECI Graphic Editor file (expected at least {EciGraphicEditorFile.LoadAddressSize + EciGraphicEditorFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - EciGraphicEditorFile.LoadAddressSize];
    data.AsSpan(EciGraphicEditorFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
