using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FliDesigner;

/// <summary>Reads FLI Designer (.fd2) files from bytes, streams, or file paths.</summary>
/// <remarks>
/// Two lengths, and the second is the first saved to the end of the sixteen-kilobyte bank it lives
/// in rather than stopping at the last byte of the picture. Nothing between them is another format,
/// so anything shorter than a whole picture is refused outright.
/// </remarks>
public static class FliDesignerReader {

  public static FliDesignerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLI Designer file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FliDesignerFile FromStream(Stream stream) {
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

  public static FliDesignerFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < FliDesignerFile.FileSize)
      throw new InvalidDataException(
        $"A FLI Designer picture takes {FliDesignerFile.FileSize} bytes, or {FliDesignerFile.PaddedFileSize} "
        + $"saved to the end of its bank; this file is {data.Length}.");

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      ColorRam = data.Slice(FliDesignerFile.ColorRamOffset, Commodore64Fli.ColorRamSize).ToArray(),
      Matrices = data.Slice(FliDesignerFile.MatricesOffset, Commodore64Fli.MatrixAreaSize).ToArray(),
      BitmapData = data.Slice(FliDesignerFile.BitmapOffset, Commodore64Fli.BitmapSize).ToArray(),
      Padded = data.Length >= FliDesignerFile.PaddedFileSize,
    };
  }

  public static FliDesignerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
