using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.ElectricImage;

namespace FileFormat.ElectricImage.Tests;

/// <summary>
/// The fixtures are built to the layout eighteen real files were measured against: a version and a
/// frame count, a frame header, a colour table when the frame has one, and run-length data that has
/// to come out at exactly the size the frame states and end where the file does.
/// </summary>
[TestFixture]
public sealed class ElectricImageTests {

  /// <summary>Packs pixels as literal runs, which is the coding a lead byte of 0x80 or over names.</summary>
  private static byte[] _Literals(IReadOnlyList<byte> pixels, int bytesPerPixel) {
    var output = new List<byte>();
    var elements = pixels.Count / bytesPerPixel;
    for (var at = 0; at < elements;) {
      var run = Math.Min(128, elements - at);
      output.Add((byte)(0x80 | (run - 1)));
      for (var i = 0; i < run * bytesPerPixel; ++i)
        output.Add(pixels[at * bytesPerPixel + i]);

      at += run;
    }

    return output.ToArray();
  }

  private static byte[] _Build(int width, int height, int depth, byte[] data, int mode = 0x0100, int extra = 0x0100, byte[]? palette = null, int version = ElectricImageFile.Version, int frames = 1) {
    var output = new List<byte>();
    output.AddRange([(byte)(version >> 8), (byte)version]);
    output.AddRange(_Be32(frames));

    output.AddRange(new byte[4]);
    output.AddRange(new byte[4]);
    output.AddRange(_Be16(height));
    output.AddRange(_Be16(width));
    output.Add((byte)depth);
    output.Add(0);
    output.AddRange(new byte[4]);
    output.AddRange(_Be16(height));
    output.AddRange(_Be16(width));
    output.AddRange(_Be16(extra));
    output.AddRange(_Be32(data.Length));
    output.AddRange(_Be16(mode));

    if (depth == 8) {
      output.Add(0);
      output.Add(255);
      output.AddRange(palette ?? Enumerable.Range(0, 256).SelectMany(i => new[] { (byte)i, (byte)(255 - i), (byte)(i / 2) }).ToArray());
    }

    if (mode == 0x0001)
      output.AddRange(new byte[5]);

    output.AddRange(data);
    return output.ToArray();
  }

  private static byte[] _Be16(int value) => [(byte)(value >> 8), (byte)value];
  private static byte[] _Be32(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ElectricImageReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_AVersionThatIsNotFiveIsRefused()
    => Assert.Throws<InvalidDataException>(() => ElectricImageReader.FromBytes(_Build(4, 2, 8, _Literals(new byte[8], 1), version: 4)));

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsAnEightBitFrameThroughItsColourTable() {
    var indices = Enumerable.Range(0, 8).Select(i => (byte)(i * 5)).ToArray();
    var file = ElectricImageReader.FromBytes(_Build(4, 2, 8, _Literals(indices, 1)));
    var image = ElectricImageFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(4));
      Assert.That(image.Height, Is.EqualTo(2));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(image.PixelData, Is.EqualTo(indices));
      Assert.That(image.Palette![indices[1] * 3], Is.EqualTo(indices[1]));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TwentyFourBitsWithAFourthChannelIsAlphaFirst() {
    var pixels = Enumerable.Range(0, 4 * 4).Select(i => (byte)(i * 3)).ToArray();
    var file = ElectricImageReader.FromBytes(_Build(2, 2, 24, _Literals(pixels, 4), mode: 0x0001, extra: 0x0108));
    var image = ElectricImageFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Argb32));
      Assert.That(image.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ARunRepeatsTheOneElementBehindIt() {
    // A lead byte under 0x80 is a run of that many plus one copies of the element that follows.
    byte[] data = [3, 9, 3, 7];
    var file = ElectricImageReader.FromBytes(_Build(4, 2, 8, data));

    Assert.That(file.Frames[0].PixelData, Is.EqualTo(new byte[] { 9, 9, 9, 9, 7, 7, 7, 7 }));
  }

  /// <summary>
  /// Every one of the eighteen files ends exactly where its frames do. A file that does not is not
  /// one of these, however plausible its head.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AFileWithBytesLeftOverIsRefused() {
    var padded = _Build(4, 2, 8, _Literals(new byte[8], 1)).Concat(new byte[3]).ToArray();
    Assert.Throws<InvalidDataException>(() => ElectricImageReader.FromBytes(padded));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DataThatDoesNotFillTheFrameIsRefused()
    => Assert.Throws<InvalidDataException>(() => ElectricImageReader.FromBytes(_Build(4, 4, 8, _Literals(new byte[8], 1))));

  [Test]
  [Category("Unit")]
  public void FromBytes_ADepthWithNothingToCheckItAgainstIsRefused()
    => Assert.Throws<InvalidDataException>(() => ElectricImageReader.FromBytes(_Build(4, 2, 16, _Literals(new byte[16], 2), mode: 0x0001)));
}
