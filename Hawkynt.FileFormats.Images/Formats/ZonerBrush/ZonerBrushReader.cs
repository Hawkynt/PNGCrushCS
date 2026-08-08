using System;
using System.IO;

namespace FileFormat.ZonerBrush;

/// <summary>Reads the preview out of a Zoner brush from bytes, streams, or file paths.</summary>
public static class ZonerBrushReader {

  public static ZonerBrushFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Zoner brush not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZonerBrushFile FromStream(Stream stream) {
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

  public static ZonerBrushFile FromSpan(ReadOnlySpan<byte> data) {
    // The drawing behind the preview is whatever length it is, so this asks only that the preview
    // is all there rather than that the file is a particular size.
    if (data.Length < ZonerBrushFile.MinimumFileSize)
      throw new InvalidDataException(
        $"A Zoner brush carries a {ZonerBrushFile.Width}x{ZonerBrushFile.Height} preview needing {ZonerBrushFile.MinimumFileSize} bytes; got {data.Length}.");

    return new() {
      Header = data[..ZonerBrushFile.PaletteOffset].ToArray(),
      Palette = data.Slice(ZonerBrushFile.PaletteOffset, ZonerBrushFile.PaletteCount * ZonerBrushFile.PaletteEntrySize).ToArray(),
      PixelData = data.Slice(ZonerBrushFile.PixelOffset, ZonerBrushFile.BytesPerRow * ZonerBrushFile.Height).ToArray(),
    };
  }

  public static ZonerBrushFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
