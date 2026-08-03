using System;
using System.IO;

namespace FileFormat.Gigacad;

/// <summary>Reads Atari ST GigaCAD monochrome images from bytes, streams, or file paths.</summary>
public static class GigacadReader {

  public static GigacadFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GigaCAD file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GigacadFile FromStream(Stream stream) {
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

  public static GigacadFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length == GigacadFile.CommodoreFileSize)
      return new GigacadFile {
        Width = 320,
        Height = 200,
        SetBitIsPaper = true,
        PixelData = GigacadFile.CellsToRows(data.Slice(2, GigacadFile.CommodoreScreenSize), 320, 200),
      };

    if (data.Length != GigacadFile.ExpectedFileSize)
      throw new InvalidDataException($"A GigaCAD picture is {GigacadFile.CommodoreFileSize} bytes on a Commodore or {GigacadFile.ExpectedFileSize} on an Atari; this file is {data.Length}.");

    var pixelData = new byte[GigacadFile.ExpectedFileSize];
    data.Slice(0, GigacadFile.ExpectedFileSize).CopyTo(pixelData);

    return new GigacadFile {
      Width = 640,
      Height = 400,
      PixelData = pixelData
    };
    }

  public static GigacadFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
