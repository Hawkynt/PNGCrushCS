using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests.GapClosures;

/// <summary>
/// Four names XnView reads with a reader this library already has: <c>.thb</c>, <c>.2d</c> and
/// <c>.bmc</c> with its Windows bitmap reader, and <c>.fx3</c> with its TIFF reader. Claiming a name
/// is only honest if the reader behind it would refuse a file of some other format arriving under
/// that name, so that is what these insist on.
/// </summary>
[TestFixture]
public sealed class ClaimedExtensionTests {

  private static readonly string[] _BitmapNames = [".thb", ".2d", ".bmc"];

  private static byte[] _Bmp(int width, int height) {
    var stride = (width * 3 + 3) & ~3;
    var output = new byte[54 + stride * height];
    output[0] = (byte)'B';
    output[1] = (byte)'M';
    _Write(output, 2, output.Length);
    _Write(output, 10, 54);
    _Write(output, 14, 40);
    _Write(output, 18, width);
    _Write(output, 22, height);
    output[26] = 1;
    output[28] = 24;
    for (var i = 54; i < output.Length; ++i)
      output[i] = (byte)(i * 11 % 251);

    return output;
  }

  private static void _Write(byte[] data, int at, int value) {
    data[at] = (byte)value;
    data[at + 1] = (byte)(value >> 8);
    data[at + 2] = (byte)(value >> 16);
    data[at + 3] = (byte)(value >> 24);
  }

  private static byte[] _Tiff(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 251);

    return FileFormat.Tiff.TiffWriter.ToBytes(FileFormat.Tiff.TiffFile.FromRawImage(new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    }));
  }

  /// <summary>A JPEG, which is none of the formats any of these names belongs to.</summary>
  private static byte[] _Foreign() {
    byte[] head = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00];
    return head.Concat(Enumerable.Range(0, 512).Select(i => (byte)(i * 37 % 251))).Concat<byte>([0xFF, 0xD9]).ToArray();
  }

  private static void _With(byte[] data, string extension, Action<FileInfo> check) {
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
    File.WriteAllBytes(path, data);
    try {
      check(new FileInfo(path));
    } finally {
      File.Delete(path);
    }
  }

  private static void _AssertClaimed(string extension) {
    var claimed = FormatRegistry.AllFormats
      .Where(entry => entry.AllExtensions?.Any(x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase)) == true)
      .ToArray();

    Assert.That(claimed, Is.Not.Empty, $"nothing claims {extension}");
  }

  [Test]
  [Category("Integration")]
  public void TheBitmapNamesAreClaimedAndReadABitmap() {
    foreach (var extension in _BitmapNames) {
      _AssertClaimed(extension);
      _With(_Bmp(9, 6), extension, file => {
        var image = FormatRegistry.Read(file);
        Assert.That(image, Is.Not.Null, $"{extension} should read a Windows bitmap");
        Assert.Multiple(() => {
          Assert.That(image!.Width, Is.EqualTo(9));
          Assert.That(image.Height, Is.EqualTo(6));
        });
      });
    }
  }

  [Test]
  [Category("Integration")]
  public void TheBitmapNamesRefuseAForeignFile() {
    foreach (var extension in _BitmapNames)
      _With(_Foreign(), extension, file => {
        var entries = FormatRegistry.AllFormats
          .Where(entry => entry.AllExtensions?.Any(x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase)) == true)
          .ToArray();

        foreach (var entry in entries)
          Assert.Throws<InvalidDataException>(() => entry.LoadRawImageOrThrow!(file), $"{entry.Name} took a JPEG named {extension}");
      });
  }

  [Test]
  [Category("Integration")]
  public void TheFugawiNameIsClaimedAndReadsATiff() {
    _AssertClaimed(".fx3");
    _With(_Tiff(9, 6), ".fx3", file => {
      var image = FormatRegistry.Read(file);
      Assert.That(image, Is.Not.Null);
      Assert.Multiple(() => {
        Assert.That(image!.Width, Is.EqualTo(9));
        Assert.That(image.Height, Is.EqualTo(6));
      });
    });
  }

  [Test]
  [Category("Integration")]
  public void TheFugawiNameRefusesAForeignFile()
    => _With(_Foreign(), ".fx3", file => {
      var entries = FormatRegistry.AllFormats
        .Where(entry => entry.AllExtensions?.Any(x => string.Equals(x, ".fx3", StringComparison.OrdinalIgnoreCase)) == true)
        .ToArray();

      foreach (var entry in entries)
        Assert.Throws<InvalidDataException>(() => entry.LoadRawImageOrThrow!(file), $"{entry.Name} took a JPEG named .fx3");
    });
}
