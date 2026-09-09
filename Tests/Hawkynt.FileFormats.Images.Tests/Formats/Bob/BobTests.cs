using System;
using System.IO;
using FileFormat.Bob;
using FileFormat.Core;
using FileFormat.Core.PixelFormats;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Bob.Tests;

[TestFixture]
public class BobReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BobReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => BobReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BobReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => BobReader.FromBytes(new byte[7]));

  /// <summary>Builds a file of the shape the format actually has: size, palette, one index a pixel.</summary>
  private static byte[] _File(int width, int height) {
    var data = new byte[4 + 768 + width * height];
    data[0] = (byte)width;
    data[1] = (byte)(width >> 8);
    data[2] = (byte)height;
    data[3] = (byte)(height >> 8);
    return data;
  }

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var result = BobReader.FromBytes(_File(320, 200));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
    });
  }

  [Test]
  public void FromBytes_TakesTheHeightFromTheSecondWord() {
    // It was read from offset 4 instead, which is inside the palette — a real file came back
    // 1419 by 65535 rather than 1419 by 1001.
    var data = _File(1419, 1001);

    Assert.That(BobReader.FromBytes(data).Height, Is.EqualTo(1001));
  }

  [Test]
  public void FromBytes_RefusesAFileWhoseLengthDoesNotMatchItsHeader() {
    // The length is the whole of the check, so a file of some other format is refused rather than
    // being given a made-up size and read anyway.
    var data = _File(320, 200);
    Array.Resize(ref data, data.Length - 1);

    Assert.Throws<InvalidDataException>(() => BobReader.FromBytes(data));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BobReader.FromStream(null!));
}

[TestFixture]
public class RoundTripTests {

  private static BobFile _Picture() {
    var pixels = new byte[320 * 200];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i & 0xFF);

    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i * 7);
    }

    return new() { Width = 320, Height = 200, PixelData = pixels, Palette = palette };
  }

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = _Picture();
    var restored = BobReader.FromBytes(BobWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.PixelData, Is.EqualTo(file.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(file.Palette));
    });
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = _Picture();
    var raw = BobFile.ToRawImage(file);
    var restored = BobFile.FromRawImage(raw);

    Assert.Multiple(() => {
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(restored.PixelData, Is.EqualTo(file.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(file.Palette));
    });
  }

  [Test]
  public void FromRawImage_RejectsNonIndexedInputInsteadOfQuantizing() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [0, 0, 0, 255],
    };

    Assert.That(() => BobFile.FromRawImage(raw), Throws.ArgumentException);
  }

  [Test]
  public void FromRawImage_RejectsPaletteThatIsNotExactly256Entries() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0],
      Palette = [0, 0, 0],
      PaletteCount = 1,
    };

    Assert.That(() => BobFile.FromRawImage(raw), Throws.ArgumentException);
  }

  [Test]
  public void FromRawImage_RejectsTransparency() {
    var file = _Picture();
    var alpha = new byte[256];
    Array.Fill(alpha, byte.MaxValue);
    alpha[17] = 0;
    var raw = new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData,
      Palette = file.Palette,
      PaletteCount = 256,
      AlphaTable = alpha,
    };

    Assert.That(() => BobFile.FromRawImage(raw), Throws.ArgumentException);
  }

  [Test]
  public void Writer_RejectsTruncatedPaletteAndPixelsInsteadOfPaddingThem() {
    var file = _Picture();

    Assert.Multiple(() => {
      Assert.That(() => BobWriter.ToBytes(file with { Palette = new byte[767] }), Throws.ArgumentException);
      Assert.That(() => BobWriter.ToBytes(file with { PixelData = new byte[file.PixelData.Length - 1] }), Throws.ArgumentException);
    });
  }

  [Test]
  public void Registry_AdvertisesAndDispatchesExactIndexed8Writer() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Bob);
    Assert.That(entry, Is.Not.Null);
    Assert.That(entry!.TypedWriteCapabilities, Has.Length.EqualTo(1));

    var capability = entry.TypedWriteCapabilities[0];
    var raw = BobFile.ToRawImage(_Picture());
    var restored = BobReader.FromBytes(capability.Encode(raw));

    Assert.Multiple(() => {
      Assert.That(capability.PixelFormat.Kind, Is.EqualTo(RawPixelFormatKind.Indexed));
      Assert.That(capability.PixelFormat.IndexBitDepth, Is.EqualTo(8));
      Assert.That(capability.Accepts(raw), Is.True);
      Assert.That(restored.PixelData, Is.EqualTo(raw.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(raw.Palette));
    });
  }

  [Test]
  public void TypedEntryPoint_PreservesPaletteAndIndices() {
    var raw = BobFile.ToRawImage(_Picture());
    var typed = RawImage<Indexed8>.FromUntyped(raw);
    var restored = BobFile.FromRawImage(typed);

    Assert.Multiple(() => {
      Assert.That(restored.PixelData, Is.EqualTo(raw.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(raw.Palette));
    });
  }
}
