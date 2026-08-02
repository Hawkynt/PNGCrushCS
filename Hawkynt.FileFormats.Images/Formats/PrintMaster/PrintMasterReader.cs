using System;
using System.IO;

namespace FileFormat.PrintMaster;

/// <summary>Reads Print Master graphics from bytes, streams, or file paths.</summary>
public static class PrintMasterReader {

  public static PrintMasterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Print Master file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PrintMasterFile FromStream(Stream stream) {
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

  public static PrintMasterFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < PrintMasterFile.HeaderSize)
      throw new InvalidDataException($"Print Master data too small: expected at least {PrintMasterFile.HeaderSize} bytes, got {data.Length}.");

    var widthBytes = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);

    if (widthBytes <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid Print Master dimensions: widthBytes={widthBytes}, height={height}.");

    var width = widthBytes * 8;
    var pixelDataSize = widthBytes * height;

    // Nothing marks one of these, so the length is the whole of the check. Padding what the file did
    // not contain let anything named .pm through: a file beginning "VIEW" read those four letters as
    // a size and came back as 150192 by 22341, a third of a billion pixels from seven kilobytes.
    var available = data.Length - PrintMasterFile.HeaderSize;
    if (available < pixelDataSize)
      throw new InvalidDataException($"A Print Master picture of {width}x{height} needs {pixelDataSize} bytes of pixel data; this file has {available}.");

    var pixelData = data.Slice(PrintMasterFile.HeaderSize, pixelDataSize).ToArray();

    return new() { Width = width, Height = height, PixelData = pixelData };
    }

  public static PrintMasterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
