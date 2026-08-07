using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.GraphSaurus7;

/// <summary>Reads Graph Saurus Screen 7 pictures from bytes, streams, or file paths.</summary>
public static class GraphSaurus7Reader {

  public static GraphSaurus7File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Graph Saurus Screen 7 picture not found.", file.FullName);

    var parsed = FromBytes(File.ReadAllBytes(file.FullName));

    // The palette sits in its own file. Without it the picture still decodes, in the sixteen colours
    // the chip starts with — which is what the machine itself would show.
    var companion = new FileInfo(Path.ChangeExtension(file.FullName, GraphSaurus7File.CompanionExtension));
    if (!companion.Exists)
      return parsed;

    // Sixteen two-byte entries and nothing else: the reference decoder reads the companion from its
    // first byte, so one carrying a BSAVE header of its own comes out shifted by seven and wrong.
    var palette = File.ReadAllBytes(companion.FullName);
    return palette.Length < GraphSaurus7File.ColorCount * MsxGraphics.PaletteEntrySize
      ? parsed
      : parsed with { Palette = palette };
  }

  public static GraphSaurus7File FromStream(Stream stream) {
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

  public static GraphSaurus7File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < GraphSaurus7File.MinimumFileSize || data[0] != MsxGraphics.BsaveMagic)
      throw new InvalidDataException(
        $"Not a Graph Saurus Screen 7 picture: it takes {GraphSaurus7File.MinimumFileSize} bytes behind a BSAVE marker, got {data.Length}.");

    // Fixed height, unlike Screen 6: the end address is not consulted because a short one is not a
    // part-height picture here — the reference decoder turns such a file down rather than reading it.
    if (MsxGraphics.ReadBsaveEndAddress(data) < 0)
      throw new InvalidDataException("Not a Graph Saurus Screen 7 picture: no BSAVE header.");

    var pixels = new byte[GraphSaurus7File.BitmapSize];
    data.Slice(GraphSaurus7File.BitmapOffset, pixels.Length).CopyTo(pixels);

    return new() { PixelData = pixels };
  }

  public static GraphSaurus7File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
