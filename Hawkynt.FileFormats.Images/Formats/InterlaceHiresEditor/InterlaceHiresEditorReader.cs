using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.InterlaceHiresEditor;

/// <summary>Reads Interlace Hires Editor pictures from bytes, streams, or file paths.</summary>
public static class InterlaceHiresEditorReader {

  public static InterlaceHiresEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Interlace Hires Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static InterlaceHiresEditorFile FromStream(Stream stream) {
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

  public static InterlaceHiresEditorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < InterlaceHiresEditorFile.ExpectedFileSize)
      throw new InvalidDataException(
        $"An Interlace Hires Editor picture is {InterlaceHiresEditorFile.ExpectedFileSize} bytes; this file is {data.Length}.");

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      FirstBitmap = data.Slice(InterlaceHiresEditorFile.FirstBitmapOffset, InterlaceHiresEditorFile.BitmapSize).ToArray(),
      SecondBitmap = data.Slice(InterlaceHiresEditorFile.SecondBitmapOffset, InterlaceHiresEditorFile.BitmapSize).ToArray(),
    };
  }

  public static InterlaceHiresEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
