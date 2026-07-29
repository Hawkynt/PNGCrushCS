using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MsxScreen10;

/// <summary>Reads MSX2+ Screen 10 pictures from bytes, streams, or file paths.</summary>
public static class MsxScreen10Reader {

  public static MsxScreen10File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Screen 10 picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxScreen10File FromStream(Stream stream) {
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

  public static MsxScreen10File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MsxScreen10File.FileSize)
      throw new InvalidDataException($"A Screen 10 picture is {MsxScreen10File.FileSize} bytes, got {data.Length}.");
    if (data[0] != MsxGraphics.BsaveMagic)
      throw new InvalidDataException("Not a Screen 10 picture: the BSAVE marker is missing.");

    // The header has to account for the whole bitmap; anything less is a different picture in a
    // file that merely happens to be long enough.
    if (MsxGraphics.ReadBsaveEndAddress(data) < MsxScreen10File.BsaveEndAddress)
      throw new InvalidDataException("Not a Screen 10 picture: the BSAVE header stops short of the bitmap.");

    var pixels = new byte[MsxScreen10File.PixelDataSize];
    data.Slice(MsxScreen10File.PixelDataOffset, MsxScreen10File.PixelDataSize).CopyTo(pixels);

    var palette = new byte[MsxScreen10File.PaletteSize];
    data.Slice(MsxScreen10File.PaletteOffset, MsxScreen10File.PaletteSize).CopyTo(palette);

    return new() { PixelData = pixels, Palette = palette };
  }

  public static MsxScreen10File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
