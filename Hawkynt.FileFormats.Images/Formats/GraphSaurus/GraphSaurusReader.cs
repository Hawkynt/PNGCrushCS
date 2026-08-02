using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.GraphSaurus;

/// <summary>Reads Graph Saurus screen dumps from bytes, streams, or file paths.</summary>
public static class GraphSaurusReader {

  public static GraphSaurusFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Graph Saurus file not found.", file.FullName);

    // Screen 12 is the same length as Screen 8, so only the name says which this is.
    var parsed = FromBytes(File.ReadAllBytes(file.FullName)) with {
      IsYjk = string.Equals(file.Extension, ".srs", StringComparison.OrdinalIgnoreCase),
    };

    // The palette sits in its own file. Without it a Screen 5 picture still decodes, in the sixteen
    // colours the chip starts with — which is what the machine itself would show.
    var companion = new FileInfo(Path.ChangeExtension(file.FullName, GraphSaurusFile.CompanionExtension));
    if (parsed.IsScreen8 || !companion.Exists)
      return parsed;

    var palette = File.ReadAllBytes(companion.FullName);
    return palette.Length < GraphSaurusFile.PaletteColors * MsxGraphics.PaletteEntrySize
      ? parsed
      : parsed with { Palette = palette };
  }

  public static GraphSaurusFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static GraphSaurusFile FromSpan(ReadOnlySpan<byte> data) {
    var isScreen8 = GraphSaurusFile.ScreenEightAt(data.Length);

    if (MsxGraphics.ReadBsaveEndAddress(data) < 0)
      throw new InvalidDataException("Not a Graph Saurus file: no BSAVE header.");

    return new() {
      IsScreen8 = isScreen8,
      PixelData = data[GraphSaurusFile.HeaderSize..].ToArray(),
    };
  }

  public static GraphSaurusFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
