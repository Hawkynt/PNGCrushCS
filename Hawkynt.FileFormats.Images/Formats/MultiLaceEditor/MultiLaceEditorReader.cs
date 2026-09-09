using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.MultiLaceEditor;

/// <summary>Reads Multi-Lace Editor (.mle) logos from bytes, streams, or file paths.</summary>
public static class MultiLaceEditorReader {

  public static MultiLaceEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Multi-Lace Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MultiLaceEditorFile FromStream(Stream stream) {
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

  public static MultiLaceEditorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MultiLaceEditorFile.FileSize)
      throw new InvalidDataException(
        $"A Multi-Lace Editor logo takes {MultiLaceEditorFile.FileSize} bytes; this file is {data.Length}.");

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      SecondField = data.Slice(MultiLaceEditorFile.SecondFieldOffset, MultiLaceEditorFile.FieldSize).ToArray(),
      FirstField = data.Slice(MultiLaceEditorFile.FirstFieldOffset, MultiLaceEditorFile.FieldSize).ToArray(),
    };
  }

  public static MultiLaceEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
