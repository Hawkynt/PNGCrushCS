using System;
using System.IO;

namespace FileFormat.ZxAttributes;

/// <summary>Reads ZX Spectrum Next (.nxi) images from bytes, streams, or file paths.</summary>
public static class ZxAttributesReader {

  public static ZxAttributesFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("attribute file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxAttributesFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static ZxAttributesFile FromSpan(ReadOnlySpan<byte> data) {
    // No signature; the fixed size is what identifies the format.
    if (data.Length != ZxAttributesFile.FileSize)
      throw new InvalidDataException($"An attribute file is exactly {ZxAttributesFile.FileSize} bytes, got {data.Length}.");

    return new() { AttributeData = data.ToArray() };
  }

  public static ZxAttributesFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
