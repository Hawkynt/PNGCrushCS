using FileFormat.Core;

namespace FileFormat.ZsStaffKid98.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A picture of at most the sixteen colours the file's palette holds.</summary>
  private static RawImage SixteenColours(int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var i = 0; i < width * height; ++i) {
      var index = i % 16;
      rgb[i * 3] = (byte)(index * 17);
      rgb[i * 3 + 1] = (byte)(255 - index * 17);
      rgb[i * 3 + 2] = (byte)(index * 5 + 40);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>
  /// A width that is neither a multiple of eight nor of the run limit, so both the last byte of a
  /// plane and the split between runs have to be right.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_AWidthThatIsNoWholeNumberOfBytes_IsExact() {
    var source = SixteenColours(101, 37);

    var bytes = ZsStaffKid98Writer.ToBytes(_Encode<ZsStaffKid98File>(source));
    var decoded = ZsStaffKid98File.ToRawImage(ZsStaffKid98Reader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(101));
      Assert.That(decoded.Height, Is.EqualTo(37));
      Assert.That(decoded.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>
  /// A run may cover 1024 pixels at most, since its four planes have to fit 512 bytes. A row wider
  /// than that has to be split, and the split is exactly where a decoder would notice a mistake.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_ARowLongerThanOneRun_IsExact() {
    var source = SixteenColours(1100, 3);

    var bytes = ZsStaffKid98Writer.ToBytes(_Encode<ZsStaffKid98File>(source));
    var decoded = ZsStaffKid98File.ToRawImage(ZsStaffKid98Reader.FromBytes(bytes));

    Assert.That(decoded.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
  }

  /// <summary>
  /// The format states its own size, so there is no screen to sample to and a picture keeps
  /// whatever size it came with.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsThePictureSize() {
    var file = _Encode<ZsStaffKid98File>(SixteenColours(13, 5));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(13));
      Assert.That(file.Height, Is.EqualTo(5));

      // Nothing shorter than this is believed to be a picture at all.
      Assert.That(ZsStaffKid98Writer.ToBytes(file), Has.Length.GreaterThanOrEqualTo(700));
    });
  }

  private static TFile _Encode<TFile>(RawImage image) where TFile : IImageFromRawImage<TFile>
    => TFile.FromRawImage(image);

}
