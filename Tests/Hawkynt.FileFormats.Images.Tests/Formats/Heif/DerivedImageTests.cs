using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H265;
using FileFormat.Core;

namespace FileFormat.Heif.Tests;

/// <summary>HEIF derived-image graph tests independent of the writer's single-item box layout.</summary>
[TestFixture]
public sealed class DerivedImageTests {

  [Test]
  [Category("Unit")]
  public void GridPrimary_ComposesReferencedTilesAndHidesComponentsFromThePageList() {
    var tiles = new[] {
      _EncodeFlatTile(210, 30, 30),
      _EncodeFlatTile(30, 210, 30),
      _EncodeFlatTile(30, 30, 210),
      _EncodeFlatTile(220, 210, 30),
    };
    var bytes = _BuildGrid(tiles, rows: 2, columns: 2, outputWidth: 3, outputHeight: 3);

    var file = HeifReader.FromBytes(bytes);
    var expected = _Compose(tiles, rows: 2, columns: 2, outputWidth: 3, outputHeight: 3);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(3));
      Assert.That(file.Height, Is.EqualTo(3));
      Assert.That(file.PixelData, Is.EqualTo(expected));
      Assert.That(file.Images, Has.Count.EqualTo(1), "dimg inputs are components of the grid, not independent pages");
      Assert.That(file.Images[0].ItemType, Is.EqualTo("grid"));
      Assert.That(file.Images[0].IsPrimary, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void GridPrimary_AppliesRotationBeforeMirrorToTheReconstructedImage() {
    var top = _EncodeFlatTile(220, 40, 40);
    var bottom = _EncodeFlatTile(40, 40, 220);
    var bytes = _BuildGrid(
      [top, bottom],
      rows: 2,
      columns: 1,
      outputWidth: 2,
      outputHeight: 4,
      rotation: 1,
      mirrorAxis: 1);

    var file = HeifReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(4));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(_Pixel(file.PixelData, file.Width, 0, 0), Is.EqualTo(_Pixel(bottom.Pixels, 2, 0, 0)),
        "90-degree CCW rotation followed by a left-right mirror puts the former bottom tile on the left");
      Assert.That(_Pixel(file.PixelData, file.Width, 3, 0), Is.EqualTo(_Pixel(top.Pixels, 2, 0, 0)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadImageInfo_GridUsesDerivedExtentTransformsAndUnderlyingCodec() {
    var tile = _EncodeFlatTile(80, 120, 180);
    var bytes = _BuildGrid(
      [tile, tile, tile, tile],
      rows: 2,
      columns: 2,
      outputWidth: 3,
      outputHeight: 4,
      rotation: 1);

    var info = HeifFile.ReadImageInfo(bytes);

    Assert.That(info, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(info!.Value.Width, Is.EqualTo(4));
      Assert.That(info.Value.Height, Is.EqualTo(3));
      Assert.That(info.Value.Compression, Is.EqualTo("HEVC"));
      Assert.That(info.Value.FrameCount, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void IdentityDerivation_CycleIsRejectedRatherThanRecursed() {
    var bytes = _BuildIdentityCycle();

    var error = Assert.Throws<InvalidDataException>(() => HeifReader.FromBytes(bytes));

    Assert.That(error!.Message, Does.Contain("cycle"));
  }

  [Test]
  [Category("Unit")]
  public void GridPrimary_WrongNumberOfDimgInputsIsRejected() {
    var tile = _EncodeFlatTile(90, 130, 170);
    var bytes = _BuildGrid([tile], rows: 1, columns: 2, outputWidth: 3, outputHeight: 2);

    var error = Assert.Throws<InvalidDataException>(() => HeifReader.FromBytes(bytes));

    Assert.That(error!.Message, Does.Contain("dimg inputs"));
  }

  private static CodedTile _EncodeFlatTile(byte r, byte g, byte b) {
    var pixels = new byte[2 * 2 * 3];
    for (var at = 0; at < pixels.Length; at += 3) {
      pixels[at] = r;
      pixels[at + 1] = g;
      pixels[at + 2] = b;
    }

    var encoded = H265PcmStillCodec.Encode(new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    });
    var decoded = HeifHevcDecoder.Decode(encoded.Sample, encoded.DecoderConfiguration, null);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(2));
      Assert.That(decoded.Height, Is.EqualTo(2));
      Assert.That(decoded.PixelData, Has.Length.EqualTo(12));
    });

    return new(encoded.DecoderConfiguration, encoded.Sample, decoded.PixelData);
  }

  private static byte[] _BuildGrid(
    IReadOnlyList<CodedTile> tiles,
    int rows,
    int columns,
    int outputWidth,
    int outputHeight,
    byte? rotation = null,
    byte? mirrorAxis = null
  ) {
    if (rows is < 1 or > 256)
      throw new ArgumentOutOfRangeException(nameof(rows));
    if (columns is < 1 or > 256)
      throw new ArgumentOutOfRangeException(nameof(columns));

    var gridDescriptor = new byte[8];
    gridDescriptor[0] = 0;
    gridDescriptor[1] = 0;
    gridDescriptor[2] = checked((byte)(rows - 1));
    gridDescriptor[3] = checked((byte)(columns - 1));
    BinaryPrimitives.WriteUInt16BigEndian(gridDescriptor.AsSpan(4), checked((ushort)outputWidth));
    BinaryPrimitives.WriteUInt16BigEndian(gridDescriptor.AsSpan(6), checked((ushort)outputHeight));

    var itemPayloads = new List<byte[]> { gridDescriptor };
    foreach (var tile in tiles)
      itemPayloads.Add(tile.Sample);

    var ftypBody = new byte[12];
    System.Text.Encoding.ASCII.GetBytes("heic", 0, 4, ftypBody, 0);
    System.Text.Encoding.ASCII.GetBytes("mif1", 0, 4, ftypBody, 8);

    var pitmBody = new byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(pitmBody, 1);

    var iinfParts = new List<byte[]> { _U16(checked((ushort)(tiles.Count + 1))), _Infe(1, "grid") };
    for (var i = 0; i < tiles.Count; ++i)
      iinfParts.Add(_Infe(checked((ushort)(i + 2)), "hvc1"));

    var properties = new List<byte[]> { _Ispe(outputWidth, outputHeight) };
    var gridAssociations = new List<int> { 1 };
    if (rotation != null) {
      properties.Add(_Box("irot", [rotation.Value]));
      gridAssociations.Add(properties.Count);
    }
    if (mirrorAxis != null) {
      properties.Add(_Box("imir", [mirrorAxis.Value]));
      gridAssociations.Add(properties.Count);
    }

    var tileAssociations = new List<(int Configuration, int Extent)>();
    foreach (var tile in tiles) {
      properties.Add(_Box("hvcC", tile.Configuration));
      var configurationIndex = properties.Count;
      properties.Add(_Ispe(2, 2));
      tileAssociations.Add((configurationIndex, properties.Count));
    }

    var ipco = _Box("ipco", _Concat(properties));
    var ipma = _BuildIpma(gridAssociations, tileAssociations);
    var iprp = _Box("iprp", _Concat([ipco, ipma]));

    var dimgBody = new byte[4 + tiles.Count * 2];
    BinaryPrimitives.WriteUInt16BigEndian(dimgBody.AsSpan(0), 1);
    BinaryPrimitives.WriteUInt16BigEndian(dimgBody.AsSpan(2), checked((ushort)tiles.Count));
    for (var i = 0; i < tiles.Count; ++i)
      BinaryPrimitives.WriteUInt16BigEndian(dimgBody.AsSpan(4 + i * 2), checked((ushort)(i + 2)));
    var iref = _FullBox("iref", _Box("dimg", dimgBody));

    var iloc = _BuildIdatLocations(itemPayloads);
    var idat = _Box("idat", _Concat(itemPayloads));
    var meta = _FullBox("meta", _Concat([
      _FullBox("pitm", pitmBody),
      _FullBox("iinf", _Concat(iinfParts)),
      iloc,
      iprp,
      iref,
      idat,
    ]));

    return _Concat([_Box("ftyp", ftypBody), meta]);
  }

  private static byte[] _BuildIdentityCycle() {
    var ftypBody = new byte[12];
    System.Text.Encoding.ASCII.GetBytes("heic", 0, 4, ftypBody, 0);
    System.Text.Encoding.ASCII.GetBytes("mif1", 0, 4, ftypBody, 8);

    var pitmBody = _U16(1);
    var iinf = _FullBox("iinf", _Concat([_U16(1), _Infe(1, "iden")]));
    var ipco = _Box("ipco", _Ispe(1, 1));
    var ipmaBody = new byte[8];
    BinaryPrimitives.WriteUInt32BigEndian(ipmaBody.AsSpan(0), 1);
    BinaryPrimitives.WriteUInt16BigEndian(ipmaBody.AsSpan(4), 1);
    ipmaBody[6] = 1;
    ipmaBody[7] = 0x81;
    var iprp = _Box("iprp", _Concat([ipco, _FullBox("ipma", ipmaBody)]));

    var dimg = new byte[6];
    BinaryPrimitives.WriteUInt16BigEndian(dimg.AsSpan(0), 1);
    BinaryPrimitives.WriteUInt16BigEndian(dimg.AsSpan(2), 1);
    BinaryPrimitives.WriteUInt16BigEndian(dimg.AsSpan(4), 1);
    var iref = _FullBox("iref", _Box("dimg", dimg));

    var meta = _FullBox("meta", _Concat([_FullBox("pitm", pitmBody), iinf, iprp, iref]));
    return _Concat([_Box("ftyp", ftypBody), meta]);
  }

  private static byte[] _BuildIpma(
    IReadOnlyList<int> gridAssociations,
    IReadOnlyList<(int Configuration, int Extent)> tileAssociations
  ) {
    using var stream = new MemoryStream();
    _WriteU32(stream, checked((uint)(tileAssociations.Count + 1)));

    _WriteU16(stream, 1);
    stream.WriteByte(checked((byte)gridAssociations.Count));
    foreach (var property in gridAssociations)
      stream.WriteByte(checked((byte)(0x80 | property)));

    for (var i = 0; i < tileAssociations.Count; ++i) {
      _WriteU16(stream, checked((ushort)(i + 2)));
      stream.WriteByte(2);
      stream.WriteByte(checked((byte)(0x80 | tileAssociations[i].Configuration)));
      stream.WriteByte(checked((byte)(0x80 | tileAssociations[i].Extent)));
    }

    return _FullBox("ipma", stream.ToArray());
  }

  private static byte[] _BuildIdatLocations(IReadOnlyList<byte[]> payloads) {
    using var stream = new MemoryStream();
    stream.WriteByte(0x44); // four-byte extent offsets and lengths
    stream.WriteByte(0x00); // no base offset and no extent index
    _WriteU16(stream, checked((ushort)payloads.Count));

    var offset = 0u;
    for (var i = 0; i < payloads.Count; ++i) {
      _WriteU16(stream, checked((ushort)(i + 1)));
      _WriteU16(stream, 1); // construction_method = idat offset
      _WriteU16(stream, 0); // data_reference_index = this file
      _WriteU16(stream, 1); // one extent
      _WriteU32(stream, offset);
      _WriteU32(stream, checked((uint)payloads[i].Length));
      offset = checked(offset + (uint)payloads[i].Length);
    }

    return _FullBox("iloc", stream.ToArray(), version: 1);
  }

  private static byte[] _Compose(
    IReadOnlyList<CodedTile> tiles,
    int rows,
    int columns,
    int outputWidth,
    int outputHeight
  ) {
    var result = new byte[outputWidth * outputHeight * 3];
    for (var tileIndex = 0; tileIndex < rows * columns; ++tileIndex) {
      var tileY = tileIndex / columns * 2;
      var tileX = tileIndex % columns * 2;
      var copyWidth = Math.Min(2, outputWidth - tileX);
      var copyHeight = Math.Min(2, outputHeight - tileY);
      for (var y = 0; y < copyHeight; ++y)
        tiles[tileIndex].Pixels.AsSpan(y * 6, copyWidth * 3)
          .CopyTo(result.AsSpan(((tileY + y) * outputWidth + tileX) * 3, copyWidth * 3));
    }
    return result;
  }

  private static byte[] _Pixel(byte[] pixels, int width, int x, int y)
    => pixels.AsSpan((y * width + x) * 3, 3).ToArray();

  private static byte[] _Infe(ushort itemId, string itemType) {
    var body = new byte[2 + 2 + 4 + 1];
    BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(0), itemId);
    BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(2), 0);
    System.Text.Encoding.ASCII.GetBytes(itemType, 0, 4, body, 4);
    return _FullBox("infe", body, version: 2);
  }

  private static byte[] _Ispe(int width, int height) {
    var body = new byte[8];
    BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(0), checked((uint)width));
    BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(4), checked((uint)height));
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
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0), checked((uint)result.Length));
    System.Text.Encoding.ASCII.GetBytes(type, 0, 4, result, 4);
    body.CopyTo(result.AsSpan(8));
    return result;
  }

  private static byte[] _U16(ushort value) {
    var result = new byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(result, value);
    return result;
  }

  private static byte[] _Concat(IReadOnlyList<byte[]> parts) {
    var total = 0;
    foreach (var part in parts)
      total = checked(total + part.Length);

    var result = new byte[total];
    var at = 0;
    foreach (var part in parts) {
      part.CopyTo(result.AsSpan(at));
      at += part.Length;
    }
    return result;
  }

  private static void _WriteU16(Stream stream, ushort value) {
    Span<byte> buffer = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
    stream.Write(buffer);
  }

  private static void _WriteU32(Stream stream, uint value) {
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
    stream.Write(buffer);
  }

  private readonly record struct CodedTile(byte[] Configuration, byte[] Sample, byte[] Pixels);
}
