using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.RawGreyscale.Tests;

/// <summary>
/// The third name XnView's raw row carries, and why the same dump is placed under it more carefully
/// than under the other two.
/// </summary>
/// <remarks>
/// XnView files <c>raw</c>, <c>gry</c> and <c>grey</c> on one row against one reader whose channel
/// type defaults to greyscale, and that reader is this one. Only two of the three names were
/// claimed here, so a dump arriving as <c>.raw</c> reached the camera-raw reader alone — which
/// wants a TIFF byte-order mark at the front and refuses a file that carries only pixels. Nothing
/// else was asked and the file was refused outright.
/// <para/>
/// The two names differ in what they promise. <c>.gry</c> and <c>.grey</c> say greyscale in the
/// name, so one byte a pixel is the reading and the length is all that is left to settle. <c>.raw</c>
/// says nothing: the same converter writes three bytes a pixel under it whenever the picture it
/// was given had colour in it, and a length can be both — 230,400 bytes is 480 by 480 in grey and
/// 320 by 240 in colour, and both of those are sizes the table holds. Under that name such a length
/// is refused rather than drawn at whichever of the two comes first, because a picture shown at the
/// wrong shape looks like a reading and is not one.
/// </remarks>
[TestFixture]
public sealed class RawUnderTheBareNameTests {

  private static byte[] _Ramp(int width, int height) {
    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = (byte)(x * 3 + y * 7);

    return pixels;
  }

  private static FileInfo _Write(byte[] data, string extension) {
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
    File.WriteAllBytes(path, data);

    return new(path);
  }

  /// <summary>The 76,800 bytes the converter wrote come back as the picture that went into it.</summary>
  [Test]
  [Category("Integration")]
  public void TheRegistryReadsAGreyscaleDumpNamedRaw() {
    var pixels = _Ramp(320, 240);
    var file = _Write(pixels, ".raw");

    try {
      var image = FormatRegistry.Read(file);

      Assert.That(image, Is.Not.Null);
      Assert.Multiple(() => {
        Assert.That(image!.Width, Is.EqualTo(320));
        Assert.That(image.Height, Is.EqualTo(240));
        Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
        Assert.That(image.PixelData, Is.EqualTo(pixels));
      });
    } finally {
      file.Delete();
    }
  }

  /// <summary>The camera-raw reader keeps the name and keeps its signature check.</summary>
  [Test]
  [Category("Unit")]
  public void TheCameraRawReaderStillClaimsTheNameFirst() {
    Assert.Multiple(() => {
      Assert.That(FormatRegistry.DetectFromExtension(".raw"), Is.EqualTo(ImageFormat.CameraRaw));
      Assert.That(FormatRegistry.DetectCandidatesFromExtension(".raw"), Does.Contain(ImageFormat.CameraRaw));
      Assert.That(FormatRegistry.DetectCandidatesFromExtension(".raw"), Does.Contain(ImageFormat.RawGreyscale));
    });
  }

  /// <summary>A length two channel counts can both explain is refused under the bare name.</summary>
  [Test]
  [Category("Integration")]
  public void ALengthColourCouldAlsoExplainIsRefusedUnderTheBareName() {
    // 230,400 bytes: 480 by 480 greyscale, or the 320 by 240 colour dump the converter writes by
    // default from a colour picture. Nothing in the file says which.
    var file = _Write(new byte[230400], ".raw");

    try {
      Assert.That(FormatRegistry.Read(file), Is.Null);
    } finally {
      file.Delete();
    }
  }

  /// <summary>The names that do say greyscale keep placing that length, as they always did.</summary>
  [Test]
  [Category("Unit")]
  public void TheNamesThatSayGreyscaleStillPlaceThatLength() {
    var file = _Write(new byte[230400], ".gry");

    try {
      var image = FormatRegistry.Read(file);

      Assert.That(image, Is.Not.Null);
      Assert.Multiple(() => {
        Assert.That(image!.Width, Is.EqualTo(480));
        Assert.That(image.Height, Is.EqualTo(480));
      });
    } finally {
      file.Delete();
    }
  }

  /// <summary>Reading the same bytes without a name is the greyscale reading, there being no other.</summary>
  [Test]
  [Category("Unit")]
  public void ReadingByBytesAloneIsStillTheGreyscaleReading()
    => Assert.That(RawGreyscaleReader.FromBytes(new byte[230400]).Width, Is.EqualTo(480));

  /// <summary>A length neither channel count explains is refused under every name.</summary>
  [Test]
  [Category("Unit")]
  public void ALengthNoShapeExplainsIsStillRefused() {
    var file = _Write(new byte[1234], ".raw");

    try {
      Assert.That(FormatRegistry.Read(file), Is.Null);
    } finally {
      file.Delete();
    }
  }

  /// <summary>
  /// The three refusals say three different things, which is the whole use of them.
  /// </summary>
  /// <remarks>
  /// 691,200 bytes is 480 by 480 in colour and no greyscale size at all; 230,400 is both; 1,234 is
  /// neither. Reporting all three as "not a size" would leave a caller unable to tell a colour dump
  /// this does not read from a file that is not a dump.
  /// </remarks>
  [TestCase(691200, "colour one, which is not read here")]
  [TestCase(230400, "both a 480 by 480 greyscale picture and a 320 by 240 colour one")]
  [TestCase(1234, "not one of the sizes it comes in")]
  [Category("Unit")]
  public void TheRefusalSaysWhichKindOfLengthItWas(int length, string expected) {
    var file = _Write(new byte[length], ".raw");

    try {
      var entry = FormatRegistry.GetEntry(ImageFormat.RawGreyscale);
      var failure = Assert.Throws<InvalidDataException>(() => entry!.LoadRawImageOrThrow!(file));

      Assert.That(failure!.Message, Does.Contain(expected));
    } finally {
      file.Delete();
    }
  }
}
