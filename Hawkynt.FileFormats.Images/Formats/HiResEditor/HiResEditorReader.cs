using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.HiResEditor;

/// <summary>Reads Hires-Editor and Run Paint pictures from bytes, streams, or file paths.</summary>
public static class HiResEditorReader {

  public static HiResEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hires-Editor picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HiResEditorFile FromStream(Stream stream) {
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

  public static HiResEditorFile FromSpan(ReadOnlySpan<byte> data) {
    // Some files carry sprite or colour data past the bitmap, which readers ignore.
    if (data.Length < HiResEditorFile.ExpectedFileSize)
      throw new InvalidDataException(
        $"A Hires-Editor picture is at least {HiResEditorFile.ExpectedFileSize} bytes, got {data.Length}.");

    var screen = new byte[HiResEditorFile.ScreenDataSize];
    data.Slice(HiResEditorFile.ScreenDataOffset, HiResEditorFile.ScreenDataSize).CopyTo(screen);

    var bitmap = new byte[HiResEditorFile.BitmapDataSize];
    data.Slice(HiResEditorFile.BitmapDataOffset, HiResEditorFile.BitmapDataSize).CopyTo(bitmap);

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      BitmapData = bitmap,
      ScreenData = screen,
    };
  }

  public static HiResEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
