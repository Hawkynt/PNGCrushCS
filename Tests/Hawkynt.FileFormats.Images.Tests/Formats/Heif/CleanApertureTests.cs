using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Heif;
using NUnit.Framework;

namespace Hawkynt.FileFormats.Images.Tests.Formats.Heif;

/// <summary>
/// The CleanApertureBox, which is how a HEIF says that most of its stored picture is padding.
/// </summary>
/// <remarks>
/// HEVC codes whole coding blocks, so an encoder puts a 37x23 still inside a 64x64 picture, writes
/// that padded size to <c>ispe</c>, and adds a <c>clap</c> naming the window that is the real image.
/// Reading only <c>ispe</c> therefore reported 64x64 for every such file — and every HEIF ImageMagick
/// writes is one.
/// </remarks>
[TestFixture]
public class CleanApertureTests {

  [Test]
  public void The_Clean_Aperture_Is_The_Size_That_Is_Reported() {
    var heif = _BuildHeif(storedWidth: 64, storedHeight: 64, clapWidth: 37, clapHeight: 23);

    var file = HeifReader.FromBytes(heif);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(37), "the visible width, not the padded one");
      Assert.That(file.Height, Is.EqualTo(23), "the visible height, not the padded one");
    });
  }

  [Test]
  public void Without_A_Clean_Aperture_The_Stored_Size_Stands() {
    var heif = _BuildHeif(storedWidth: 48, storedHeight: 32, clapWidth: 0, clapHeight: 0);

    var file = HeifReader.FromBytes(heif);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(48));
      Assert.That(file.Height, Is.EqualTo(32));
    });
  }

  /// <summary>
  /// A clean aperture larger than the picture holding it is not believed.
  /// </summary>
  /// <remarks>
  /// It cannot be honoured — there are no pixels out there — and taking it at its word would have a
  /// caller allocate for an image the file does not contain.
  /// </remarks>
  [Test]
  public void A_Clean_Aperture_Bigger_Than_Its_Picture_Is_Ignored() {
    var heif = _BuildHeif(storedWidth: 32, storedHeight: 32, clapWidth: 900, clapHeight: 900);

    var file = HeifReader.FromBytes(heif);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(32));
      Assert.That(file.Height, Is.EqualTo(32));
    });
  }

  /// <summary>A minimal HEIF: ftyp, then a meta box carrying ispe and optionally clap.</summary>
  private static byte[] _BuildHeif(int storedWidth, int storedHeight, int clapWidth, int clapHeight) {
    var ispe = _Box("ispe", _Concat(
      new byte[4],                              // version + flags
      _UInt32(storedWidth), _UInt32(storedHeight)));

    var properties = new List<byte[]> { ispe };
    if (clapWidth > 0 && clapHeight > 0)
      properties.Add(_Box("clap", _Concat(
        _UInt32(clapWidth), _UInt32(1),
        _UInt32(clapHeight), _UInt32(1),
        _UInt32(0), _UInt32(1),                 // horizOff
        _UInt32(0), _UInt32(1))));              // vertOff

    var ipco = _Box("ipco", _Concat(properties.ToArray()));
    var iprp = _Box("iprp", ipco);
    var meta = _Box("meta", _Concat(new byte[4], iprp)); // meta is a FullBox
    var ftyp = _Box("ftyp", _Concat("heic"u8.ToArray(), _UInt32(0), "heic"u8.ToArray()));

    return _Concat(ftyp, meta);
  }

  private static byte[] _Box(string type, byte[] payload) {
    var box = new byte[8 + payload.Length];
    BinaryPrimitives.WriteInt32BigEndian(box.AsSpan(0), box.Length);
    for (var i = 0; i < 4; ++i)
      box[4 + i] = (byte)type[i];

    payload.CopyTo(box.AsSpan(8));
    return box;
  }

  private static byte[] _UInt32(int value) {
    var bytes = new byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
    return bytes;
  }

  private static byte[] _Concat(params byte[][] parts) {
    var total = 0;
    foreach (var part in parts)
      total += part.Length;

    var result = new byte[total];
    var at = 0;
    foreach (var part in parts) {
      part.CopyTo(result.AsSpan(at));
      at += part.Length;
    }

    return result;
  }
}
