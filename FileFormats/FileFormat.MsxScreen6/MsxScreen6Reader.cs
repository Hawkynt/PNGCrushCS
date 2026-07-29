using System;
using System.IO;

namespace FileFormat.MsxScreen6;

/// <summary>Reads MSX2 Screen 6 images from bytes, streams, or file paths.</summary>
public static class MsxScreen6Reader {

  public static MsxScreen6File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Screen 6 image not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxScreen6File FromStream(Stream stream) {
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

  public static MsxScreen6File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MsxScreen6File.BsaveHeaderSize + MsxScreen6File.PixelDataSize)
      throw new InvalidDataException(
        $"A Screen 6 image is at least {MsxScreen6File.BsaveHeaderSize + MsxScreen6File.PixelDataSize} bytes, got {data.Length}.");
    if (data[0] != MsxScreen6File.BsaveMagic)
      throw new InvalidDataException("Not a Screen 6 image: the BSAVE marker is missing.");

    var pixels = new byte[MsxScreen6File.PixelDataSize];
    data.Slice(MsxScreen6File.BsaveHeaderSize, MsxScreen6File.PixelDataSize).CopyTo(pixels);

    // Files short of the full video page carry no palette; the machine's default four apply.
    var palette = new byte[MsxScreen6File.PaletteSize];
    if (data.Length >= MsxScreen6File.FileSize)
      data.Slice(MsxScreen6File.PaletteOffset, MsxScreen6File.PaletteSize).CopyTo(palette);

    return new() { PixelData = pixels, Palette = palette };
  }

  public static MsxScreen6File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
