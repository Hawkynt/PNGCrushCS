using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Pcd;
using FileFormat.Pcds;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Pcds.Tests;

/// <summary>
/// The Photo CD read as sRGB, which is the same file as the Photo CD and not a second one.
/// </summary>
/// <remarks>
/// The name suggests a stacked or multi-picture form and it is nothing of the kind: written from the
/// same picture, a <c>.pcd</c> and a <c>.pcds</c> are byte for byte identical. Everything that
/// distinguishes them happens at the last step of reading, so these fixtures are built by writing
/// one and reading it under both names.
/// </remarks>
[TestFixture]
public sealed class PcdsTests {

  private static RawImage _Source(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 255 / Math.Max(1, width - 1));
      pixels[at + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
      pixels[at + 2] = (byte)((x + y) & 0xFF);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FromFile_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => PcdsReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_MissingFile_Throws()
    => Assert.Throws<FileNotFoundException>(() => PcdsReader.FromFile(new FileInfo("nonexistent.pcds")));

  [Test]
  [Category("Unit")]
  public void FromBytes_NullData_Throws()
    => Assert.Throws<ArgumentNullException>(() => PcdsReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_Throws()
    => Assert.Throws<InvalidDataException>(() => PcdsReader.FromBytes(new byte[2049]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_Throws() {
    var data = PcdsWriter.ToBytes(PcdsFile.FromRawImage(_Source(768, 512)));
    data[PcdFile.PreambleSize] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => PcdsReader.FromBytes(data));
  }

  /// <summary>
  /// The container is the same one, so the other name's reader has to accept these bytes whole.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Bytes_AreAPhotoCdTheOtherReaderAlsoTakes() {
    var data = PcdsWriter.ToBytes(PcdsFile.FromRawImage(_Source(768, 512)));

    var asPcd = PcdReader.FromBytes(data);
    var asPcds = PcdsReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That((asPcd.Width, asPcd.Height), Is.EqualTo((768, 512)));
      Assert.That((asPcds.Width, asPcds.Height), Is.EqualTo((768, 512)));
      // If these agreed there would be nothing here to implement.
      Assert.That(asPcds.PixelData, Is.Not.EqualTo(asPcd.PixelData));
    });
  }

  /// <summary>
  /// The three planes are the three channels, in the order they are stored: the luminance plane is
  /// red and the two chrominance planes are green and blue.
  /// </summary>
  /// <remarks>
  /// Built by hand rather than through the writer, because the writer agreeing with the reader
  /// about a channel order they both invented would prove nothing about which plane is which.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void ToRawImage_TakesThePlanesAsTheChannelsInStoredOrder() {
    const int width = 768;
    const int height = 512;
    var half = width / 2;
    var groupBytes = width * 2 + half * 2;
    var (_, _, offset) = PcdFile.Resolutions[^1];
    var data = new byte[offset + PcdFile.PlaneBytes(width, height)];
    PcdFile.Magic.CopyTo(data.AsSpan(PcdFile.PreambleSize));

    // A luminance that changes across the picture against two chrominances that do not, so the
    // half-resolution planes read the same whatever is made of the samples between them.
    for (var y = 0; y < height; ++y) {
      var row = offset + y / 2 * groupBytes + (y & 1) * width;
      for (var x = 0; x < width; ++x)
        data[row + x] = (byte)((x * 3 + y * 5) & 0xFF);
    }

    for (var row = 0; row < height / 2; ++row) {
      var at = offset + row * groupBytes + width * 2;
      for (var i = 0; i < half; ++i) {
        data[at + i] = 200;
        data[at + half + i] = 60;
      }
    }

    var image = PcdsFile.ToRawImage(PcdsReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That((image.Width, image.Height), Is.EqualTo((width, height)));
      for (var y = 0; y < height; y += 37)
      for (var x = 0; x < width; x += 41) {
        var at = (y * width + x) * 3;
        Assert.That(image.PixelData[at], Is.EqualTo((byte)((x * 3 + y * 5) & 0xFF)), $"red at {x},{y}");
        Assert.That(image.PixelData[at + 1], Is.EqualTo(200), $"green at {x},{y}");
        Assert.That(image.PixelData[at + 2], Is.EqualTo(60), $"blue at {x},{y}");
      }
    });
  }

  /// <summary>
  /// A picture whose colour is already flat within every block the container samples comes back
  /// exactly, because nothing then has to be guessed between the stored samples.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void RoundTrip_KeepsEveryPixelWhereTheSubsamplingCostsNothing() {
    const int width = 768;
    const int height = 512;
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)((x * 7 + y * 11) & 0xFF);
      pixels[at + 1] = 200;
      pixels[at + 2] = 60;
    }

    var source = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
    var read = PcdsFile.ToRawImage(PcdsReader.FromBytes(PcdsWriter.ToBytes(PcdsFile.FromRawImage(source))));

    Assert.That(read.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void Registry_KnowsTheExtension()
    => Assert.That(FormatRegistry.DetectFromExtension(".pcds"), Is.EqualTo(ImageFormat.Pcds));
}
