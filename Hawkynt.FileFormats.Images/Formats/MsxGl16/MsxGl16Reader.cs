using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.MsxGl16;

/// <summary>Reads sixteen-colour MSX2 GL pictures from bytes, streams, or file paths.</summary>
public static class MsxGl16Reader {

  public static MsxGl16File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GL16 picture not found.", file.FullName);

    // Only here is the extension available, and it is the only thing that names the screen.
    return FromSpan(File.ReadAllBytes(file.FullName), MsxGl16File.ModeFromExtension(file.Extension));
  }

  public static MsxGl16File FromStream(Stream stream) {
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

  /// <summary>Reads a picture, assuming Screen 5 — the reading that draws it as stored.</summary>
  public static MsxGl16File FromSpan(ReadOnlySpan<byte> data) => FromSpan(data, MsxGl16Mode.Screen5);

  public static MsxGl16File FromSpan(ReadOnlySpan<byte> data, MsxGl16Mode mode) {
    if (data.Length < MsxGl16File.HeaderSize + 1)
      throw new InvalidDataException($"A GL16 picture is at least {MsxGl16File.HeaderSize + 1} bytes, got {data.Length}.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
    if (width < 1 || height < 1 || width > MsxGl16File.MaxDimension || height > MsxGl16File.MaxDimension)
      throw new InvalidDataException($"Not a GL16 picture: the header claims {width}x{height}.");

    var size = MsxGl16File.PixelDataSizeFor(width, height);
    if (data.Length < MsxGl16File.HeaderSize + size)
      throw new InvalidDataException($"A {width}x{height} GL16 picture needs {MsxGl16File.HeaderSize + size} bytes, got {data.Length}.");

    var pixels = new byte[size];
    data.Slice(MsxGl16File.HeaderSize, size).CopyTo(pixels);

    return new() { Width = width, Height = height, Mode = mode, PixelData = pixels, Palette = [] };
  }

  public static MsxGl16File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
