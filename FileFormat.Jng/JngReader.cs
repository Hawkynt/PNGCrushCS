using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Jng;

/// <summary>Reads JNG files from bytes, streams, or file paths.</summary>
public static class JngReader {

  private static readonly byte[] _Signature = [0x8B, 0x4A, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

  public static JngFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JNG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JngFile FromStream(Stream stream) {
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

  public static JngFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static JngFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 8)
      throw new InvalidDataException("Data too small for a valid JNG file.");

    for (var i = 0; i < 8; ++i)
      if (data[i] != _Signature[i])
        throw new InvalidDataException("Invalid JNG signature.");

    var offset = 8;
    var header = default(JngHeader);
    var hasHeader = false;
    var jdatChunks = new List<byte[]>();
    var alphaChunks = new List<byte[]>();

    while (offset + 8 <= data.Length) {
      var chunkLength = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset));
      var chunkType = Encoding.ASCII.GetString(data, offset + 4, 4);
      offset += 8;

      if (chunkLength < 0 || offset + chunkLength + 4 > data.Length)
        break;

      var chunkData = data.AsSpan(offset, chunkLength);

      switch (chunkType) {
        case "JHDR":
          if (chunkLength >= JngHeader.StructSize) {
            header = JngHeader.ReadFrom(chunkData);
            hasHeader = true;
          }
          break;
        case "JDAT":
          jdatChunks.Add(chunkData.ToArray());
          break;
        case "JDAA":
        case "IDAT":
          alphaChunks.Add(chunkData.ToArray());
          break;
        case "IEND":
          offset += chunkLength + 4;
          goto done;
      }

      offset += chunkLength;
      offset += 4; // CRC
    }

    done:

    if (!hasHeader)
      throw new InvalidDataException("JNG file missing JHDR chunk.");

    var jpegData = _Concatenate(jdatChunks);
    var alphaData = alphaChunks.Count > 0 ? _Concatenate(alphaChunks) : null;

    return new JngFile {
      Width = header.Width,
      Height = header.Height,
      ColorType = header.ColorType,
      ImageSampleDepth = header.ImageSampleDepth,
      AlphaSampleDepth = header.AlphaSampleDepth,
      AlphaCompression = (JngAlphaCompression)header.AlphaCompressionMethod,
      JpegData = jpegData,
      AlphaData = alphaData
    };
  }

  private static byte[] _Concatenate(List<byte[]> chunks) {
    var totalLength = 0;
    foreach (var chunk in chunks)
      totalLength += chunk.Length;

    var result = new byte[totalLength];
    var offset = 0;
    foreach (var chunk in chunks) {
      Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
      offset += chunk.Length;
    }

    return result;
  }
}
