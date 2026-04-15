using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Net;
using System.Text;
using FileFormat.Png;

namespace FileFormat.Apng;

/// <summary>Writes APNG (Animated PNG) files.</summary>
public static class ApngWriter {

  private static readonly byte[] _PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

  /// <summary>Write an ApngFile to bytes.</summary>
  public static byte[] ToBytes(ApngFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // PNG signature
    ms.Write(_PngSignature);

    // IHDR
    var ihdrData = new byte[PngIhdr.StructSize];
    var ihdr = new PngIhdr(file.Width, file.Height, (byte)file.BitDepth, (byte)file.ColorType, 0, 0, 0);
    ihdr.WriteTo(ihdrData);
    _WriteChunk(ms, "IHDR", ihdrData);

    // acTL
    var actlData = new byte[ApngActl.StructSize];
    var actl = new ApngActl(file.Frames.Count, file.NumPlays);
    actl.WriteTo(actlData);
    _WriteChunk(ms, "acTL", actlData);

    // PLTE (if palette)
    if (file.ColorType == PngColorType.Palette && file.Palette != null) {
      _WriteChunk(ms, "PLTE", file.Palette);
      if (file.Transparency != null)
        _WriteChunk(ms, "tRNS", file.Transparency);
    }

    // tRNS for non-palette
    if (file.ColorType != PngColorType.Palette && file.Transparency != null)
      _WriteChunk(ms, "tRNS", file.Transparency);

    var sequenceNumber = 0;

    for (var frameIndex = 0; frameIndex < file.Frames.Count; ++frameIndex) {
      var frame = file.Frames[frameIndex];

      // fcTL
      var fctlData = new byte[ApngFctl.StructSize];
      var fctl = new ApngFctl(
        sequenceNumber++,
        frame.Width,
        frame.Height,
        frame.XOffset,
        frame.YOffset,
        frame.DelayNumerator,
        frame.DelayDenominator,
        (byte)frame.DisposeOp,
        (byte)frame.BlendOp
      );
      fctl.WriteTo(fctlData);
      _WriteChunk(ms, "fcTL", fctlData);

      // Compress frame pixel data
      var compressedData = _CompressFrameData(frame, file.BitDepth, file.ColorType);

      if (frameIndex == 0) {
        // Frame 0 uses IDAT
        _WriteChunk(ms, "IDAT", compressedData);
      } else {
        // Subsequent frames use fdAT with sequence number prefix
        var fdatData = new byte[4 + compressedData.Length];
        BinaryPrimitives.WriteInt32BigEndian(fdatData, sequenceNumber++);
        Buffer.BlockCopy(compressedData, 0, fdatData, 4, compressedData.Length);
        _WriteChunk(ms, "fdAT", fdatData);
      }
    }

    // IEND
    _WriteChunk(ms, "IEND", []);

    return ms.ToArray();
  }

  private static byte[] _CompressFrameData(ApngFrame frame, int bitDepth, PngColorType colorType) {
    using var compressedStream = new MemoryStream();
    using (var zlibStream = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, true)) {
      foreach (var scanline in frame.PixelData) {
        zlibStream.WriteByte(0); // None filter
        zlibStream.Write(scanline);
      }
    }

    return compressedStream.ToArray();
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
