using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Heif.Tests;

[TestFixture]
public sealed class HeifItemAddressingTests {

  [TestCase(1, 11, 7)]
  [TestCase(2, 23, 13)]
  [Category("Unit")]
  public void ReadImageInfo_UsesThePrimaryItemsOwnPropertyAssociations(
    int primaryId,
    int expectedWidth,
    int expectedHeight
  ) {
    var bytes = _BuildTwoItemMetadata((ushort)primaryId);

    var info = HeifFile.ReadImageInfo(bytes);

    Assert.That(info, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(info!.Value.Width, Is.EqualTo(expectedWidth));
      Assert.That(info.Value.Height, Is.EqualTo(expectedHeight));
      Assert.That(info.Value.Compression, Is.EqualTo("HEVC"));
      Assert.That(info.Value.FrameCount, Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void MultiImageContract_ExposesEveryDecodedTopLevelImageWithPrimaryFirst() {
    var primary = new HeifImage {
      ItemId = 9,
      ItemType = "hvc1",
      IsPrimary = true,
      Width = 2,
      Height = 1,
      PixelData = [1, 2, 3, 4, 5, 6],
      RawImageData = [10],
    };
    var second = new HeifImage {
      ItemId = 3,
      ItemType = "hvc1",
      Width = 1,
      Height = 1,
      PixelData = [7, 8, 9],
      RawImageData = [11],
    };
    var file = new HeifFile {
      Width = primary.Width,
      Height = primary.Height,
      PixelData = primary.PixelData,
      RawImageData = primary.RawImageData,
      Brand = "heic",
      Images = [primary, second],
    };

    Assert.That(HeifFile.ImageCount(file), Is.EqualTo(2));
    Assert.Multiple(() => {
      Assert.That(HeifFile.ToRawImage(file, 0).PixelData, Is.EqualTo(primary.PixelData));
      Assert.That(HeifFile.ToRawImage(file, 1).PixelData, Is.EqualTo(second.PixelData));
      Assert.That(HeifFile.ToRawImage(file, 1).Width, Is.EqualTo(1));
      Assert.That(HeifFile.ToRawImage(file, 1).Height, Is.EqualTo(1));
    });
  }

  private static byte[] _BuildTwoItemMetadata(ushort primaryId) {
    var ftypBody = new byte[12];
    System.Text.Encoding.ASCII.GetBytes("heic", 0, 4, ftypBody, 0);
    System.Text.Encoding.ASCII.GetBytes("mif1", 0, 4, ftypBody, 8);

    var pitmBody = new byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(pitmBody, primaryId);

    var iinfBody = _Concat([
      new byte[] { 0, 2 },
      _Infe(1, "hvc1"),
      _Infe(2, "hvc1"),
    ]);

    var ipco = _Box("ipco", _Concat([
      _Ispe(11, 7),
      _Box("hvcC", [1]),
      _Ispe(23, 13),
      _Box("hvcC", [1]),
    ]));

    var ipmaData = new byte[4 + 2 * (2 + 1 + 2)];
    BinaryPrimitives.WriteUInt32BigEndian(ipmaData.AsSpan(0), 2);
    var at = 4;

    BinaryPrimitives.WriteUInt16BigEndian(ipmaData.AsSpan(at), 1);
    at += 2;
    ipmaData[at++] = 2;
    ipmaData[at++] = 0x80 | 1;
    ipmaData[at++] = 0x80 | 2;

    BinaryPrimitives.WriteUInt16BigEndian(ipmaData.AsSpan(at), 2);
    at += 2;
    ipmaData[at++] = 2;
    ipmaData[at++] = 0x80 | 3;
    ipmaData[at] = 0x80 | 4;

    var iprp = _Box("iprp", _Concat([ipco, _FullBox("ipma", ipmaData)]));
    var meta = _FullBox("meta", _Concat([
      _FullBox("pitm", pitmBody),
      _FullBox("iinf", iinfBody),
      iprp,
    ]));

    return _Concat([_Box("ftyp", ftypBody), meta]);
  }

  private static byte[] _Infe(ushort itemId, string itemType) {
    var body = new byte[2 + 2 + 4 + 1];
    BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(0), itemId);
    BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(2), 0);
    System.Text.Encoding.ASCII.GetBytes(itemType, 0, 4, body, 4);
    return _FullBox("infe", body, version: 2);
  }

  private static byte[] _Ispe(int width, int height) {
    var body = new byte[8];
    BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(0), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(4), (uint)height);
    return _FullBox("ispe", body);
  }

  private static byte[] _FullBox(string type, byte[] body, byte version = 0) {
    var full = new byte[4 + body.Length];
    full[0] = version;
    body.CopyTo(full.AsSpan(4));
    return _Box(type, full);
  }

  private static byte[] _Box(string type, byte[] body) {
    var result = new byte[8 + body.Length];
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0), (uint)result.Length);
    System.Text.Encoding.ASCII.GetBytes(type, 0, 4, result, 4);
    body.CopyTo(result.AsSpan(8));
    return result;
  }

  private static byte[] _Concat(IReadOnlyList<byte[]> parts) {
    var total = 0;
    foreach (var part in parts)
      total += part.Length;

    var result = new byte[total];
    var at = 0;
    foreach (var part in parts) {
      part.CopyTo(result.AsSpan(at));
      at += part.Length;
    }

    return result;
  }
}
