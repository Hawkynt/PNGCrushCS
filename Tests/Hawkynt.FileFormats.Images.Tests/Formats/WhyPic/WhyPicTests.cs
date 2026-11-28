using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.Png;
using Hawkynt.FileFormats.Images;

namespace FileFormat.WhyPic.Tests;

/// <summary>
/// WhyPic held to the reference implementation: pictures its encoder wrote, the pixels its decoder
/// made of them, and the bytes its encoder writes for the same bitmap.
/// </summary>
/// <remarks>Fixtures\WhyPic\manifest.txt records how every fixture was produced.</remarks>
[TestFixture]
public sealed class WhyPicTests {

  private static readonly string[] _ReferenceWritten = [
    "ref11_1x1_16", "ref11_33x17_16", "ref10_33x17_256",
    "ref11_40x30_16_packed_palette", "ref10_40x30_16_packed_palette",
    "ref11_29x23_256_packed_palette", "ref10_29x23_256_packed_palette",
  ];

  private static readonly string[] _OursWritten = ["ours11_1x9_16", "ours11_4x4_16_noise", "ours10_80x50_256_noise"];

  private static IEnumerable<string> _AllFixtures() => _ReferenceWritten.Concat(_OursWritten);

  private static byte[] _Fixture(string name)
    => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "WhyPic", name));

  /// <summary>The palette (RGB) and top-down indices of a 4- or 8-bit bitmap as WhyPic writes one.</summary>
  private static (int Width, int Height, int Colors, byte[] Palette, byte[] Indices) _Bitmap(byte[] bmp) {
    var offset = BitConverter.ToInt32(bmp, 10);
    var header = BitConverter.ToInt32(bmp, 14);
    var width = BitConverter.ToInt32(bmp, 18);
    var height = BitConverter.ToInt32(bmp, 22);
    var bits = bmp[28];
    var colors = bits == 4 ? 16 : 256;

    var palette = new byte[colors * 3];
    for (var i = 0; i < colors; ++i) {
      var at = 14 + header + i * 4;
      palette[i * 3] = bmp[at + 2];
      palette[i * 3 + 1] = bmp[at + 1];
      palette[i * 3 + 2] = bmp[at];
    }

    var stride = ((bits == 4 ? (width + 1) / 2 : width) + 3) / 4 * 4;
    var indices = new byte[width * height];
    for (var y = 0; y < height; ++y) {
      var row = offset + (height - 1 - y) * stride;
      for (var x = 0; x < width; ++x)
        indices[y * width + x] = bits == 8
          ? bmp[row + x]
          : (byte)((x & 1) == 0 ? bmp[row + x / 2] >> 4 : bmp[row + x / 2] & 0x0F);
    }

    return (width, height, colors, palette, indices);
  }

  private static WhyPicFile _FromBitmap(byte[] bmp, WhyPicLayout layout) {
    var (width, height, colors, palette, indices) = _Bitmap(bmp);
    return new() { Width = width, Height = height, Colors = colors, Layout = layout, Palette = palette, PixelData = indices };
  }

  private static WhyPicLayout _LayoutOf(string name) => name.Contains("11_") ? WhyPicLayout.Version11 : WhyPicLayout.Version10;

  // ---------------------------------------------------------------- reading against the reference

  [TestCaseSource(nameof(_AllFixtures))]
  [Category("Unit")]
  public void Decode_MatchesTheReferenceDecoder(string name) {
    var file = WhyPicReader.FromBytes(_Fixture(name + ".ypc"));
    var expected = _Bitmap(_Fixture(name + ".bmp"));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(expected.Width));
      Assert.That(file.Height, Is.EqualTo(expected.Height));
      Assert.That(file.Colors, Is.EqualTo(expected.Colors));
      Assert.That(file.Layout, Is.EqualTo(_LayoutOf(name)));
      Assert.That(file.Palette, Is.EqualTo(expected.Palette), "palette");
      Assert.That(file.PixelData, Is.EqualTo(expected.Indices), "pixels");
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_ReportsWhichPaletteCodingWasUsed() {
    Assert.Multiple(() => {
      Assert.That(WhyPicReader.FromBytes(_Fixture("ref11_33x17_16.ypc")).PaletteCompressed, Is.False);
      Assert.That(WhyPicReader.FromBytes(_Fixture("ref11_40x30_16_packed_palette.ypc")).PaletteCompressed, Is.True);
      Assert.That(WhyPicReader.FromBytes(_Fixture("ref10_29x23_256_packed_palette.ypc")).PaletteCompressed, Is.True);
    });
  }

  // ---------------------------------------------------------------- writing against the reference

  /// <summary>The writer makes the reference encoder's choices, so for the same bitmap it writes the same bytes.</summary>
  [TestCaseSource(nameof(_ReferenceWritten))]
  [Category("Unit")]
  public void Encode_WritesWhatTheReferenceEncoderWrites(string name) {
    var source = _FromBitmap(_Fixture(name + ".bmp"), _LayoutOf(name));
    Assert.That(WhyPicWriter.ToBytes(source), Is.EqualTo(_Fixture(name + ".ypc")));
  }

  /// <summary>
  /// Where the reference encoder fails its own verification, the writer's file is the one the
  /// reference decoder was given, and the bitmap beside it is what that decoder made of it.
  /// </summary>
  [TestCaseSource(nameof(_OursWritten))]
  [Category("Unit")]
  public void Encode_WritesTheFileTheReferenceDecoderRead(string name) {
    var source = _FromBitmap(_Fixture(name + ".bmp"), _LayoutOf(name));
    Assert.That(WhyPicWriter.ToBytes(source), Is.EqualTo(_Fixture(name + ".ypc")));
  }

  // ---------------------------------------------------------------- detection and refusal

  [Test]
  [Category("Unit")]
  public void Detection_ClaimsTheExtensionAndRecognisesTheBytes() {
    Assert.Multiple(() => {
      Assert.That(FormatRegistry.DetectFromExtension(".ypc"), Is.EqualTo(ImageFormat.WhyPic));
      foreach (var name in _AllFixtures())
        Assert.That(FormatRegistry.DetectFromBytes(_Fixture(name + ".ypc")), Is.EqualTo(ImageFormat.WhyPic), name);
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
  public void RefusalAudit_AnotherFormatUnderTheNameIsRefused(byte[] foreign) {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => WhyPicReader.FromBytes(foreign));
      Assert.That(FormatRegistry.DetectFromBytes(foreign), Is.Not.EqualTo(ImageFormat.WhyPic));
      Assert.That(WhyPicFile.ReadImageInfo(foreign), Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => WhyPicReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws()
    => Assert.Throws<FileNotFoundException>(() => WhyPicReader.FromFile(new FileInfo("no-such-picture.ypc")));

  [TestCase(1)]
  [TestCase(2)]
  [TestCase(5)]
  [TestCase(50)]
  [Category("Unit")]
  public void Truncated_IsRefused(int missing) {
    foreach (var name in _AllFixtures()) {
      var data = _Fixture(name + ".ypc");
      if (data.Length - missing < 2)
        continue;
      Assert.Throws<InvalidDataException>(() => WhyPicReader.FromBytes(data[..^missing]), name);
    }
  }

  /// <summary>A changed pixel byte decodes to some other picture, whose own code rarely ends the way the file does.</summary>
  /// <remarks>
  /// An arithmetic code has no redundancy to check: every run of bits decodes to some sequence of
  /// colours, and that sequence codes back to the same bits — up to the end, where the encoder
  /// closes the interval in one particular way. That closing and the stream running dry early are
  /// all the reader has to go on, so damage among the pixels is refused nearly always, not always.
  /// A changed palette byte, which is stored evenly spread like a plain byte, cannot be told from a
  /// different palette at all, so damage is only made where the pixels are.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void CorruptedPixels_AreAlmostAlwaysRefused() {
    var tried = 0;
    var wrong = 0;
    foreach (var name in _AllFixtures()) {
      var data = _Fixture(name + ".ypc");
      var original = WhyPicReader.FromBytes(data).PixelData;
      var start = data.Length * 2 / 3;
      for (var at = start; at < data.Length; at += Math.Max(1, (data.Length - start) / 30)) {
        var damaged = data.ToArray();
        damaged[at] ^= 0x5A;
        ++tried;
        try {
          if (!WhyPicReader.FromBytes(damaged).PixelData.SequenceEqual(original))
            ++wrong;
        } catch (Exception e) when (e is InvalidDataException or NotSupportedException) {
        }
      }
    }

    TestContext.Out.WriteLine($"{wrong} of {tried} damaged files read as a different picture");
    Assert.That(wrong, Is.LessThanOrEqualTo(tried / 10), $"{wrong} of {tried} damaged files read as a different picture");
  }

  /// <summary>Old transfers padded files to a block; whatever follows the code is not part of it.</summary>
  [Test]
  [Category("Unit")]
  public void TrailingPadding_IsIgnored() {
    var data = _Fixture("ref11_40x30_16_packed_palette.ypc");
    var padded = data.Concat(Enumerable.Repeat((byte)0x1A, 100)).ToArray();
    Assert.That(WhyPicReader.FromBytes(padded).PixelData, Is.EqualTo(WhyPicReader.FromBytes(data).PixelData));
  }

  [Test]
  [Category("Unit")]
  public void TheTwoLettersAlone_AreRefused()
    => Assert.Throws<InvalidDataException>(() => WhyPicReader.FromBytes("Yp"u8.ToArray()));

  private static byte[] _Header(Action<WhyPicRangeEncoder> fields) {
    var encoder = new WhyPicRangeEncoder();
    fields(encoder);
    var code = encoder.Finish();
    return [(byte)'Y', (byte)'p', .. code, 0, 0, 0, 0, 0, 0, 0, 0];
  }

  [Test]
  [Category("Unit")]
  public void ZeroSize_IsRefused() {
    var data = _Header(e => { e.Uniform(0, 4096); e.Uniform(17, 4096); e.Uniform(0, 2); e.Uniform(0, 2); });
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => WhyPicReader.FromBytes(data));
      Assert.That(WhyPicFile.ReadImageInfo(data), Is.Null);
    });
  }

  [TestCase(0, 1)]
  [TestCase(1, 0)]
  [TestCase(1, 1)]
  [Category("Unit")]
  public void ReservedVersionCodes_AreRefusedByName(int second, int third) {
    var data = _Header(e => {
      e.Uniform(8, 4096);
      e.Uniform(8, 4096);
      e.Uniform(1, 2);
      e.Uniform(second, 2);
      e.Uniform(third, 2);
      e.Uniform(0, 2);
    });

    var failure = Assert.Throws<NotSupportedException>(() => WhyPicReader.FromBytes(data));
    Assert.That(failure!.Message, Does.Contain("reserved"));
  }

  [Test]
  [Category("Unit")]
  public void OversizedPicture_IsRefusedByTheWriter() {
    var image = new RawImage {
      Width = WhyPicFile.MaxDimension + 1, Height = 1, Format = PixelFormat.Gray8,
      PixelData = new byte[WhyPicFile.MaxDimension + 1],
    };
    Assert.Throws<ArgumentException>(() => WhyPicFile.FromRawImage(image));
  }

  // ---------------------------------------------------------------- header-only information

  [TestCaseSource(nameof(_AllFixtures))]
  [Category("Unit")]
  public void ReadImageInfo_AgreesWithAFullDecode(string name) {
    var data = _Fixture(name + ".ypc");
    var file = WhyPicReader.FromBytes(data);
    var info = WhyPicFile.ReadImageInfo(data.AsSpan(0, Math.Min(data.Length, 16)));

    Assert.That(info, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(info!.Value.Width, Is.EqualTo(file.Width));
      Assert.That(info.Value.Height, Is.EqualTo(file.Height));
      Assert.That(info.Value.BitsPerPixel, Is.EqualTo(file.Colors == 16 ? 4 : 8));
      Assert.That(info.Value.Compression, Is.EqualTo(file.Layout == WhyPicLayout.Version11 ? "WhyPic 1.1" : "WhyPic 1.0"));
      Assert.That(info.Value.FrameCount, Is.EqualTo(1));
    });
  }

  // ---------------------------------------------------------------- round trips and edge geometry

  private static RawImage _Indexed(int width, int height, int colors, int seed) {
    var random = new Random(seed);
    var palette = new byte[colors * 3];
    random.NextBytes(palette);
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)((i / 3 + (i % width) / 2 + (random.Next(10) == 0 ? random.Next(colors) : 0)) % colors);

    return new() {
      Width = width, Height = height, Format = PixelFormat.Indexed8,
      PixelData = pixels, Palette = palette, PaletteCount = colors,
    };
  }

  [TestCase(1, 1, 16)]
  [TestCase(1, 1, 256)]
  [TestCase(33, 17, 16)]
  [TestCase(33, 17, 256)]
  [TestCase(1, 23, 16)]
  [TestCase(23, 1, 256)]
  [TestCase(2, 2, 16)]
  [TestCase(7, 5, 256)]
  [TestCase(64, 40, 16)]
  [Category("Unit")]
  public void RoundTrip_IsLossless(int width, int height, int colors) {
    var source = _Indexed(width, height, colors, width * 31 + height);
    foreach (var layout in new[] { WhyPicLayout.Version10, WhyPicLayout.Version11 }) {
      var file = WhyPicFile.FromRawImage(source) with { Layout = layout };
      var back = WhyPicFile.ToRawImage(WhyPicReader.FromBytes(WhyPicWriter.ToBytes(file)));

      Assert.Multiple(() => {
        Assert.That(back.Width, Is.EqualTo(width), layout.ToString());
        Assert.That(back.Height, Is.EqualTo(height), layout.ToString());
        Assert.That(
          PixelConverter.Convert(back, PixelFormat.Rgb24).PixelData,
          Is.EqualTo(PixelConverter.Convert(source, PixelFormat.Rgb24).PixelData), layout.ToString());
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_FromTrueColourWithFewColours_KeepsEveryPixel() {
    var rgb = new byte[19 * 11 * 3];
    for (var i = 0; i < 19 * 11; ++i) {
      var colour = (i * 7 / 5) % 12;
      rgb[i * 3] = (byte)(colour * 20);
      rgb[i * 3 + 1] = (byte)(255 - colour * 9);
      rgb[i * 3 + 2] = (byte)(colour * colour);
    }

    var source = new RawImage { Width = 19, Height = 11, Format = PixelFormat.Rgb24, PixelData = rgb };
    var bytes = WhyPicWriter.ToBytes(WhyPicFile.FromRawImage(source));
    var file = WhyPicReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(file.Colors, Is.EqualTo(16), "twelve colours are written as a 16-colour picture");
      Assert.That(PixelConverter.Convert(WhyPicFile.ToRawImage(file), PixelFormat.Rgb24).PixelData, Is.EqualTo(rgb));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheRegistry_WritesAndReadsAnyPicture() {
    var bytes = FormatRegistry.Write(_Picture(40, 30), ImageFormat.WhyPic);
    Assert.That(bytes, Is.Not.Null.And.Not.Empty);
    var back = FormatRegistry.Read(bytes!);
    Assert.That(back, Is.Not.Null);
    Assert.That((back!.Width, back.Height), Is.EqualTo((40, 30)));
  }

  // ---------------------------------------------------------------- metadata

  /// <summary>The palette is the only thing beside the pixels the format holds, and the coding version.</summary>
  [TestCaseSource(nameof(_AllFixtures))]
  [Category("Unit")]
  public void PaletteAndCoding_SurviveReadWriteRead(string name) {
    var first = WhyPicReader.FromBytes(_Fixture(name + ".ypc"));
    var second = WhyPicReader.FromBytes(WhyPicWriter.ToBytes(first));

    Assert.Multiple(() => {
      Assert.That(second.Palette, Is.EqualTo(first.Palette));
      Assert.That(second.Layout, Is.EqualTo(first.Layout));
      Assert.That(second.Colors, Is.EqualTo(first.Colors));
      Assert.That(second.PixelData, Is.EqualTo(first.PixelData));
    });
  }

  // ---------------------------------------------------------------- the real-world corpus

  /// <summary>
  /// Every <c>.ypc</c> in the directory <c>WHYPIC_SAMPLES</c> names, against the reference decoder.
  /// </summary>
  /// <remarks>
  /// Beside each sample goes either <c>name.bmp</c>, the reference decoder's output, or
  /// <c>name.refused</c> where the reference decoder rejected it; this reader must agree.
  /// </remarks>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs WHYPIC_SAMPLES: a directory of .ypc samples with the reference decoder's .bmp or a .refused marker beside each.")]
  public void SampleCorpus_DecodesAsTheReferenceDoes() {
    var root = Environment.GetEnvironmentVariable("WHYPIC_SAMPLES");
    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
      Assert.Inconclusive("Set WHYPIC_SAMPLES to a directory of .ypc samples.");

    var failures = new List<string>();
    var samples = Directory.EnumerateFiles(root!).Where(p => p.EndsWith(".ypc", StringComparison.OrdinalIgnoreCase)).ToList();
    Assert.That(samples, Is.Not.Empty);

    foreach (var path in samples) {
      var stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
      var data = File.ReadAllBytes(path);
      if (File.Exists(stem + ".refused")) {
        try {
          WhyPicReader.FromBytes(data);
          failures.Add($"{path}: the reference refused it, this reader did not");
        } catch (Exception e) when (e is InvalidDataException or NotSupportedException) {
        }
        continue;
      }

      try {
        var file = WhyPicReader.FromBytes(data);
        var expected = _Bitmap(File.ReadAllBytes(stem + ".bmp"));
        if (!file.Palette.SequenceEqual(expected.Palette) || !file.PixelData.SequenceEqual(expected.Indices))
          failures.Add($"{path}: differs from the reference decode");
      } catch (Exception e) {
        failures.Add($"{path}: {e.GetType().Name}: {e.Message}");
      }
    }

    Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
  }
}
