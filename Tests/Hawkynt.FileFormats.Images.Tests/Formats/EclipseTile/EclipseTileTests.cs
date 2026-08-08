using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.EclipseTile;

namespace FileFormat.EclipseTile.Tests;

[TestFixture]
public sealed class EclipseTileTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 13);
      pixels[i * 3 + 1] = (byte)(i * 7);
      pixels[i * 3 + 2] = (byte)(i * 3);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EclipseTileReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => EclipseTileReader.FromBytes(new byte[EclipseTileFile.HeaderSize]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongCreator_ThrowsInvalidDataException() {
    var data = EclipseTileWriter.ToBytes(EclipseTileFile.FromRawImage(_Picture(8, 4)));
    data[EclipseTileFile.CreatorAt] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => EclipseTileReader.FromBytes(data));
  }

  /// <summary>The tiles fill the size rounded up to whole tiles, and the header states that rounded
  /// size; the two together are the length of the file in every sample there is.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ThePaddedSizeMustAccountForTheFile() {
    var data = EclipseTileWriter.ToBytes(EclipseTileFile.FromRawImage(_Picture(8, 4)));
    Array.Resize(ref data, data.Length - EclipseTileFile.BytesPerPixel);

    Assert.Throws<InvalidDataException>(() => EclipseTileReader.FromBytes(data));
  }

  /// <summary>A padded size that is not what rounding the real one gives is a header read in the
  /// wrong place, not a picture.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ThePaddedSizeMustBeTheSizeRoundedUp() {
    var data = EclipseTileWriter.ToBytes(EclipseTileFile.FromRawImage(_Picture(8, 4)));
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(EclipseTileFile.PaddedWidthAt), EclipseTileFile.TileSize * 2);

    Assert.Throws<InvalidDataException>(() => EclipseTileReader.FromBytes(data));
  }

  /// <summary>The colour space and the channel count say the same thing twice.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ColourSpaceAndChannelCountMustAgree() {
    var data = EclipseTileWriter.ToBytes(EclipseTileFile.FromRawImage(_Picture(8, 4)));
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(EclipseTileFile.ColorSpaceAt), EclipseTileFile.CmykColorSpace);

    Assert.Throws<InvalidDataException>(() => EclipseTileReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TheHeaderAccountsForTheWholeFile() {
    var bytes = EclipseTileWriter.ToBytes(EclipseTileFile.FromRawImage(_Picture(300, 260)));
    var paddedWidth = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(EclipseTileFile.PaddedWidthAt));
    var paddedHeight = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(EclipseTileFile.PaddedHeightAt));

    Assert.Multiple(() => {
      Assert.That(paddedWidth, Is.EqualTo(512), "300 across fills two tiles");
      Assert.That(paddedHeight, Is.EqualTo(512), "260 down fills two tiles");
      Assert.That(EclipseTileFile.HeaderSize + paddedWidth * paddedHeight * EclipseTileFile.BytesPerPixel,
        Is.EqualTo(bytes.Length));
    });
  }

  /// <summary>A separation with black generation is not a formula, so writing four channels is refused
  /// rather than invented.</summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_FourChannelsIsRefused() {
    var file = EclipseTileFile.FromRawImage(_Picture(8, 4)) with { ChannelCount = EclipseTileFile.CmykChannelCount };

    Assert.Throws<NotSupportedException>(() => EclipseTileWriter.ToBytes(file));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThePictureComesBackExactly() {
    var original = _Picture(300, 260);
    var decoded = EclipseTileFile.ToRawImage(
      EclipseTileReader.FromBytes(EclipseTileWriter.ToBytes(EclipseTileFile.FromRawImage(original))));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(300));
      Assert.That(decoded.Height, Is.EqualTo(260));
      Assert.That(decoded.PixelData, Is.EqualTo(original.PixelData), "across tile boundaries and the vertical flip");
    });
  }
}
