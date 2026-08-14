using System;
using System.Diagnostics;
using Hawkynt.FileFormats.Images.Tests;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Pcl.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A picture in the eight colours a job can state without a palette command.</summary>
  private static RawImage _Colours(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        data[at] = (byte)((x + y) % 2 == 0 ? 255 : 0);
        data[at + 1] = (byte)(x / 3 % 2 == 0 ? 255 : 0);
        data[at + 2] = (byte)(y / 2 % 2 == 0 ? 255 : 0);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TheEightDeviceColours_ReproduceExactly() {
    var source = _Colours(37, 11);
    var decoded = PclFile.ToRawImage(PclReader.FromBytes(PclWriter.ToBytes(PclFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = PclFile.FromRawImage(_Colours(200, 3));
    var tall = PclFile.FromRawImage(_Colours(3, 200));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  /// <summary>
  /// The eight-entry device palette holds black, white and six saturated colours and no grey at all,
  /// so a grey goes out as the black and white a printer prints it in rather than being tinted.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void FromRawImage_AGreyGoesOutBlackAndWhite() {
    var pixels = new byte[37 * 11];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 2 == 0 ? 240 : 10);

    var file = PclFile.FromRawImage(new() { Width = 37, Height = 11, Format = PixelFormat.Gray8, PixelData = pixels });
    var job = Encoding.Latin1.GetString(PclWriter.ToBytes(file));
    var decoded = PclFile.ToRawImage(PclReader.FromBytes(PclWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(file.Planes, Is.EqualTo(1));
      Assert.That(job, Does.Contain("*r1U"));
      Assert.That(decoded.PaletteCount, Is.EqualTo(2));
      Assert.That(decoded.PixelData[0], Is.EqualTo(0), "the light pixel is the unprinted one");
      Assert.That(decoded.PixelData[1], Is.EqualTo(1));
    });
  }

  /// <summary>
  /// The size is stated with <c>ESC*r#S</c> and <c>ESC*r#T</c>, and a printer takes those only
  /// outside a raster: sent after <c>ESC*r#A</c> they are locked out and the page prints at whatever
  /// the first row's byte count happened to make it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_StatesTheSizeBeforeTheRasterStarts() {
    var job = Encoding.Latin1.GetString(PclWriter.ToBytes(PclFile.FromRawImage(_Colours(37, 11))));

    var width = job.IndexOf("*r37S", StringComparison.Ordinal);
    var height = job.IndexOf("*r11T", StringComparison.Ordinal);
    var start = job.IndexOf("*r0A", StringComparison.Ordinal);

    Assert.Multiple(() => {
      Assert.That(job, Does.StartWith("E"), "a job resets the printer first");
      Assert.That(width, Is.GreaterThan(0));
      Assert.That(height, Is.GreaterThan(width));
      Assert.That(start, Is.GreaterThan(height), "and both before the raster opens");
      Assert.That(job, Does.Contain("*b2M"), "the TIFF packing");
      Assert.That(job, Does.Contain("*rC"), "and the raster is closed");
    });
  }

  /// <summary>
  /// A colour row is three planes: the first two handed over with <c>ESC*b#V</c>, which leaves the
  /// row open, and the last with <c>ESC*b#W</c>, which closes it. A writer sending every plane with
  /// W would make each plane its own row and the page would come out three times as tall.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_SendsAColourRowAsTwoPlanesAndAClose() {
    var job = PclWriter.ToBytes(PclFile.FromRawImage(_Colours(37, 11)));

    var transfers = 0;
    var closes = 0;
    for (var at = 0; at + 4 < job.Length; ++at) {
      if (job[at] != PclFile.Escape || job[at + 1] != '*' || job[at + 2] != 'b')
        continue;

      var end = at + 3;
      while (end < job.Length && job[end] is >= (byte)'0' and <= (byte)'9')
        ++end;

      if (end >= job.Length)
        break;

      if (job[end] == 'V')
        ++transfers;
      else if (job[end] == 'W')
        ++closes;
    }

    Assert.Multiple(() => {
      Assert.That(closes, Is.EqualTo(11), "one closing plane a row");
      Assert.That(transfers, Is.EqualTo(11 * 2), "and two open ones");
    });
  }

  /// <summary>What <c>file(1)</c> makes of it, which knows the escape framing and nothing else.</summary>
  [Test]
  [Category("Conformance")]
  public void SomethingElseCallsItAPrinterJob() {
    var directory = Directory.CreateTempSubdirectory("pcl");
    try {
      var path = Path.Combine(directory.FullName, "job.pcl");
      File.WriteAllBytes(path, PclWriter.ToBytes(PclFile.FromRawImage(_Colours(37, 11))));

      using var identify = ExternalTool.StartOrIgnore("file", $"-b \"{path}\"");

      var reported = identify.StandardOutput.ReadToEnd().Trim();
      identify.WaitForExit();

      if (identify.ExitCode != 0)
        Assert.Ignore("file(1) would not answer");

      Assert.That(reported, Does.Contain("PCL"));
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }
}
