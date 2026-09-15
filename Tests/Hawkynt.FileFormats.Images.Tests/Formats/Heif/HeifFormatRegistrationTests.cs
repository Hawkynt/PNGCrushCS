using Hawkynt.FileFormats.Images;

namespace FileFormat.Heif.Tests;

[TestFixture]
public sealed class HeifFormatRegistrationTests {

  [TestCase("image/heic")]
  [TestCase("image/heif")]
  [TestCase("image/avci")]
  [TestCase("image/avcs")]
  public void RegisteredMediaTypes_DetectAsHeif(string mimeType) {
    Assert.That(FormatRegistry.DetectFromMimeType(mimeType), Is.EqualTo(ImageFormat.Heif));
  }

  [Test]
  public void PrimaryMediaType_IsHeic() {
    Assert.Multiple(() => {
      Assert.That(FormatRegistry.PrimaryMimeType(ImageFormat.Heif), Is.EqualTo("image/heic"));
      Assert.That(FormatRegistry.AllMimeTypes(ImageFormat.Heif), Does.Contain("image/heif"));
    });
  }
}
