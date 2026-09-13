using System;
using System.IO;

namespace FileFormat.TextureMaker0;

/// <summary>Reads ZX Spectrum Next (.nxi) images from bytes, streams, or file paths.</summary>
public static class TextureMaker0Reader {

  public static TextureMaker0File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Texture Maker0 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TextureMaker0File FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static TextureMaker0File FromSpan(ReadOnlySpan<byte> data) {
    // No signature; the fixed size is what identifies the format.
    if (data.Length != TextureMaker0File.FileSize)
      throw new InvalidDataException($"An Texture Maker0 file is exactly {TextureMaker0File.FileSize} bytes, got {data.Length}.");

    var texels = new byte[TextureMaker0File.TexelDataSize];
    data[..TextureMaker0File.TexelDataSize].CopyTo(texels);

    return new() { TexelData = texels, Color = data[TextureMaker0File.ColorOffset] };
  }

  public static TextureMaker0File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
