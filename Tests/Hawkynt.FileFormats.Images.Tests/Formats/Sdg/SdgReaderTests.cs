using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Sdg.Tests;

[TestFixture]
public sealed class SdgReaderTests {

  [Test]
  [Category("Unit")]
  public void FromSpan_Sga3Objects_ReadsPlainAndZCompressedBmpThumbnails() {
    var first = _Image(0x10);
    var second = _Image(0x50);
    var plain = _Object(BmpWriter.ToBytes(BmpFile.FromRawImage(first)));
    var compressed = _Object(_ZCompressedBmp(BmpWriter.ToBytes(BmpFile.FromRawImage(second))));
    var data = new byte[plain.Length + compressed.Length];
    plain.CopyTo(data, 0);
    compressed.CopyTo(data, plain.Length);

    var file = SdgReader.FromSpan(data);

    Assert.Multiple(() => {
      Assert.That(file.Images, Has.Count.EqualTo(2));
      Assert.That(file.Images[0].PixelData, Is.EqualTo(first.PixelData));
      Assert.That(file.Images[1].PixelData, Is.EqualTo(second.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_GarbageBetweenObjects_IsRefusedInsteadOfByteScanned() {
    var first = _Object(BmpWriter.ToBytes(BmpFile.FromRawImage(_Image(0x10))));
    var second = _Object(BmpWriter.ToBytes(BmpFile.FromRawImage(_Image(0x50))));
    var data = new byte[first.Length + 7 + second.Length];
    first.CopyTo(data, 0);
    second.CopyTo(data, first.Length + 7);

    Assert.That(() => SdgReader.FromSpan(data), Throws.TypeOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_TruncatedBitmapTrailer_IsRefused() {
    var data = _Object(BmpWriter.ToBytes(BmpFile.FromRawImage(_Image(0x20))));

    Assert.That(() => SdgReader.FromSpan(data[..^1]), Throws.TypeOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Sga3WithoutBitmapFlag_IsRefused() {
    var data = _Object(BmpWriter.ToBytes(BmpFile.FromRawImage(_Image(0x20))));
    data[10] = 0;

    Assert.That(() => SdgReader.FromSpan(data), Throws.TypeOf<InvalidDataException>());
  }

  private static byte[] _Object(byte[] bmp) {
    // SgaObjectBmp v5: common 11-byte header, BitmapEx, empty URL, ten legacy fields,
    // an empty compatibility string and an empty UTF-8 title.
    var result = new byte[11 + bmp.Length + 16];
    result[0] = (byte)'S'; result[1] = (byte)'G'; result[2] = (byte)'A'; result[3] = (byte)'3';
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 4);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), 5);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), 1);
    result[10] = 1;
    bmp.CopyTo(result, 11);
    return result;
  }

  private static byte[] _ZCompressedBmp(byte[] bmp) {
    const uint zCompress = 0x01004453;
    const int bodyAt = 54;
    var uncoded = bmp[bodyAt..];
    byte[] coded;
    using (var output = new MemoryStream()) {
      using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        zlib.Write(uncoded);
      coded = output.ToArray();
    }

    var result = new byte[bodyAt + 12 + coded.Length];
    bmp.AsSpan(0, bodyAt).CopyTo(result);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(2), checked((uint)result.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(30), zCompress);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(bodyAt), checked((uint)coded.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(bodyAt + 4), checked((uint)uncoded.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(bodyAt + 8), 0);
    coded.CopyTo(result, bodyAt + 12);
    return result;
  }

  private static RawImage _Image(byte seed) => new() {
    Width = 2,
    Height = 2,
    Format = PixelFormat.Bgr24,
    PixelData = [
      seed, (byte)(seed + 1), (byte)(seed + 2), (byte)(seed + 3), (byte)(seed + 4), (byte)(seed + 5),
      (byte)(seed + 6), (byte)(seed + 7), (byte)(seed + 8), (byte)(seed + 9), (byte)(seed + 10), (byte)(seed + 11),
    ],
  };
}
