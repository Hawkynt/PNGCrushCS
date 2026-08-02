using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.AwardBmp;

/// <summary>Writes Award BIOS bitmap logos (AWBM).</summary>
public static class AwardBmpWriter {

  public static byte[] ToBytes(AwardBmpFile file) {
    var width = file.Width;
    var height = file.Height;
    var stride = AwardBmpFile.StrideOf(width);
    var data = new byte[AwardBmpFile.SizeOf(width, height)];

    AwardBmpFile.Signature.CopyTo(data);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), (ushort)height);

    for (var y = 0; y < height; ++y) {
      var row = 8 + y * stride * AwardBmpFile.Planes;
      for (var x = 0; x < width; ++x) {
        var index = file.PixelData[y * width + x] & 15;
        for (var plane = 0; plane < AwardBmpFile.Planes; ++plane)
          if ((index >> plane & 1) != 0)
            data[row + plane * stride + (x >> 3)] |= (byte)(1 << (~x & 7));
      }
    }

    var at = 8 + stride * AwardBmpFile.Planes * height;
    AwardBmpFile.PaletteMarker.CopyTo(data.AsSpan(at));
    at += AwardBmpFile.PaletteMarker.Length;
    for (var i = 0; i < AwardBmpFile.PaletteCount * 3; ++i)
      data[at + i] = (byte)(file.Palette[i] >> 2);

    return data;
  }

  public static void ToStream(AwardBmpFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var bytes = ToBytes(file);
    stream.Write(bytes, 0, bytes.Length);
  }

  public static void ToFile(AwardBmpFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
