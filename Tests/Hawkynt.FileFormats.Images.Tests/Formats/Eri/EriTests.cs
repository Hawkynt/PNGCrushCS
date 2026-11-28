using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.Png;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Eri.Tests;

/// <summary>
/// ERI held to the Entis libraries: files their encoders wrote, and the pixels their decoders made
/// of them, beside our writer's files and what those decoders made of ours.
/// </summary>
/// <remarks>Fixtures\Eri\manifest.txt records how every fixture was produced.</remarks>
[TestFixture]
public sealed class EriTests {

  private static string _FixtureDirectory => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Eri");

  private static byte[] _Fixture(string name) => File.ReadAllBytes(Path.Combine(_FixtureDirectory, name));

  /// <summary>Every fixture that has a reference decode beside it.</summary>
  private static IEnumerable<string> _Decoded()
    => Directory.Exists(_FixtureDirectory)
      ? Directory.EnumerateFiles(_FixtureDirectory)
        .Where(p => p.EndsWith(".eri", StringComparison.Ordinal) || p.EndsWith(".mei", StringComparison.Ordinal))
        .Select(Path.GetFileName)
        .Where(n => File.Exists(Path.Combine(_FixtureDirectory, Path.GetFileNameWithoutExtension(n) + "_0.pam")))
        .Order(StringComparer.Ordinal)!
      : [];

  /// <summary>The fixtures this package's writer made.</summary>
  private static IEnumerable<string> _Ours() => _Decoded().Where(n => n.StartsWith("ours_", StringComparison.Ordinal));

