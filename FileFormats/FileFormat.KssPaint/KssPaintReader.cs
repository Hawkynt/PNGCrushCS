using System;
using System.IO;

namespace FileFormat.KssPaint;

/// <summary>Reads KSS-Paint (.kss) files from bytes, streams, or file paths.</summary>
public static class KssPaintReader {

  public static KssPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("KSS-Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static KssPaintFile FromStream(Stream stream) {
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

  public static KssPaintFile FromSpan(ReadOnlySpan<byte> data) {
    // The format carries no signature; its fixed size is the only thing identifying it.
    if (data.Length != KssPaintFile.FileSize)
      throw new InvalidDataException(
        $"A KSS-Paint file is exactly {KssPaintFile.FileSize} bytes, got {data.Length}.");

    var bitmap = new byte[KssPaintFile.BitmapDataSize];
    data[..KssPaintFile.BitmapDataSize].CopyTo(bitmap);

    var colors = new byte[KssPaintFile.ColorCount];
    data.Slice(KssPaintFile.ColorOffset, colors.Length).CopyTo(colors);

    return new() {
      BitmapData = bitmap,
      Colors = colors,
    };
  }

  public static KssPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
