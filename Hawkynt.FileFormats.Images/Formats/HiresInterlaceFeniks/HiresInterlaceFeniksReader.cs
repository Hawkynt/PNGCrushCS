using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.HiresInterlaceFeniks;

/// <summary>Reads Hires Interlace (.hlf, .hie) files from bytes, streams, or file paths.</summary>
public static class HiresInterlaceFeniksReader {

  public static HiresInterlaceFeniksFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hires Interlace file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HiresInterlaceFeniksFile FromStream(Stream stream) {
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

  public static HiresInterlaceFeniksFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < HiresInterlaceFeniksFile.SecondBitmapOffset + HiresInterlaceFeniksFile.BitmapDataSize)
      throw new InvalidDataException(
        $"A Hires Interlace picture takes {HiresInterlaceFeniksFile.FileSize} bytes; this file is {data.Length}.");

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      FirstBitmap = data.Slice(HiresInterlaceFeniksFile.FirstBitmapOffset, HiresInterlaceFeniksFile.BitmapDataSize).ToArray(),
      SecondScreen = data.Slice(HiresInterlaceFeniksFile.SecondScreenOffset, HiresInterlaceFeniksFile.ScreenRamSize).ToArray(),
      FirstScreen = data.Slice(HiresInterlaceFeniksFile.FirstScreenOffset, HiresInterlaceFeniksFile.ScreenRamSize).ToArray(),
      SecondBitmap = data.Slice(HiresInterlaceFeniksFile.SecondBitmapOffset, HiresInterlaceFeniksFile.BitmapDataSize).ToArray(),
    };
  }

  public static HiresInterlaceFeniksFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
