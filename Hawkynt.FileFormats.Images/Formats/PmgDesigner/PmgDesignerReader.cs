using System;
using System.IO;

namespace FileFormat.PmgDesigner;

/// <summary>Reads PMG Designer sheets from bytes, streams, or file paths.</summary>
public static class PmgDesignerReader {

  public static PmgDesignerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sheet not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PmgDesignerFile FromStream(Stream stream) {
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

  public static PmgDesignerFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 12 || !data[..PmgDesignerFile.Signature.Length].SequenceEqual(PmgDesignerFile.Signature))
      throw new InvalidDataException("Not a PMG Designer sheet.");

    int sprites = data[7], shapes = data[8] * data[9], height = data[10];
    var total = sprites * shapes;

    if (sprites == 0 || sprites > 4 || shapes == 0 || shapes > 160 || height == 0 || height > 48
        || PmgDesignerFile.ShapesOffset + total * height != data.Length)
      throw new InvalidDataException($"Not a sheet: {sprites} sprites of {shapes} shapes in {data.Length} bytes.");

    // Sprites are drawn in pairs, so the sheet shows half as many cells as there are shapes.
    var cells = total >> 1;
    var rows = (cells + PmgDesignerFile.CellsPerRow - 1) / PmgDesignerFile.CellsPerRow;
    if (rows > 1 && rows * (height + PmgDesignerFile.RowGap) - PmgDesignerFile.RowGap > 560)
      throw new InvalidDataException("A PMG Designer sheet is at most 560 rows deep.");

    return new() { Data = data.ToArray(), Shapes = shapes, Cells = cells, Height = height };
  }

  public static PmgDesignerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
