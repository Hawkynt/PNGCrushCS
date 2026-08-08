using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.AxialisScreensaver.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      data[i * 3] = (byte)(i * 7);
      data[i * 3 + 1] = (byte)(i * 13);
      data[i * 3 + 2] = (byte)(i * 29);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  private static AxialisScreensaverFile _RoundTrip(RawImage image)
    => AxialisScreensaverReader.FromBytes(AxialisScreensaverWriter.ToBytes(AxialisScreensaverFile.FromRawImage(image)));

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient_ReproducesEveryPixel() {
    var source = _Gradient(37, 11);
    var decoded = AxialisScreensaverFile.ToRawImage(_RoundTrip(source));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = AxialisScreensaverFile.ToRawImage(_RoundTrip(_Gradient(200, 3)));
    var tall = AxialisScreensaverFile.ToRawImage(_RoundTrip(_Gradient(3, 200)));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AcceptsAFormatOtherThanItsOwn() {
    var grey = new RawImage { Width = 5, Height = 4, Format = PixelFormat.Gray8, PixelData = new byte[20] };

    Assert.That(AxialisScreensaverFile.ToRawImage(_RoundTrip(grey)).Width, Is.EqualTo(5));
  }

  /// <summary>
  /// A project has no directory. What finds a picture is the length written immediately in front of
  /// it agreeing with the length that picture's own framing gives — so the length has to be the
  /// picture's real one and the picture has to start where the length says, with nothing between
  /// them.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_StatesEachPicturesOwnLengthDirectlyInFrontOfIt() {
    var file = AxialisScreensaverFile.FromRawImage(_Gradient(37, 11));
    var bytes = AxialisScreensaverWriter.ToBytes(file);

    var stated = (uint)(bytes[AxialisScreensaverFile.SignatureSize]
                        | (bytes[AxialisScreensaverFile.SignatureSize + 1] << 8)
                        | (bytes[AxialisScreensaverFile.SignatureSize + 2] << 16)
                        | (bytes[AxialisScreensaverFile.SignatureSize + 3] << 24));

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, AxialisScreensaverFile.Magic.Length).SequenceEqual(AxialisScreensaverFile.Magic), Is.True);
      Assert.That(Encoding.ASCII.GetString(bytes, AxialisScreensaverFile.Magic.Length, 4), Is.EqualTo("0100"));
      Assert.That(stated, Is.EqualTo((uint)file.Embedded[0].Length));
      Assert.That(bytes, Has.Length.EqualTo(AxialisScreensaverFile.MinimumPayloadOffset + file.Embedded[0].Length));
    });
  }
}
