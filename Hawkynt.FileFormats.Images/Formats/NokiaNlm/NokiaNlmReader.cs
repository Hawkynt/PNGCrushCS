using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.NokiaNlm;

/// <summary>Reads Nokia Logo Manager files from bytes, streams, or file paths.</summary>
public static class NokiaNlmReader {

  public static NokiaNlmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Logo not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NokiaNlmFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static NokiaNlmFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < NokiaNlmFile.HeaderSize
        || Encoding.ASCII.GetString(data[..NokiaNlmFile.Signature.Length]) != NokiaNlmFile.Signature)
      throw new InvalidDataException("Not a Nokia Logo Manager file.");

    int width = data[NokiaNlmFile.WidthOffset], height = data[NokiaNlmFile.HeightOffset];
    if (width == 0 || height == 0)
      throw new InvalidDataException($"A Nokia logo states no size: {width}x{height}.");

    var stride = (width + 7) / 8;
    if (data.Length < NokiaNlmFile.HeaderSize + stride * height)
      throw new InvalidDataException(
        $"{width}x{height} needs {NokiaNlmFile.HeaderSize + stride * height} bytes; this file is {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      PixelData = BilevelRows.Unpack(data[NokiaNlmFile.HeaderSize..], width, height),
    };
  }

  public static NokiaNlmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
