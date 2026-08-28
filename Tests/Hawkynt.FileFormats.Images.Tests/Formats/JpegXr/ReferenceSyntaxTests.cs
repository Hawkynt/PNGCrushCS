using System;
using System.Buffers.Binary;
using FileFormat.JpegXr.Codec;

namespace FileFormat.JpegXr.Tests;

[TestFixture]
public sealed class ReferenceSyntaxTests {

  // glencoesoftware/jxrlib fixtures/red.jxr, also used by jpegxr-pure-rs as an upstream JXRLib
  // parity fixture. 10x10 solid red, 32bpp BGRA container with a separate planar alpha stream.
  private const string _RedFixtureBase64 =
    "SUm8ASAAAAAkw91vA07+S7GFPXd2jckPAAAAAAAAAAAKAAG8AQAQAAAACAAA" +
    "AAK8BAABAAAAAAAAAIC8BAABAAAACgAAAIG8BAABAAAACgAAAIK8CwABAAAA" +
    "nASQQoO8CwABAAAAnASQQsC8BAABAAAAngAAAMG8BAABAAAArwAAAMK8BAAB" +
    "AAAATgEAAMO8BAABAAAAxgEAAAAAAABXTVBIT1RPABFFwHEACQAJYADAAAAM" +
    "AAAAwAAAAAABAAAACgAn//8AAAEBdcSPEXggAAABAgAhgAAIBAMAABDAAAQC" +
    "AYAAAAAAAAAAAAAAAAEDSxbn+jWyIvDIi8dNKJkP8QF9NKId/j3VkH9Omupt" +
    "W+W7r6byh3ccy7fczLW25ly1s5Da2T/qZP+q2N1NP+qBLtjdR43O5bTvVbGx" +
    "GVb+bl9NxDu7uXb+TE1t+TawAFdNUEhPVE8AEUXAAQAJAAkAgCAIAAABAAAA" +
    "BgAU//8AAAEBkeAAAAECEEBCmGIwhAMQAAAAAQOPOkyUnbp55zhMzxTDQ5GI" +
    "+9zgfDRwmyipGHRih47kaF+5zA7hjHY6m4J2cRPeUNQtwtoeO6ahYnvKGoVL" +
    "bnTsYA==";

  [Test]
  public void RedJxr_ContainerUsesRealWicGuidAndReferencePacketLayout() {
    var bytes = Convert.FromBase64String(_RedFixtureBase64);
    Assert.That(bytes.Length, Is.EqualTo(454));
    var ifd = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
    var entries = JpegXrIfd.ParseEntries(bytes, checked((int)ifd));

    JpegXrIfdEntry? pixel = null;
    uint imageOffset = 0, imageByteCount = 0, alphaOffset = 0;
    foreach (var entry in entries) {
      if (entry.Tag == JpegXrIfd.TAG_PIXEL_FORMAT) pixel = entry;
      else if (entry.Tag == JpegXrIfd.TAG_IMAGE_OFFSET) imageOffset = entry.Value;
      else if (entry.Tag == JpegXrIfd.TAG_IMAGE_BYTE_COUNT) imageByteCount = entry.Value;
      else if (entry.Tag == JpegXrIfd.TAG_ALPHA_OFFSET) alphaOffset = entry.Value;
    }

    Assert.That(pixel, Is.Not.Null);
    var format = JpegXrIfd.ParsePixelFormat(bytes, pixel!.Value);
    Assert.Multiple(() => {
      Assert.That(format.ComponentCount, Is.EqualTo(4));
      Assert.That(format.BgrOrder, Is.True);
      Assert.That(format.HasAlpha, Is.True);
      Assert.That(imageOffset, Is.EqualTo(158));
      Assert.That(imageByteCount, Is.EqualTo(175));
      Assert.That(alphaOffset, Is.EqualTo(334));
    });

    var codestream = bytes.AsMemory((int)imageOffset, (int)imageByteCount);
    var header = JxrReferenceSyntax.Parse(codestream);
    Assert.Multiple(() => {
      Assert.That(header.Version, Is.EqualTo(1));
      Assert.That(header.SubVersion, Is.EqualTo(1));
      Assert.That(header.Width, Is.EqualTo(10));
      Assert.That(header.Height, Is.EqualTo(10));
      Assert.That(header.BitstreamFormat, Is.EqualTo(JxrReferenceSyntax.BitstreamFormat.Frequency));
      Assert.That(header.Overlap, Is.EqualTo(JxrReferenceSyntax.Overlap.One));
      Assert.That(header.ExternalColorFormat, Is.EqualTo(JxrReferenceSyntax.ColorFormat.Rgb));
      Assert.That(header.ExternalBitDepth, Is.EqualTo(JxrReferenceSyntax.BitDepth.Eight));
      Assert.That(header.Plane.InternalColorFormat, Is.EqualTo(JxrReferenceSyntax.ColorFormat.Yuv444));
      Assert.That(header.Plane.Subband, Is.EqualTo(JxrReferenceSyntax.Subband.All));
      Assert.That(header.Plane.ChannelCount, Is.EqualTo(3));
      Assert.That(header.PacketOffsets, Is.EqualTo(new[] { 0, 10, 39, 0 }));
      Assert.That(header.PacketBodyOffset, Is.EqualTo(39));
    });
  }
}
