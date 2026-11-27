using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using FileFormat.Core;
using FileFormat.Pax;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Pax.Tests;

/// <summary>Pick Ax pictures, held to files Smaller Animals' own libraries wrote.</summary>
/// <remarks>
/// The fixtures are described in Fixtures/PROVENANCE.txt. The writer was held to the same libraries
/// outside this suite — PAXIO.dll read every type it knows (1 to 3) back exactly, and ImgSource 5
/// read all six types, paletted and RGB, identical to the source everywhere outside the cross its
/// unregistered build stamps on every decode.
/// </remarks>
[TestFixture]
public sealed class PaxTests {

  private const string _CORPUS_VARIABLE = "PAX_CORPUS";

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Pax", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  private static (int Width, int Height, byte[] Rgb) _Ppm(byte[] data) {
    var fields = new List<string>();
    var position = 0;
    while (fields.Count < 4) {
      while (char.IsWhiteSpace((char)data[position]))
        ++position;
      var start = position;
      while (!char.IsWhiteSpace((char)data[position]))
        ++position;
      fields.Add(Encoding.ASCII.GetString(data, start, position - start));
    }

    var width = int.Parse(fields[1]);
    var height = int.Parse(fields[2]);
    return (width, height, data.AsSpan(position + 1, width * height * 3).ToArray());
  }

  private static byte[] _Rgb(PaxFile file) => PaxFile.ToRawImage(file).EnsureFormat(PixelFormat.Rgb24).PixelData;

  // ==========================================================================================
  // Files Smaller Animals wrote
  // ==========================================================================================

  [TestCase("paxio_blowfish.pax", PaxEncryption.Blowfish)]
  [TestCase("paxio_blowfishcbc.pax", PaxEncryption.BlowfishCbc)]
  [TestCase("paxio_sapphire.pax", PaxEncryption.Sapphire)]
  [Category("Conformance")]
  public void PaxioWrittenFile_DecodesToThePictureItWasGiven(string name, PaxEncryption encryption) {
    var file = PaxReader.FromBytes(_Fixture(name), "password");
    var (width, height, expected) = _Ppm(_Fixture("rgb_16x4.ppm"));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
      Assert.That(file.ColorMode, Is.EqualTo(PaxColorMode.Rgb24));
      Assert.That(file.Encryption, Is.EqualTo(encryption));
      Assert.That(file.ApplicationName, Is.EqualTo("PaxIO DLL"));
      Assert.That(_Rgb(file), Is.EqualTo(expected));
    });
  }

  [Test]
  [Category("Conformance")]
  public void ImgSourceWrittenRgbFiles_DecodeWithTheirTagsAndNames([Range(1, 6)] int type) {
    var file = PaxReader.FromBytes(_Fixture($"imgsource_rgb_type{type}.pax"), $"pass word{type}");
    var (_, _, expected) = _Ppm(_Fixture("rgb_16x4.ppm"));

    Assert.Multiple(() => {
      Assert.That(file.Encryption, Is.EqualTo((PaxEncryption)type));
      Assert.That(file.ApplicationName, Is.EqualTo("TestApp"));
      Assert.That(file.AuthorName, Is.EqualTo("TestAuthor"));
      Assert.That(file.Date, Is.EqualTo(new DateOnly(2026, 11, 27)));
      Assert.That(file.FileId, Is.EqualTo(Enumerable.Range(0, 128).Select(static i => (byte)(i * 7)).ToArray()));
      Assert.That(file.Tags.Select(static t => t.Name), Is.EqualTo(new[] { "TAG1", "ZZZZ" }));
      Assert.That(file.Tags[0].Data, Is.EqualTo("hello tag\0\0\0\0\0\0\0"u8.ToArray()), "ImgSource states and stores the padded size");
      Assert.That(file.Tags[1].Data, Is.EqualTo("0123456789abcdef"u8.ToArray()));
      Assert.That(_Rgb(file), Is.EqualTo(expected));
    });
  }

  [Test]
  [Category("Conformance")]
  public void ImgSourceWrittenIndexedFiles_DecodeThroughTheirPalette([Range(1, 6)] int type) {
    var file = PaxReader.FromBytes(_Fixture($"imgsource_indexed_type{type}.pax"), $"pw{type}");
    var (width, height, expected) = _Ppm(_Fixture("indexed_13x5.ppm"));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
      Assert.That(file.ColorMode, Is.EqualTo(PaxColorMode.Indexed8));
      Assert.That(file.PaletteCount, Is.EqualTo(256));
      Assert.That(file.PixelData, Is.EqualTo(Enumerable.Range(0, width * height).Select(i => (byte)(5 * (i % width) + 17 * (i / width))).ToArray()));
      Assert.That(_Rgb(file), Is.EqualTo(expected));
    });
  }

  // ==========================================================================================
  // The password
  // ==========================================================================================

  [Test]
  [Category("Unit")]
  public void WithoutItsPassword_TheFileIsRefusedAsPasswordProtected() {
    var data = _Fixture("paxio_blowfish.pax");
    var exception = Assert.Throws<InvalidDataException>(() => PaxReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("password-protected"));
  }

  [Test]
  [Category("Unit")]
  public void WithTheWrongPassword_TheFileIsRefusedAsPasswordProtected() {
    var data = _Fixture("paxio_blowfish.pax");
    var exception = Assert.Throws<InvalidDataException>(() => PaxReader.FromBytes(data, "passwort"));
    Assert.That(exception!.Message, Does.Contain("password-protected"));
  }

  [Test]
  [Category("Unit")]
  public void IsPassword_TellsTheRightPasswordFromAWrongOne() {
    var data = _Fixture("imgsource_rgb_type6.pax");
    Assert.Multiple(() => {
      Assert.That(PaxReader.IsPassword(data, "pass word6"), Is.True);
      Assert.That(PaxReader.IsPassword(data, "pass word5"), Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void KeyDerivation_IsTheSapphireHashOfThePasswordCutToSevenBytes() {
    // Measured against PAXIO.dll: these seven bytes decrypt "password" files, and with a zero byte
    // appended they are the CBC chaining value.
    Assert.Multiple(() => {
      Assert.That(Convert.ToHexString(PaxCipher.DeriveKey("password")), Is.EqualTo("A948F1269FE8A0"));
      Assert.That(Convert.ToHexString(PaxCipher.DeriveKey(string.Empty)), Is.EqualTo("C1E0DF6CE706A3"));
    });
  }

  // ==========================================================================================
  // Blowfish
  // ==========================================================================================

  [Test]
  [Category("Unit")]
  public void BlowfishTables_AreTheHexadecimalDigitsOfPi() {
    const int words = 18 + 4 * 256;
    const int guard = 64;
    var one = BigInteger.One << (32 * words + guard);
    var pi = 4 * (4 * _ArcTanInverse(5, one) - _ArcTanInverse(239, one));
    var fraction = (pi - 3 * one) >> guard;

    var tables = PaxBlowfish.InitialTables();
    var mask = new BigInteger(uint.MaxValue);
    for (var i = 0; i < words; ++i) {
      var expected = (uint)((fraction >> (32 * (words - 1 - i))) & mask);
      if (tables[i] != expected)
        Assert.Fail($"Word {i} is {tables[i]:X8}, pi says {expected:X8}.");
    }
  }

  private static BigInteger _ArcTanInverse(int x, BigInteger one) {
    var term = one / x;
    var total = term;
    var square = x * x;
    for (var k = 1; !term.IsZero; ++k) {
      term /= square;
      var part = term / (2 * k + 1);
      total += (k & 1) == 1 ? -part : part;
    }

    return total;
  }

  [TestCase("0000000000000000", "0000000000000000", "4EF997456198DD78")]
  [TestCase("FFFFFFFFFFFFFFFF", "FFFFFFFFFFFFFFFF", "51866FD5B85ECB8A")]
  [TestCase("3000000000000000", "1000000000000001", "7D856F9A613063F2")]
  [TestCase("0123456789ABCDEF", "1111111111111111", "61F9C3802281B096")]
  [TestCase("FEDCBA9876543210", "0123456789ABCDEF", "0ACEAB0FC6A0A28D")]
  [Category("Unit")]
  public void Blowfish_MatchesEricYoungsTestVectors(string key, string plain, string cipher) {
    var blowfish = new PaxBlowfish(Convert.FromHexString(key));
    var block = Convert.FromHexString(plain);
    blowfish.EncryptBlock(block);
    Assert.That(Convert.ToHexString(block), Is.EqualTo(cipher));
    blowfish.DecryptBlock(block);
    Assert.That(Convert.ToHexString(block), Is.EqualTo(plain));
  }

  // ==========================================================================================
  // Writing
  // ==========================================================================================

  private static PaxFile _Sample(PaxColorMode mode, PaxEncryption encryption, int width = 19, int height = 7) {
    var random = new Random(1234 + (int)mode * 10 + (int)encryption);
    var pixels = new byte[width * height * (int)mode];
    random.NextBytes(pixels);
    if (mode == PaxColorMode.Rgb555)
      for (var i = 1; i < pixels.Length; i += 2)
        pixels[i] &= 0x7F;

    var palette = new byte[256 * 3];
    random.NextBytes(palette);
    var id = new byte[PaxFile.FileIdSize];
    random.NextBytes(id);

    return new() {
      Width = width,
      Height = height,
      ColorMode = mode,
      Encryption = encryption,
      Password = "sésame ouvre-toi",
      FileId = id,
      ApplicationName = "PNGCrushCS",
      AuthorName = "Tester",
      Date = new DateOnly(1999, 8, 4),
      Palette = mode == PaxColorMode.Indexed8 ? palette : null,
      PaletteCount = mode == PaxColorMode.Indexed8 ? 256 : 0,
      Tags = [new("NOTE", "eight by"u8.ToArray()), new("XYZW", [1, 2, 3])],
      PixelData = pixels,
    };
  }

  [Test]
  [Category("Unit")]
  public void WrittenFile_ReadsBackUnchanged(
    [Values(PaxColorMode.Indexed8, PaxColorMode.Rgb555, PaxColorMode.Rgb24)] PaxColorMode mode,
    [Values] PaxEncryption encryption) {
    var original = _Sample(mode, encryption);
    var again = PaxReader.FromBytes(PaxWriter.ToBytes(original), original.Password);

    Assert.Multiple(() => {
      Assert.That(again.Width, Is.EqualTo(original.Width));
      Assert.That(again.Height, Is.EqualTo(original.Height));
      Assert.That(again.ColorMode, Is.EqualTo(mode));
      Assert.That(again.Encryption, Is.EqualTo(encryption));
      Assert.That(again.FileId, Is.EqualTo(original.FileId));
      Assert.That(again.ApplicationName, Is.EqualTo(original.ApplicationName));
      Assert.That(again.AuthorName, Is.EqualTo(original.AuthorName));
      Assert.That(again.Date, Is.EqualTo(original.Date));
      Assert.That(again.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(again.PaletteCount, Is.EqualTo(original.PaletteCount));
      Assert.That(again.Palette, Is.EqualTo(original.Palette));
      Assert.That(again.Tags[0].Data, Is.EqualTo(original.Tags[0].Data));
      Assert.That(again.Tags[1].Data, Is.EqualTo(new byte[] { 1, 2, 3, 0, 0, 0, 0, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void WrittenFile_LeavesTheTagAndIdentifierInTheClearAndEncryptsTheRest() {
    var original = _Sample(PaxColorMode.Rgb24, PaxEncryption.Blowfish);
    var bytes = PaxWriter.ToBytes(original);

    Assert.Multiple(() => {
      Assert.That(bytes[..3], Is.EqualTo("PAX"u8.ToArray()));
      Assert.That(bytes[3..131], Is.EqualTo(original.FileId));
      Assert.That(Encoding.Latin1.GetString(bytes).Contains("PNGCrushCS"), Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void OddPaletteCount_IsStoredEvenSoItFillsWholeBlocks() {
    var original = _Sample(PaxColorMode.Indexed8, PaxEncryption.BlowfishCbc) with {
      Palette = Enumerable.Range(0, 9).Select(static i => (byte)(i * 20)).ToArray(),
      PaletteCount = 3,
      PixelData = Enumerable.Range(0, 19 * 7).Select(static i => (byte)(i % 3)).ToArray(),
    };

    var again = PaxReader.FromBytes(PaxWriter.ToBytes(original), original.Password);
    Assert.Multiple(() => {
      Assert.That(again.PaletteCount, Is.EqualTo(4));
      Assert.That(again.Palette![..9], Is.EqualTo(original.Palette));
      Assert.That(again.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Rgb555_ExpandsFiveBitsByRepeatingTheTopBits() {
    var file = new PaxFile {
      Width = 2, Height = 1, ColorMode = PaxColorMode.Rgb555,
      PixelData = [0xFF, 0x7F, 0x21, 0x04], // white; R=1, G=1, B=1
    };

    Assert.That(_Rgb(file), Is.EqualTo(new byte[] { 255, 255, 255, 8, 8, 8 }));
  }

  // ==========================================================================================
  // Through the registry
  // ==========================================================================================

  [Test]
  [Category("Unit")]
  public void Registry_DetectsAPaxByItsTag() {
    Assert.That(FormatRegistry.DetectFromBytes(_Fixture("paxio_sapphire.pax")), Is.EqualTo(ImageFormat.Pax));
  }

  [Test]
  [Category("Unit")]
  public void Registry_WritesUnderTheEmptyPasswordAndReadsItBack() {
    var image = new RawImage {
      Width = 5, Height = 3, Format = PixelFormat.Rgb24,
      PixelData = Enumerable.Range(0, 45).Select(static i => (byte)(i * 5)).ToArray(),
    };

    var bytes = PaxWriter.ToBytes(PaxFile.FromRawImage(image));
    var back = PaxFile.ToRawImage(PaxReader.FromBytes(bytes));
    Assert.That(back.PixelData, Is.EqualTo(image.PixelData));
  }

  [TestCase(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F' }, TestName = "JpegRenamedToPax_IsRefused")]
  [TestCase(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }, TestName = "PngRenamedToPax_IsRefused")]
  [TestCase(new byte[] { (byte)'B', (byte)'M', 0x3A, 0, 0, 0, 0, 0, 0, 0, 0x36, 0, 0, 0 }, TestName = "BmpRenamedToPax_IsRefused")]
  [Category("Unit")]
  public void OtherFormatsRenamedToPax_AreRefused(byte[] head) {
    var data = new byte[512];
    head.CopyTo(data, 0);
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => PaxReader.FromBytes(data));
      Assert.That(FormatRegistry.DetectFromBytes(data), Is.Not.EqualTo(ImageFormat.Pax));
    });
  }

  [Test]
  [Category("Unit")]
  public void TruncatedFile_IsRefused() {
    var data = _Fixture("paxio_blowfish.pax");
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => PaxReader.FromBytes(data[..100], "password"));
      Assert.Throws<InvalidDataException>(() => PaxReader.FromBytes(data[..^9], "password"));
    });
  }

  // ==========================================================================================
  // A corpus, when one is at hand
  // ==========================================================================================

  /// <summary>
  /// Reads every <c>.pax</c> under <c>PAX_CORPUS</c>. A <c>name.pax.password</c> beside a file
  /// holds its password (else "password" is tried); a <c>name.ppm</c> beside it, when present,
  /// is the picture an oracle decoded from it and the decode has to match it exactly.
  /// </summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs a directory of PAX files in PAX_CORPUS.")]
  public void Corpus_EveryFileDecodes() {
    var directory = Environment.GetEnvironmentVariable(_CORPUS_VARIABLE);
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
      Assert.Inconclusive($"Set {_CORPUS_VARIABLE} to a directory of .pax files.");

    var failures = new List<string>();
    var count = 0;
    foreach (var path in Directory.EnumerateFiles(directory!, "*.pax", SearchOption.AllDirectories)) {
      ++count;
      var passwordFile = path + ".password";
      var password = File.Exists(passwordFile) ? File.ReadAllText(passwordFile).TrimEnd('\r', '\n') : "password";
      try {
        var file = PaxReader.FromBytes(File.ReadAllBytes(path), password);
        var expectedPath = Path.ChangeExtension(path, ".ppm");
        if (File.Exists(expectedPath)) {
          var (width, height, rgb) = _Ppm(File.ReadAllBytes(expectedPath));
          if (width != file.Width || height != file.Height || !_Rgb(file).AsSpan().SequenceEqual(rgb))
            failures.Add($"{path}: differs from {expectedPath}");
        }
      } catch (Exception exception) {
        failures.Add($"{path}: {exception.Message}");
      }
    }

    Assert.That(failures, Is.Empty, $"{failures.Count} of {count} files failed:\n" + string.Join("\n", failures));
  }
}
