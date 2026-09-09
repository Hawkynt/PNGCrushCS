using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.EmcEditor;

/// <summary>Reads EMC-editor (.emc) files from bytes, streams, or file paths.</summary>
public static class EmcEditorReader {

  public static EmcEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("EMC-editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EmcEditorFile FromStream(Stream stream) {
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

  public static EmcEditorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < EmcEditorFile.FileSize)
      throw new InvalidDataException(
        $"An EMC-editor picture takes {EmcEditorFile.FileSize} bytes; this file is {data.Length}.");

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      Matrices = data.Slice(EmcEditorFile.MatricesOffset, Commodore64Fli.MatrixAreaSize).ToArray(),
      BitmapData = data.Slice(EmcEditorFile.BitmapOffset, Commodore64Fli.BitmapSize).ToArray(),
      ColorRam = data.Slice(EmcEditorFile.ColorRamOffset, Commodore64Fli.ColorRamSize).ToArray(),
      Background = data[EmcEditorFile.BackgroundOffset],
    };
  }

  public static EmcEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
