using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.SonyPmp;

/// <summary>Reads Sony DSC-F1 pictures from bytes, streams, or file paths.</summary>
public static class SonyPmpReader {

  public static SonyPmpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sony DSC-F1 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SonyPmpFile FromStream(Stream stream) {
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

  public static SonyPmpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static SonyPmpFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < SonyPmpFile.HeaderSize + SonyPmpFile.JpegStart.Length)
      throw new InvalidDataException(
        $"Data too small for a Sony DSC-F1 picture (minimum {SonyPmpFile.HeaderSize + SonyPmpFile.JpegStart.Length} bytes, got {data.Length}).");

    var stated = BinaryPrimitives.ReadUInt32BigEndian(data[SonyPmpFile.HeaderSizeOffset..]);
    if (stated != SonyPmpFile.HeaderSize)
      throw new InvalidDataException(
        $"A Sony DSC-F1 picture states a header of {stated} bytes; the camera's is {SonyPmpFile.HeaderSize}.");

    if (!data.Slice(SonyPmpFile.HeaderSize, SonyPmpFile.JpegStart.Length).SequenceEqual(SonyPmpFile.JpegStart))
      throw new InvalidDataException("A Sony DSC-F1 picture carries no JPEG behind its header.");

    var length = BinaryPrimitives.ReadUInt32BigEndian(data[SonyPmpFile.JpegLengthOffset..]);
    var available = data.Length - SonyPmpFile.HeaderSize;
    if (length < 1 || length > available)
      throw new InvalidDataException(
        $"A Sony DSC-F1 picture states a JPEG of {length} bytes where {available} stand behind its header.");

    var decoded = PixelConverter.Convert(
      JpegFile.ToRawImage(JpegReader.FromBytes(data.Slice(SonyPmpFile.HeaderSize, (int)length).ToArray())),
      PixelFormat.Rgb24);

    return new() { Width = decoded.Width, Height = decoded.Height, PixelData = decoded.PixelData };
  }
}
