using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ilbm;

namespace FileFormat.IffDctv;

/// <summary>Assembles IFF DCTV (Digital Composite Television) bytes from an <see cref="IffDctvFile"/>.</summary>
/// <remarks>
/// The carrier is a plain ILBM: BMHD, CMAP, CAMG, BODY, ByteRun1-compressed, which is what the DCTV
/// software itself wrote and what every genuine sample file examined here contains. The colour map is
/// the one copied in <see cref="IffDctvCodec.ColourMap"/> — writing any other palette would change
/// the analogue levels and hand a DCTV unit a different waveform.
/// </remarks>
public static class IffDctvWriter {

  private const int _BMHD_LENGTH = 20;

  public static byte[] ToBytes(IffDctvFile file) {
    IffDctvFile.Validate(file, nameof(file));

    var width = file.Width;
    var height = file.ContentHeight;
    var planes = IffDctvCodec.WrittenPlaneCount;
    var bytesPerPlane = (width + 15) >> 4 << 1;
    var body = ByteRun1Compressor.Encode(_ToPlanar(file.Samples, width, height, planes, bytesPerPlane));

    var colourMap = IffDctvCodec.ColourMap;
    using var ms = new MemoryStream();
    ms.Write("FORM"u8);
    _WriteUInt32(ms, 0); // patched once the payload length is known
    ms.Write("ILBM"u8);

    _WriteChunkHeader(ms, "BMHD"u8, _BMHD_LENGTH);
    Span<byte> bmhd = stackalloc byte[_BMHD_LENGTH];
    BinaryPrimitives.WriteUInt16BigEndian(bmhd, (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(bmhd[2..], (ushort)height);
    BinaryPrimitives.WriteInt16BigEndian(bmhd[4..], 0);
    BinaryPrimitives.WriteInt16BigEndian(bmhd[6..], 0);
    bmhd[8] = (byte)planes;
    bmhd[9] = 0; // masking: none
    bmhd[10] = 1; // compression: ByteRun1
    bmhd[11] = 0;
    BinaryPrimitives.WriteUInt16BigEndian(bmhd[12..], 0);
    bmhd[14] = 20; // pixel aspect, as the genuine interlaced samples carry it
    bmhd[15] = 11;
    BinaryPrimitives.WriteInt16BigEndian(bmhd[16..], (short)width);
    BinaryPrimitives.WriteInt16BigEndian(bmhd[18..], (short)height);
    ms.Write(bmhd);

    _WriteChunkHeader(ms, "CMAP"u8, colourMap.Length);
    ms.Write(colourMap);

    _WriteChunkHeader(ms, "CAMG"u8, 4);
    _WriteUInt32(ms, IffDctvCodec.WrittenViewportMode);

    _WriteChunkHeader(ms, "BODY"u8, body.Length);
    ms.Write(body);
    if ((body.Length & 1) != 0)
      ms.WriteByte(0);

    var result = ms.ToArray();
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), (uint)(result.Length - 8));
    return result;
  }

  public static void ToStream(IffDctvFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var bytes = ToBytes(file);
    stream.Write(bytes, 0, bytes.Length);
  }

  public static void ToFile(IffDctvFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }

  private static byte[] _ToPlanar(byte[] samples, int width, int height, int planes, int bytesPerPlane) {
    var bytesPerRow = bytesPerPlane * planes;
    var planar = new byte[bytesPerRow * height];
    for (var y = 0; y < height; ++y) {
      var row = y * bytesPerRow;
      for (var x = 0; x < width; ++x) {
        var nibble = samples[y * width + x];
        if (nibble == 0)
          continue;

        var mask = (byte)(0x80 >> (x & 7));
        for (var plane = 0; plane < planes; ++plane)
          if ((nibble >> plane & 1) != 0)
            planar[row + plane * bytesPerPlane + (x >> 3)] |= mask;
      }
    }

    return planar;
  }

  private static void _WriteChunkHeader(Stream stream, ReadOnlySpan<byte> id, int length) {
    stream.Write(id);
    _WriteUInt32(stream, (uint)length);
  }

  private static void _WriteUInt32(Stream stream, uint value) {
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
    stream.Write(buffer);
  }
}
