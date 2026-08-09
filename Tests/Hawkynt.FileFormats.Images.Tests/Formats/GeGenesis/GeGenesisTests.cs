using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.GeGenesis;

namespace FileFormat.GeGenesis.Tests;

/// <summary>
/// The fixtures are built to the header David Clunie's Medical Image Format FAQ describes, and the
/// rules they exercise are the ones the Visible Human sample settled: the arithmetic decides whether
/// a file is uncompressed, and a sixteen-bit picture is scaled by its own largest sample.
/// </summary>
[TestFixture]
public sealed class GeGenesisTests {

  private static byte[] _Build(int width, int height, int depth, byte[] pixels, int headerLength = GeGenesisFile.ControlHeaderSize, int compression = 1) {
    var output = new byte[headerLength + pixels.Length];
    "IMGF"u8.CopyTo(output);
    _Write(output, 4, headerLength);
    _Write(output, 8, width);
    _Write(output, 12, height);
    _Write(output, 16, depth);
    _Write(output, 20, compression);
    pixels.CopyTo(output, headerLength);
    return output;
  }

  private static void _Write(byte[] data, int at, int value) {
    data[at] = (byte)(value >> 24);
    data[at + 1] = (byte)(value >> 16);
    data[at + 2] = (byte)(value >> 8);
    data[at + 3] = (byte)value;
  }

  private static byte[] _Samples16(params int[] values)
    => values.SelectMany(v => new[] { (byte)(v >> 8), (byte)v }).ToArray();

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => GeGenesisReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheOpeningLettersIsRefused()
    => Assert.Throws<InvalidDataException>(() => GeGenesisReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsTheSizeAndTheDepthTheHeaderStates() {
    var file = GeGenesisReader.FromBytes(_Build(4, 2, 8, Enumerable.Range(0, 8).Select(i => (byte)i).ToArray()));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(4));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.Depth, Is.EqualTo(8));
      Assert.That(file.PixelData.Length, Is.EqualTo(8));
    });
  }

  /// <summary>
  /// A compressed file is shorter than its own arithmetic, and that is what refuses it — not the
  /// compression code, which the one file measured states as 1 for an uncompressed picture.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_APictureThatDoesNotAccountForTheFileIsRefused() {
    var truncated = _Build(4, 2, 8, new byte[8])[..^1];
    Assert.Throws<InvalidDataException>(() => GeGenesisReader.FromBytes(truncated));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ADepthTheFormatDoesNotHaveIsRefused()
    => Assert.Throws<InvalidDataException>(() => GeGenesisReader.FromBytes(_Build(4, 2, 12, new byte[12])));

  [Test]
  [Category("Unit")]
  public void FromBytes_AHeaderPointingPastTheFileIsRefused() {
    var data = _Build(4, 2, 8, new byte[8]);
    _Write(data, 4, 1 << 20);
    Assert.Throws<InvalidDataException>(() => GeGenesisReader.FromBytes(data));
  }

  /// <summary>
  /// The top byte of every sample has to be what XnView draws, which is the sample times 255 over
  /// the largest sample in the picture.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_ScalesSixteenBitSamplesByTheLargestOne() {
    var samples = new[] { 0, 100, 500, 1000 };
    var file = GeGenesisReader.FromBytes(_Build(4, 1, 16, _Samples16(samples)));
    var image = GeGenesisFile.ToRawImage(file);

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray16));
    Assert.Multiple(() => {
      for (var i = 0; i < samples.Length; ++i)
        Assert.That(image.PixelData[i * 2], Is.EqualTo((byte)(samples[i] * 255 / 1000)), $"sample {i}");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RoundTrips() {
    var pixels = Enumerable.Range(0, 12).Select(i => (byte)(i * 17)).ToArray();
    var written = GeGenesisWriter.ToBytes(new() { Width = 4, Height = 3, Depth = 8, PixelData = pixels });
    var read = GeGenesisReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(4));
      Assert.That(read.Height, Is.EqualTo(3));
      Assert.That(read.Depth, Is.EqualTo(8));
      Assert.That(read.PixelData, Is.EqualTo(pixels));
    });
  }
}