  /// <summary>The pixels of a reference decode: a PAM of RGB, RGB_ALPHA or GRAYSCALE samples, top row first.</summary>
  internal static (int Width, int Height, int Depth, byte[] Samples) Pam(byte[] data) {
    var text = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 256));
    var end = text.IndexOf("ENDHDR\n", StringComparison.Ordinal) + 7;
    int Field(string key) {
      var at = text.IndexOf(key + " ", StringComparison.Ordinal) + key.Length + 1;
      return int.Parse(text[at..text.IndexOf('\n', at)]);
    }

    var (width, height, depth) = (Field("WIDTH"), Field("HEIGHT"), Field("DEPTH"));
    return (width, height, depth, data.AsSpan(end, width * height * depth).ToArray());
  }

  /// <summary>Our frame as the reference decode lays it out: red, green, blue (and alpha), or one byte.</summary>
  internal static byte[] AsPamSamples(RawImage image) {
    switch (image.Format) {
      case PixelFormat.Bgra32: {
        var o = new byte[image.PixelData.Length];
        for (var i = 0; i < o.Length; i += 4) {
          o[i] = image.PixelData[i + 2];
          o[i + 1] = image.PixelData[i + 1];
          o[i + 2] = image.PixelData[i];
          o[i + 3] = image.PixelData[i + 3];
        }
        return o;
      }
      case PixelFormat.Bgr24: {
        var o = new byte[image.PixelData.Length];
        for (var i = 0; i < o.Length; i += 3) {
          o[i] = image.PixelData[i + 2];
          o[i + 1] = image.PixelData[i + 1];
          o[i + 2] = image.PixelData[i];
        }
        return o;
      }
      default:
        return image.PixelData;
    }
  }

  // ---------------------------------------------------------------- reading against the references

  [TestCaseSource(nameof(_Decoded))]
  [Category("Unit")]
  public void Decode_MatchesTheReferenceDecoder(string name) {
    var file = EriReader.FromBytes(_Fixture(name));
    var stem = Path.GetFileNameWithoutExtension(name);

    Assert.Multiple(() => {
      for (var frame = 0; ; ++frame) {
        var path = Path.Combine(_FixtureDirectory, $"{stem}_{frame}.pam");
        if (!File.Exists(path)) {
          Assert.That(EriFile.ImageCount(file), Is.EqualTo(frame), "frame count");
          break;
        }

        var expected = Pam(File.ReadAllBytes(path));
        var image = EriFile.ToRawImage(file, frame);
        Assert.That((image.Width, image.Height), Is.EqualTo((expected.Width, expected.Height)), $"frame {frame} size");
        Assert.That(AsPamSamples(image), Is.EqualTo(expected.Samples), $"frame {frame} pixels");

        var palette = Path.Combine(_FixtureDirectory, $"{stem}_{frame}.pal");
        if (File.Exists(palette)) {
          var bgrx = File.ReadAllBytes(palette);
          for (var i = 0; i < 256; ++i)
            Assert.That(
              (image.Palette![i * 3], image.Palette[i * 3 + 1], image.Palette[i * 3 + 2]),
              Is.EqualTo((bgrx[i * 4 + 2], bgrx[i * 4 + 1], bgrx[i * 4])), $"palette entry {i}");
        }
      }
    });
  }

  // ---------------------------------------------------------------- writing against the references

  /// <summary>
  /// Files this writer made, which the Entis libraries were given: the reference decode beside each
  /// is what they made of it, and the writer has to make the same file again.
  /// </summary>
  [TestCaseSource(nameof(_Ours))]
  [Category("Unit")]
  public void Encode_WritesTheFileTheReferenceDecodersRead(string name) {
    var original = EriReader.FromBytes(_Fixture(name));
    Assert.That(EriWriter.ToBytes(original), Is.EqualTo(_Fixture(name)));
  }

  [Test]
  [Category("Unit")]
  public void Decode_ReadsEightBitCommentAndCopyrightText() {
    var file = EriReader.FromBytes(_Fixture("sa_rgb24_desc_copyright.eri"));
    Assert.Multiple(() => {
      Assert.That(file.Description, Is.EqualTo("test description"));
      Assert.That(file.Copyright, Is.EqualTo("test copyright"));
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_NamesTheCodingOfEveryLevel() {
    Assert.Multiple(() => {
      Assert.That(EriFile.ReadImageInfo(_Fixture("na_rgb24_gamma_v20100_dep.eri"))!.Value.Compression, Is.EqualTo("ERI run-length gamma"));
      Assert.That(EriFile.ReadImageInfo(_Fixture("sa_rgb24_huffman_v20200.eri"))!.Value.Compression, Is.EqualTo("ERINA"));
      Assert.That(EriFile.ReadImageInfo(_Fixture("sa_rgb24_nemesis_v20200.eri"))!.Value.Compression, Is.EqualTo("ERISA"));
      Assert.That(EriFile.ReadImageInfo(_Fixture("sa_lossy_dct_444_rgb24.eri"))!.Value.Compression, Is.EqualTo("ERI lossy DCT"));
    });
  }

  // ---------------------------------------------------------------- detection and refusal

  [Test]
  [Category("Unit")]
  public void Detection_ClaimsTheExtensionsAndRecognisesTheBytes() {
    Assert.Multiple(() => {
      Assert.That(FormatRegistry.DetectFromExtension(".eri"), Is.EqualTo(ImageFormat.Eri));
      Assert.That(FormatRegistry.DetectFromExtension(".mei"), Is.EqualTo(ImageFormat.Eri));
      foreach (var name in _Decoded())
        Assert.That(FormatRegistry.DetectFromBytes(_Fixture(name)), Is.EqualTo(ImageFormat.Eri), name);
    });
  }

  private static RawImage _Picture(int width = 64, int height = 48) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)(x * 4);
        pixels[at + 1] = (byte)(y * 5);
        pixels[at + 2] = (byte)((x + y) * 3);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static IEnumerable<TestCaseData> _Foreign() {
    yield return new TestCaseData(JpegWriter.ToBytes(JpegFile.FromRawImage(_Picture()))).SetArgDisplayNames("jpeg");
    yield return new TestCaseData(PngWriter.ToBytes(PngFile.FromRawImage(_Picture()))).SetArgDisplayNames("png");
    yield return new TestCaseData(BmpWriter.ToBytes(BmpFile.FromRawImage(_Picture()))).SetArgDisplayNames("bmp");
  }

  [TestCaseSource(nameof(_Foreign))]
  [Category("Unit")]
  public void RefusalAudit_AnotherFormatUnderTheNamesIsRefused(byte[] foreign) {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => EriReader.FromBytes(foreign));
      Assert.That(FormatRegistry.DetectFromBytes(foreign), Is.Not.EqualTo(ImageFormat.Eri));
      Assert.That(EriFile.ReadImageInfo(foreign), Is.Null);
    });
  }

  /// <summary>Variants the libraries wrote that no available decoder reads back faithfully, refused by name.</summary>
  [TestCase("refused_lossy_gray.eri", "grey")]
  [TestCase("refused_lossy_filter_one_pixel.mei", "one pixel wide")]
  [TestCase("refused_lossy_filter_one_line.eri", "one line high")]
  [TestCase("refused_lossy_v9.eri", "version 9")]
  [TestCase("refused_version8_gamma.eri", "cannot read them back")]
  [Category("Unit")]
  public void UnverifiableVariants_AreRefusedByName(string name, string reason) {
    var data = _Fixture(name);
    Assert.Multiple(() => {
      Assert.That(Assert.Throws<NotSupportedException>(() => EriReader.FromBytes(data))!.Message, Does.Contain(reason));
      Assert.That(EriFile.ReadImageInfo(data), Is.Not.Null, "the header is still described");
    });
  }

  [Test]
  [Category("Unit")]
  public void ArithmeticCode_IsRefusedByName() {
    var data = _Written(_Picture(8, 8));
    BitConverter.GetBytes(32u).CopyTo(data, _Find(data, "ImageInf") + 8);
    Assert.That(Assert.Throws<NotSupportedException>(() => EriReader.FromBytes(data))!.Message, Does.Contain("arithmetic code"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => EriReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws()
    => Assert.Throws<FileNotFoundException>(() => EriReader.FromFile(new FileInfo("no-such-picture.eri")));

  private static byte[] _Written(RawImage image) => EriWriter.ToBytes(EriFile.FromRawImage(image));

  [Test]
  [Category("Unit")]
  public void Truncated_IsRefused() {
    var data = _Written(_Picture(40, 30));
    foreach (var keep in new[] { 8, 40, 64, 100, data.Length / 2, data.Length - 10 })
      Assert.That(() => EriReader.FromBytes(data[..keep]), Throws.InstanceOf<InvalidDataException>(), $"{keep} of {data.Length} bytes");
  }

  [Test]
  [Category("Unit")]
  public void ACorruptedHeaderIsRefusedByName() {
    var data = _Written(_Picture(8, 8));
    var info = _Find(data, "ImageInf");

    var wrongType = data.ToArray();
    wrongType[info + 12] = 7;
    var failure = Assert.Throws<NotSupportedException>(() => EriReader.FromBytes(wrongType));
    Assert.That(failure!.Message, Does.Contain("colour type"));

    var huge = data.ToArray();
    BitConverter.GetBytes(int.MaxValue).CopyTo(huge, info + 16);
    Assert.That(Assert.Throws<InvalidDataException>(() => EriReader.FromBytes(huge))!.Message, Does.Contain("size"));

    var badId = data.ToArray();
    badId[11] = 0x02;
    Assert.That(Assert.Throws<InvalidDataException>(() => EriReader.FromBytes(badId))!.Message, Does.Contain("identifier"));
  }

  private static int _Find(byte[] data, string id) {
    var pattern = Encoding.ASCII.GetBytes(id);
    for (var i = 0; i + pattern.Length <= data.Length; ++i)
      if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
        return i + 16;
    throw new InvalidOperationException(id);
  }

  // ---------------------------------------------------------------- header-only information

  [TestCaseSource(nameof(_Decoded))]
  [Category("Unit")]
  public void ReadImageInfo_AgreesWithAFullDecode(string name) {
    var data = _Fixture(name);
    var file = EriReader.FromBytes(data);
    var info = EriFile.ReadImageInfo(data);

    Assert.That(info, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(info!.Value.Width, Is.EqualTo(file.Width));
      Assert.That(info.Value.Height, Is.EqualTo(file.Height));
      Assert.That(info.Value.BitsPerPixel, Is.EqualTo(file.Info.BitsPerPixel));
      Assert.That(info.Value.ColorMode, Is.EqualTo(file.ColorType.ToString()));
      Assert.That(info.Value.Compression, Is.EqualTo(EriFile.CompressionName(file.Info)));
      Assert.That(info.Value.FrameCount, Is.EqualTo(EriFile.ImageCount(file)));
    });
  }

  // ---------------------------------------------------------------- round trips and edge geometry

  private static RawImage _Image(int width, int height, PixelFormat format, int seed) {
    var random = new Random(seed);
    var bytes = format switch { PixelFormat.Rgba32 => 4, PixelFormat.Rgb24 => 3, _ => 1 };
    var pixels = new byte[width * height * bytes];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % bytes * 60 + i / bytes % width * 3 + (random.Next(8) == 0 ? random.Next(256) : 0));

    if (format == PixelFormat.Indexed8) {
      var palette = new byte[256 * 3];
      random.NextBytes(palette);
      return new() { Width = width, Height = height, Format = format, PixelData = pixels, Palette = palette, PaletteCount = 256 };
    }

    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  [TestCase(1, 1)]
  [TestCase(1, 17)]
  [TestCase(17, 1)]
  [TestCase(15, 16)]
  [TestCase(16, 16)]
  [TestCase(17, 17)]
  [TestCase(33, 7)]
  [TestCase(100, 37)]
  [Category("Unit")]
  public void RoundTrip_IsLossless(int width, int height) {
    foreach (var format in new[] { PixelFormat.Rgb24, PixelFormat.Rgba32, PixelFormat.Gray8, PixelFormat.Indexed8 }) {
      var source = _Image(width, height, format, width * 7 + height);
      var back = EriFile.ToRawImage(EriReader.FromBytes(_Written(source)));

      Assert.Multiple(() => {
        Assert.That((back.Width, back.Height), Is.EqualTo((width, height)), format.ToString());
        if (format == PixelFormat.Indexed8) {
          Assert.That(back.PixelData, Is.EqualTo(source.PixelData), format.ToString());
          Assert.That(back.Palette, Is.EqualTo(source.Palette), format.ToString());
        } else
          Assert.That(back.EnsureFormat(format).PixelData, Is.EqualTo(source.PixelData), format.ToString());
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_KeepsEveryFrameOfAnAnimation() {
    var frames = Enumerable.Range(0, 4).Select(i => _Image(23, 19, PixelFormat.Rgb24, i).EnsureFormat(PixelFormat.Bgr24).PixelData).ToList();
    var file = EriFile.Create(23, 19, EriColorType.Rgb, frames);
    var back = EriReader.FromBytes(EriWriter.ToBytes(file));

    Assert.That(EriFile.ImageCount(back), Is.EqualTo(4));
    for (var i = 0; i < 4; ++i)
      Assert.That(back.Frames[i], Is.EqualTo(frames[i]), $"frame {i}");
  }

  [Test]
  [Category("Unit")]
  public void TheRegistry_WritesAndReadsAnyPicture() {
    var bytes = FormatRegistry.Write(_Picture(40, 30), ImageFormat.Eri);
    Assert.That(bytes, Is.Not.Null.And.Not.Empty);
    var back = FormatRegistry.Read(bytes!);
    Assert.That(back, Is.Not.Null);
    Assert.That((back!.Width, back.Height), Is.EqualTo((40, 30)));
  }

  // ---------------------------------------------------------------- metadata

  [Test]
  [Category("Unit")]
  public void CommentAndCopyright_SurviveReadWriteRead() {
    var image = _Picture(9, 9);
    var tagged = new RawImage {
      Width = image.Width, Height = image.Height, Format = image.Format, PixelData = image.PixelData,
      Metadata = new() { TextEntries = [new("Comment", "#hot-spot\r\n3,4\r\n恵理"), new("Copyright", "(C) nobody")] },
    };

    var first = EriReader.FromBytes(_Written(tagged));
    var second = EriReader.FromBytes(EriWriter.ToBytes(first));

    Assert.Multiple(() => {
      Assert.That(first.Description, Is.EqualTo("#hot-spot\r\n3,4\r\n恵理"));
      Assert.That(first.Copyright, Is.EqualTo("(C) nobody"));
      Assert.That(second.Description, Is.EqualTo(first.Description));
      Assert.That(second.Copyright, Is.EqualTo(first.Copyright));
      var entries = EriFile.ToRawImage(second).Metadata!.TextEntries;
      Assert.That(entries.Select(e => e.Keyword), Is.EquivalentTo(new[] { "Comment", "Copyright" }));
    });
  }

  [Test]
  [Category("Unit")]
  public void PaletteAndTransparentIndex_SurviveReadWriteRead() {
    var source = _Image(12, 5, PixelFormat.Indexed8, 3);
    var file = EriFile.FromRawImage(source);
    file = file with { Info = file.Info with { FormatType = file.Info.FormatType | EriInfoHeader.UseClipping, ClippedPixel = 7 } };

    var back = EriReader.FromBytes(EriWriter.ToBytes(EriReader.FromBytes(EriWriter.ToBytes(file))));
    var image = EriFile.ToRawImage(back);

    Assert.Multiple(() => {
      Assert.That(back.Palette, Is.EqualTo(source.Palette));
      Assert.That(back.Info.ClippedPixel, Is.EqualTo(7));
      Assert.That(image.AlphaTable![7], Is.EqualTo(0));
      Assert.That(image.AlphaTable.Count(a => a == 0), Is.EqualTo(1));
    });
  }

  // ---------------------------------------------------------------- the real-world corpus

  /// <summary>
  /// Every <c>.eri</c>/<c>.mei</c> in the directory <c>ERI_SAMPLES</c> names, against reference decodes.
  /// </summary>
  /// <remarks>
  /// Beside each sample go the reference decoder's frames as <c>name_N.pam</c> (and <c>name_N.pal</c>
  /// for a paletted one), or <c>name.refused</c> where no reference decoder reads it.
  /// </remarks>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs ERI_SAMPLES: a directory of .eri/.mei samples with reference decodes beside each.")]
  public void SampleCorpus_DecodesAsTheReferenceDoes() {
    var root = Environment.GetEnvironmentVariable("ERI_SAMPLES");
    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
      Assert.Inconclusive("Set ERI_SAMPLES to a directory of .eri samples.");

    var failures = new List<string>();
    var samples = Directory.EnumerateFiles(root!)
      .Where(p => p.EndsWith(".eri", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".mei", StringComparison.OrdinalIgnoreCase))
      .ToList();
    Assert.That(samples, Is.Not.Empty);

    foreach (var path in samples) {
      var stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
      try {
        var file = EriReader.FromBytes(File.ReadAllBytes(path));
        if (File.Exists(stem + ".refused")) {
          failures.Add($"{path}: no reference decoder reads it, this reader did");
          continue;
        }

        for (var frame = 0; File.Exists($"{stem}_{frame}.pam"); ++frame) {
          var expected = Pam(File.ReadAllBytes($"{stem}_{frame}.pam"));
          if (!AsPamSamples(EriFile.ToRawImage(file, frame)).AsSpan().SequenceEqual(expected.Samples))
            failures.Add($"{path}: frame {frame} differs from the reference decode");
        }
      } catch (Exception e) when (e is InvalidDataException or NotSupportedException) {
        if (!File.Exists(stem + ".refused"))
          failures.Add($"{path}: {e.GetType().Name}: {e.Message}");
      }
    }

    Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
  }
}
