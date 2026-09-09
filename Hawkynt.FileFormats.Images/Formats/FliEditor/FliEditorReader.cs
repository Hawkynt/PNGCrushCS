using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FliEditor;

/// <summary>Reads FLI Editor (.fed) files from bytes, streams, or file paths.</summary>
public static class FliEditorReader {

  public static FliEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLI Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FliEditorFile FromStream(Stream stream) {
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

  public static FliEditorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < FliEditorFile.PictureSize)
      throw new InvalidDataException(
        $"A FLI Editor picture takes {FliEditorFile.FileSize} bytes; this file is {data.Length}.");

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      Backgrounds = data.Slice(FliEditorFile.BackgroundsOffset, FliEditorFile.FixedHeight).ToArray(),
      ColorRam = data.Slice(FliEditorFile.ColorRamOffset, Commodore64Fli.ColorRamSize).ToArray(),
      Matrices = data.Slice(FliEditorFile.MatricesOffset, Commodore64Fli.MatrixAreaSize).ToArray(),
      BitmapData = data.Slice(FliEditorFile.BitmapOffset, Commodore64Fli.BitmapSize).ToArray(),
    };
  }

  public static FliEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
