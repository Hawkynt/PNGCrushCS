using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.GraphSaurus6;

/// <summary>Reads Graph Saurus Screen 6 pictures from bytes, streams, or file paths.</summary>
public static class GraphSaurus6Reader {

  public static GraphSaurus6File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Graph Saurus picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GraphSaurus6File FromStream(Stream stream) {
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

  public static GraphSaurus6File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 135 || data[0] != MsxGraphics.BsaveMagic)
      throw new InvalidDataException("Not a Graph Saurus picture: the BSAVE marker is missing.");

    // The header's end address gives the height: one row per 128 bytes, and the address is the last
    // byte rather than the count, so it rounds up.
    var end = MsxGraphics.ReadBsaveEndAddress(data);
    if (end < 127)
      throw new InvalidDataException("Not a Graph Saurus picture: the BSAVE header describes no rows.");

    var stored = (end + 1) >> 7;
    if (data.Length < GraphSaurus6File.BitmapOffset + (stored << 7))
      throw new InvalidDataException($"A {stored}-row picture needs {GraphSaurus6File.BitmapOffset + (stored << 7)} bytes, got {data.Length}.");

    if (stored > GraphSaurus6File.MaxHeight)
      stored = GraphSaurus6File.MaxHeight;

    var pixels = new byte[stored * GraphSaurus6File.BytesPerRow];
    data.Slice(GraphSaurus6File.BitmapOffset, pixels.Length).CopyTo(pixels);

    return new() { StoredHeight = stored, PixelData = pixels };
  }

  public static GraphSaurus6File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
