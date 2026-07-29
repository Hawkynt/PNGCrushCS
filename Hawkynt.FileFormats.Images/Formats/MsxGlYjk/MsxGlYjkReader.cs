using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.MsxGlYjk;

/// <summary>Reads MSX2+ GL/SH YJK pictures from bytes, streams, or file paths.</summary>
public static class MsxGlYjkReader {

  public static MsxGlYjkFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GL/SH picture not found.", file.FullName);

    // Only here is the extension available, and it is the only thing that says which reading applies.
    return FromSpan(File.ReadAllBytes(file.FullName), MsxGlYjkFile.ModeFromExtension(file.Extension));
  }

  public static MsxGlYjkFile FromStream(Stream stream) {
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

  /// <summary>Reads a picture, assuming the Screen 12 reading.</summary>
  /// <remarks>
  /// Without a file name there is no extension to go on. Screen 12 is the safe assumption: the
  /// Screen 10 reading needs a palette that is never in this file, so guessing it would turn a
  /// sixteenth of the pixels black.
  /// </remarks>
  public static MsxGlYjkFile FromSpan(ReadOnlySpan<byte> data) => FromSpan(data, MsxGlYjkMode.Screen12);

  public static MsxGlYjkFile FromSpan(ReadOnlySpan<byte> data, MsxGlYjkMode mode) {
    if (data.Length < MsxGlYjkFile.HeaderSize)
      throw new InvalidDataException($"A GL/SH picture is at least {MsxGlYjkFile.HeaderSize} bytes, got {data.Length}.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
    if (width < 1 || height < 1 || width > MsxGlYjkFile.MaxDimension || height > MsxGlYjkFile.MaxDimension)
      throw new InvalidDataException($"Not a GL/SH picture: the header claims {width}x{height}.");

    // The file holds exactly one byte per pixel and nothing else, which is what tells this format
    // apart from everything else carrying these extensions.
    var expected = MsxGlYjkFile.HeaderSize + width * height;
    if (data.Length != expected)
      throw new InvalidDataException($"A {width}x{height} GL/SH picture is {expected} bytes, got {data.Length}.");

    var pixels = new byte[width * height];
    data.Slice(MsxGlYjkFile.HeaderSize, pixels.Length).CopyTo(pixels);

    return new() { Width = width, Height = height, Mode = mode, PixelData = pixels, Palette = [] };
  }

  public static MsxGlYjkFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
