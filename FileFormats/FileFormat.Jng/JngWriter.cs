using System;
using System.IO;
using System.IO.Hashing;
using System.Net;
using System.Text;

namespace FileFormat.Jng;

/// <summary>Assembles JNG file bytes from a JngFile data model.</summary>
public static class JngWriter {

  private static readonly byte[] _Signature = [0x8B, 0x4A, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

  public static byte[] ToBytes(JngFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // Write signature
    ms.Write(_Signature);

    // Write JHDR chunk
    var jhdrData = new byte[JngHeader.StructSize];
    var hasAlpha = file.AlphaSampleDepth > 0;
    var jhdr = new JngHeader(
      file.Width,
      file.Height,
      file.ColorType,
      file.ImageSampleDepth,
      8, // ImageCompressionMethod: always JPEG
      0, // ImageInterlaceMethod: always 0
      file.AlphaSampleDepth,
      (byte)file.AlphaCompression,
      hasAlpha ? (byte)0 : (byte)0, // AlphaFilterMethod
      0 // AlphaInterlaceMethod
    );
    jhdr.WriteTo(jhdrData);
    _WriteChunk(ms, "JHDR", jhdrData);

    // Write JDAT chunk(s)
    if (file.JpegData.Length > 0)
      _WriteChunk(ms, "JDAT", file.JpegData);

    // Write alpha data chunk(s)
    if (file.AlphaData != null && file.AlphaData.Length > 0) {
      var alphaChunkType = file.AlphaCompression == JngAlphaCompression.Jpeg ? "JDAA" : "IDAT";
      _WriteChunk(ms, alphaChunkType, file.AlphaData);
    }

    // Write IEND chunk
    _WriteChunk(ms, "IEND", ReadOnlySpan<byte>.Empty);

    return ms.ToArray();
  }

  private static void _WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data) {
    using var bw = new BinaryWriter(stream, Encoding.ASCII, true);
    bw.Write(IPAddress.HostToNetworkOrder(data.Length));
    var typeBytes = Encoding.ASCII.GetBytes(type);
    stream.Write(typeBytes);
    stream.Write(data);

    var crc = new Crc32();
    crc.Append(typeBytes);
    crc.Append(data);
    bw.Write(IPAddress.HostToNetworkOrder((int)crc.GetCurrentHashAsUInt32()));
  }
}
