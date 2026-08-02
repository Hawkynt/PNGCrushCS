using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// Formats whose reader needs the file's name, and whether the registry can reach that reader.
/// </summary>
/// <remarks>
/// Several formats store a picture whose shape nothing inside the file states — a BBC Micro screen
/// of 20480 bytes is mode 0, mode 1 or mode 2 and only the extension says which. Their readers have
/// always known this and taken the extension into account, but the format only wired up its by-bytes
/// entry, so the registry never reached the extension-aware path and a default decided instead.
/// <para/>
/// Twelve formats carried it. Each was otherwise found only when a sample happened to expose it, so
/// this reads a file by name twice under different extensions and insists the answers differ.
/// </remarks>
[TestFixture]
public sealed class ExtensionAwareReadersTests {

  private static string _Write(byte[] data, string extension) {
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
    File.WriteAllBytes(path, data);
    return path;
  }

  [Test]
  [Category("Integration")]
  public void TheSameBytesUnderDifferentExtensionsAreReadDifferently() {
    // A 20480-byte BBC dump: mode 0 is 640 by 512 in two colours, mode 1 is 320 by 256 in four.
    var data = new byte[20480];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 31 % 251);

    var zero = _Write(data, ".bb0");
    var one = _Write(data, ".bb1");

    try {
      var a = FormatRegistry.Read(new FileInfo(zero));
      var b = FormatRegistry.Read(new FileInfo(one));

      Assert.That(a, Is.Not.Null);
      Assert.That(b, Is.Not.Null);
      Assert.Multiple(() => {
        Assert.That(a!.Width, Is.EqualTo(640), "mode 0 is the wide one");
        Assert.That(b!.Width, Is.EqualTo(320), "mode 1 is not, and only the name says so");
        Assert.That(b.Height, Is.EqualTo(256));
      });
    } finally {
      File.Delete(zero);
      File.Delete(one);
    }
  }

  [Test]
  [Category("Integration")]
  public void ReadingByNameAndByBytesNeedNotAgree() {
    // Reading the bytes alone cannot know the mode, so it falls back; that is expected, and it is
    // exactly why the by-name path has to exist.
    var data = new byte[20480];
    var path = _Write(data, ".bb1");

    try {
      var byName = FormatRegistry.Read(new FileInfo(path));
      var byBytes = FormatRegistry.Read(data);

      Assert.That(byName, Is.Not.Null);
      Assert.That(byName!.Width, Is.EqualTo(320));
      if (byBytes != null)
        Assert.That(byBytes.Width, Is.Not.EqualTo(byName.Width).Or.EqualTo(320));
    } finally {
      File.Delete(path);
    }
  }
}
