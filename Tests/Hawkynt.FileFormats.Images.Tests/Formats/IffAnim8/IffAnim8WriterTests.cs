using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.IffAnim8;

namespace FileFormat.IffAnim8.Tests;

[TestFixture]
public sealed class IffAnim8WriterTests {

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_CopiesPixelsForWriting() {
    var pixels = new byte[] { 1, 2, 3, 4, 5, 6 };
    var image = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    var file = IffAnim8File.FromRawImage(image);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(2));
      Assert.That(file.Height, Is.EqualTo(1));
      Assert.That(file.PixelData, Is.EqualTo(pixels));
      Assert.That(file.PixelData, Is.Not.SameAs(pixels));
      Assert.That(file.RawData, Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FromPixels_WritesMethod8WordDeltaThatDecodesToSourcePixels() {
    const int width = 17;
    const int height = 4;
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var value = (byte)(((x + y) % 3 == 0) ? 255 : 0);
      var at = (y * width + x) * 3;
      pixels[at] = value;
      pixels[at + 1] = value;
      pixels[at + 2] = value;
    }

    var file = IffAnim8File.FromRawImage(new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    });

    var encoded = IffAnim8Writer.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(encoded.AsSpan(0, 4).SequenceEqual("FORM"u8), Is.True);
      Assert.That(encoded.AsSpan(8, 4).SequenceEqual("ANIM"u8), Is.True);
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(4, 4)), Is.EqualTo(encoded.Length - 8));
      Assert.That(encoded.AsSpan(12, 4).SequenceEqual("FORM"u8), Is.True);
      Assert.That(encoded.AsSpan(20, 4).SequenceEqual("ILBM"u8), Is.True);
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(16, 4)), Is.EqualTo(encoded.Length - 20));
    });

    var anhd = _FindFirstFrameChunk(encoded, "ANHD"u8);
    var dlta = _FindFirstFrameChunk(encoded, "DLTA"u8);
    Assert.Multiple(() => {
      Assert.That(anhd, Is.GreaterThanOrEqualTo(0));
      Assert.That(dlta, Is.GreaterThanOrEqualTo(0));
      Assert.That(encoded[anhd + 8], Is.EqualTo(8), "ANHD operation must be method 8");
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(anhd + 28, 4)), Is.Zero,
        "ANHD bits bit 0 clear selects WORD data");
    });

    var dltaData = dlta + 8;
    Assert.That(
      Enumerable.Range(0, 8).Any(plane => BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(dltaData + plane * 4, 4)) != 0),
      Is.True,
      "at least one bit plane must carry a delta for the black/white test pattern");
    Assert.That(
      Enumerable.Range(8, 8).All(slot => BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(dltaData + slot * 4, 4)) == 0),
      Is.True,
      "method 8 leaves the second set of eight pointers unused");

    Assert.That(_DecodeFirstFrame(encoded), Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LegacyParsedFile_PreservesRawData() {
    var rawData = new byte[IffAnim8File.MinFileSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7);

    var file = new IffAnim8File {
      Width = 320,
      Height = 200,
      PixelData = [],
      RawData = rawData,
    };

    var encoded = IffAnim8Writer.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(encoded, Is.EqualTo(rawData));
      Assert.That(encoded, Is.Not.SameAs(rawData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataLengthDoesNotMatchDimensions_ThrowsInvalidDataException() {
    var file = new IffAnim8File {
      Width = 2,
      Height = 2,
      PixelData = [1, 2, 3],
      RawData = [],
    };

    Assert.Throws<InvalidDataException>(() => IffAnim8Writer.ToBytes(file));
  }

  private static byte[] _DecodeFirstFrame(byte[] encoded) {
    var bmhd = _FindFirstFrameChunk(encoded, "BMHD"u8);
    var cmap = _FindFirstFrameChunk(encoded, "CMAP"u8);
    var dlta = _FindFirstFrameChunk(encoded, "DLTA"u8);
    if (bmhd < 0 || cmap < 0 || dlta < 0)
      throw new InvalidDataException("The first ANIM8 frame is missing BMHD, CMAP, or DLTA.");

    var width = BinaryPrimitives.ReadUInt16BigEndian(encoded.AsSpan(bmhd + 8, 2));
    var height = BinaryPrimitives.ReadUInt16BigEndian(encoded.AsSpan(bmhd + 10, 2));
    var numPlanes = encoded[bmhd + 16];
    var rowBytes = ((width + 15) / 16) * 2;
    var planar = new byte[rowBytes * numPlanes * height];
    var dltaData = dlta + 8;

    for (var plane = 0; plane < numPlanes; ++plane) {
      var pointer = BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(dltaData + plane * 4, 4));
      if (pointer == 0)
        continue;

      var at = checked(dltaData + (int)pointer);
      for (var column = 0; column < rowBytes / 2; ++column) {
        var operationCount = _ReadUInt16(encoded, ref at);
        var row = 0;

        for (var operation = 0; operation < operationCount; ++operation) {
          var opcode = _ReadUInt16(encoded, ref at);
          if (opcode == 0) {
            var count = _ReadUInt16(encoded, ref at);
            var value = _ReadUInt16(encoded, ref at);
            for (var i = 0; i < count; ++i)
              _PutWord(planar, row++, numPlanes, rowBytes, plane, column, value);
            continue;
          }

          if ((opcode & 0x8000) != 0) {
            var count = opcode & 0x7FFF;
            for (var i = 0; i < count; ++i)
              _PutWord(planar, row++, numPlanes, rowBytes, plane, column, _ReadUInt16(encoded, ref at));
            continue;
          }

          row += opcode;
        }

        if (row > height)
          throw new InvalidDataException("ANIM8 column operations ran beyond the bitmap height.");
      }
    }

    var indices = PlanarConverter.IlbmPlanarToChunky(planar, width, height, numPlanes);
    var paletteSize = BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(cmap + 4, 4));
    var palette = encoded.AsSpan(cmap + 8, paletteSize);
    var rgb = new byte[indices.Length * 3];

    for (var i = 0; i < indices.Length; ++i) {
      var paletteAt = indices[i] * 3;
      if (paletteAt + 2 >= palette.Length)
        throw new InvalidDataException("ANIM8 pixel references a color outside CMAP.");

      rgb[i * 3] = palette[paletteAt];
      rgb[i * 3 + 1] = palette[paletteAt + 1];
      rgb[i * 3 + 2] = palette[paletteAt + 2];
    }

    return rgb;
  }

  private static int _FindFirstFrameChunk(byte[] encoded, ReadOnlySpan<byte> id) {
    const int nestedFormStart = 12;
    if (encoded.Length < nestedFormStart + 12 || !encoded.AsSpan(nestedFormStart, 4).SequenceEqual("FORM"u8))
      return -1;

    var formSize = BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(nestedFormStart + 4, 4));
    var end = Math.Min(encoded.Length, checked(nestedFormStart + 8 + formSize));
    for (var at = nestedFormStart + 12; at + 8 <= end;) {
      var size = BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(at + 4, 4));
      if (size < 0 || at + 8L + size > end)
        return -1;
      if (encoded.AsSpan(at, 4).SequenceEqual(id))
        return at;

      at = checked(at + 8 + size + (size & 1));
    }

    return -1;
  }

  private static ushort _ReadUInt16(byte[] data, ref int at) {
    var result = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at, 2));
    at += 2;
    return result;
  }

  private static void _PutWord(byte[] planar, int row, int numPlanes, int rowBytes, int plane, int column, ushort value) {
    var at = checked((row * numPlanes + plane) * rowBytes + column * 2);
    BinaryPrimitives.WriteUInt16BigEndian(planar.AsSpan(at, 2), value);
  }
}
