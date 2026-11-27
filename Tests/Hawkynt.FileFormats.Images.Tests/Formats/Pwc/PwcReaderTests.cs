using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Pwc;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Pwc.Tests;

[TestFixture]
public sealed class PwcReaderTests {

  private static IEnumerable<string> FixtureNames() => PwcFixtures.Names();

  [TestCaseSource(nameof(FixtureNames))]
  [Category("Unit")]
  public void Fixture_DecodesToWhatPwcdecodeWrote(string name) {
    var file = PwcReader.FromBytes(PwcFixtures.Pwc(name));
    var reference = PwcFixtures.Bmp(name);

    Assert.That(file.Width, Is.EqualTo(reference.Width));
    Assert.That(file.Height, Is.EqualTo(reference.Height));
    Assert.That(file.PixelData, Is.EqualTo(reference.Indices), "indices differ from pwcdecode's decode");
    Assert.That(file.Palette, Is.EqualTo(reference.Palette.AsSpan(0, file.Palette.Length).ToArray()), "palette differs");
  }

  [TestCaseSource(nameof(FixtureNames))]
  [Category("Unit")]
  public void Fixture_HeaderInfoAgreesWithFullDecode(string name) {
    var bytes = PwcFixtures.Pwc(name);
    var info = PwcFile.ReadImageInfo(bytes.AsSpan(0, 12));
    var file = PwcReader.FromBytes(bytes);

    Assert.That(info, Is.Not.Null);
    Assert.That(info!.Value.Width, Is.EqualTo(file.Width));
    Assert.That(info.Value.Height, Is.EqualTo(file.Height));
    Assert.That(info.Value.BitsPerPixel, Is.EqualTo(file.BitsPerPixel));
    Assert.That(info.Value.FrameCount, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Fixture_OptionsAreReadFromTheCodingBlock() {
    Assert.That(PwcReader.FromBytes(PwcFixtures.Pwc("d8_40_shapes__noq2")).CodingFlags,
      Is.EqualTo(PwcCodingFlags.NoCornerGuesses | PwcCodingFlags.NoListDecision));
    Assert.That(PwcReader.FromBytes(PwcFixtures.Pwc("d8_256_shapes__default")).CodingFlags, Is.EqualTo(PwcCodingFlags.None));
    Assert.That(PwcReader.FromBytes(PwcFixtures.Pwc("d8_gray__gray")).CodingFlags, Is.EqualTo(PwcCodingFlags.Gray));
    Assert.That(PwcReader.FromBytes(PwcFixtures.Pwc("d4_5_shapes__lps3")).MaxLps, Is.EqualTo(3));
    Assert.That(PwcReader.FromBytes(PwcFixtures.Pwc("d1_9x3__flip")).BottomUp, Is.True);
    Assert.That(PwcReader.FromBytes(PwcFixtures.Pwc("d1_shapes__default")).BottomUp, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RealFile_IsDetectedBySignature() {
    Assert.That(FormatRegistry.DetectFromBytes(PwcFixtures.Pwc("d4_16_shapes__default")), Is.EqualTo(ImageFormat.Pwc));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_IsIndexedWithThePalette() {
    var raw = PwcFile.ToRawImage(PwcReader.FromBytes(PwcFixtures.Pwc("d8_40_shapes__default")));

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(raw.PaletteCount, Is.EqualTo(40));
    Assert.That(raw.PixelData, Has.Length.EqualTo(raw.Width * raw.Height));
  }

  [TestCase(ImageFormat.Png)]
  [TestCase(ImageFormat.Bmp)]
  [TestCase(ImageFormat.Jpeg)]
  [Category("Unit")]
  public void OtherFormatRenamedToPwc_IsRefused(ImageFormat other) {
    var raw = new RawImage { Width = 4, Height = 3, Format = PixelFormat.Rgb24, PixelData = new byte[36] };
    var bytes = FormatRegistry.Write(raw, other)!;

    Assert.That(PwcFile.ReadImageInfo(bytes), Is.Null);
    Assert.Throws<InvalidDataException>(() => PwcReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PwcReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PwcReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pwc"));
    Assert.Throws<FileNotFoundException>(() => PwcReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_IsRefused()
    => Assert.Throws<InvalidDataException>(() => PwcReader.FromBytes([0x34, 0x79, 0x56, 0x61, 1, 0]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSignature_IsRefused() {
    var bytes = PwcFixtures.Pwc("d8_40_shapes__default");
    bytes[3] = (byte)'b';
    Assert.Throws<InvalidDataException>(() => PwcReader.FromBytes(bytes));
  }

  [TestCase(2)]
  [TestCase(3)]
  [TestCase(16)]
  [TestCase(24)]
  [Category("Unit")]
  public void FromBytes_DepthWithoutAModel_IsRefusedByName(int depth) {
    var bytes = PwcFixtures.Pwc("d8_40_shapes__default");
    bytes[8] = (byte)depth;
    var error = Assert.Throws<InvalidDataException>(() => PwcReader.FromBytes(bytes))!;
    Assert.That(error.Message, Does.Contain($"{depth}-bit"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroDimension_IsRefused() {
    var bytes = PwcFixtures.Pwc("d4_3x2__default");
    bytes[6] = bytes[7] = 0;
    Assert.Throws<InvalidDataException>(() => PwcReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_OversizedPicture_IsRefusedBeforeAllocating() {
    var bytes = PwcFixtures.Pwc("d4_flat__default");
    bytes[4] = bytes[5] = bytes[6] = bytes[7] = 0xFF;
    var error = Assert.Throws<InvalidDataException>(() => PwcReader.FromBytes(bytes))!;
    Assert.That(error.Message, Does.Contain("limit"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PaletteTooLarge_IsRefused() {
    var bytes = PwcFixtures.Pwc("d8_40_shapes__default");
    bytes[12] = 0x01;
    bytes[13] = 0x02;
    Assert.Throws<InvalidDataException>(() => PwcReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_FourBitPictureWithByteSizedPalette_IsRefused() {
    var bytes = PwcFixtures.Pwc("d8_40_shapes__default");
    bytes[8] = 4;
    Assert.Throws<InvalidDataException>(() => PwcReader.FromBytes(bytes));
  }

  [TestCase("d1_shapes__default")]
  [TestCase("d4_16_shapes__default")]
  [TestCase("d8_256_shapes__default")]
  [TestCase("d8_gray__gray")]
  [Category("Unit")]
  public void FromBytes_EveryTruncation_IsRefused(string name) {
    var bytes = PwcFixtures.Pwc(name);
    for (var length = 0; length < bytes.Length; ++length)
      Assert.Throws<InvalidDataException>(() => PwcReader.FromSpan(bytes.AsSpan(0, length)), $"truncated to {length} bytes");
  }

  [TestCase("d1_shapes__default")]
  [TestCase("d4_16_shapes__default")]
  [TestCase("d8_40_shapes__default")]
  [TestCase("d8_256_grad__default")]
  [Category("Unit")]
  public void FromBytes_CorruptedPayload_IsRefused(string name) {
    var bytes = PwcFixtures.Pwc(name);
    var payloadStart = PwcFixtures.PayloadOffset(bytes);
    var refused = 0;
    var tried = 0;
    for (var at = payloadStart; at < bytes.Length; at += Math.Max(1, (bytes.Length - payloadStart) / 16)) {
      var corrupt = (byte[])bytes.Clone();
      corrupt[at] ^= 0x5A;
      ++tried;
      try {
        PwcReader.FromBytes(corrupt);
      } catch (InvalidDataException) {
        ++refused;
      }
    }

    Assert.That(refused, Is.EqualTo(tried), "a corrupted stream decoded to a picture instead of being refused");
  }

  [Test]
  [Category("Unit")]
  public void RandomDamage_IsOnlyEverRefusedAsInvalidData() {
    var random = new Random(577);
    foreach (var name in PwcFixtures.Names()) {
      var bytes = PwcFixtures.Pwc(name);
      for (var round = 0; round < 40; ++round) {
        var damaged = (byte[])bytes.Clone();
        for (var hits = random.Next(1, 4); hits > 0; --hits)
          damaged[random.Next(12, damaged.Length)] = (byte)random.Next(256);

        try {
          PwcReader.FromBytes(damaged);
        } catch (InvalidDataException) {
          // The one acceptable way to fail.
        } catch (Exception error) {
          Assert.Fail($"{name}, round {round}: {error.GetType().Name}: {error.Message}");
        }
      }
    }
  }
}
