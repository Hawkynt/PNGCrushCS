using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.CartesMichelin;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests.Formats.CartesMichelin;

[TestFixture]
public sealed class CartesMichelinTests {

  [Test]
  [Category("Unit")]
  public void WriterTilesAnImageIntoTheSmallestExactOccupiedGrid() {
    var source = _Picture(640, 480);
    var file = CartesMichelinFile.FromRawImage(source);

    Assert.Multiple(() => {
      Assert.That(file.TileWidth, Is.EqualTo(320));
      Assert.That(file.TileHeight, Is.EqualTo(480));
      Assert.That(file.GridColumns, Is.EqualTo(2));
      Assert.That(file.GridRows, Is.EqualTo(2));
      Assert.That(file.TileCount, Is.EqualTo(2));
    });

    var bytes = CartesMichelinWriter.ToBytes(file);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(320));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)), Is.EqualTo(480));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)), Is.EqualTo(2));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)), Is.EqualTo(2));
    });

    for (var column = 0; column < 2; ++column) {
      var entryAt = CartesMichelinFile.HeaderSize + column * CartesMichelinFile.DirectoryEntrySize;
      var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entryAt));
      var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entryAt + 4));
      Assert.That(length, Is.GreaterThan(0), $"tile {column} length");
      Assert.That(bytes.AsSpan(offset, CartesMichelinFile.TileSignature.Length).ToArray(),
        Is.EqualTo(CartesMichelinFile.TileSignature.ToArray()), $"tile {column} signature");
    }

    for (var index = 2; index < 4; ++index) {
      var entryAt = CartesMichelinFile.HeaderSize + index * CartesMichelinFile.DirectoryEntrySize;
      Assert.Multiple(() => {
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entryAt)), Is.Zero, $"entry {index} offset");
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entryAt + 4)), Is.Zero, $"entry {index} length");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void WriterRoundTripsItsGifTilesAndTheRegistryDetectsThem() {
    var source = _Picture(640, 480);
    var bytes = CartesMichelinWriter.ToBytes(CartesMichelinFile.FromRawImage(source));

    Assert.That(FormatRegistry.DetectFromBytes(bytes), Is.EqualTo(ImageFormat.CartesMichelin));

    var read = CartesMichelinFile.ToRawImage(CartesMichelinReader.FromBytes(bytes));
    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(source.Width));
      Assert.That(read.Height, Is.EqualTo(source.Height));
      Assert.That(read.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void OneOccupiedTileStillUsesTheFormatsMinimumTwoByTwoDirectory() {
    var source = _Picture(200, 100);
    var file = CartesMichelinFile.FromRawImage(source);
    var bytes = CartesMichelinWriter.ToBytes(file);
    var read = CartesMichelinReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(file.TileWidth, Is.EqualTo(200));
      Assert.That(file.TileHeight, Is.EqualTo(100));
      Assert.That(file.GridColumns, Is.EqualTo(2));
      Assert.That(file.GridRows, Is.EqualTo(2));
      Assert.That(file.TileCount, Is.EqualTo(1));
      Assert.That(read.Width, Is.EqualTo(200));
      Assert.That(read.Height, Is.EqualTo(100));
      Assert.That(read.GridColumns, Is.EqualTo(2));
      Assert.That(read.GridRows, Is.EqualTo(2));
      Assert.That(read.TileCount, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReaderRefusesACorruptPresentTileInsteadOfSilentlyDroppingIt() {
    var bytes = CartesMichelinWriter.ToBytes(CartesMichelinFile.FromRawImage(_Picture(640, 480)));
    var firstTile = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(CartesMichelinFile.HeaderSize));
    bytes[firstTile] = (byte)'B';

    Assert.Throws<InvalidDataException>(() => CartesMichelinReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImageRefusesAnAxisThatCannotBeRepresentedExactly() {
    var source = _Picture(521, 64);

    Assert.Throws<ArgumentOutOfRangeException>(() => CartesMichelinFile.FromRawImage(source));
  }

  [Test]
  [Category("Unit")]
  public void SignatureProbeAbstainsUntilTheFirstPresentTilesPayloadIsAvailable() {
    var bytes = CartesMichelinWriter.ToBytes(CartesMichelinFile.FromRawImage(_Picture(200, 100)));
    var payloadAt = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(CartesMichelinFile.HeaderSize));

    Assert.That(_Matches<CartesMichelinFile>(bytes.AsSpan(0, payloadAt)), Is.Null);
    Assert.That(_Matches<CartesMichelinFile>(bytes), Is.True);
  }

  private static RawImage _Picture(int width, int height) {
    ReadOnlySpan<byte> palette = [
      0x10, 0x20, 0x30,
      0xE0, 0x40, 0x20,
      0x20, 0xD0, 0x70,
      0xF0, 0xE0, 0x90,
    ];
    var pixels = new byte[checked(width * height * 3)];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var color = ((x / 17) + (y / 13)) & 3;
      palette.Slice(color * 3, 3).CopyTo(pixels.AsSpan((y * width + x) * 3, 3));
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static bool? _Matches<T>(ReadOnlySpan<byte> data) where T : IImageFormatMetadata<T>
    => T.MatchesSignature(data);
}
