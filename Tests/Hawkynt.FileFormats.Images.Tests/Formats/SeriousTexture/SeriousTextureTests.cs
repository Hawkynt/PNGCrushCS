using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.SeriousTexture;
using Hawkynt.FileFormats.Images;

namespace FileFormat.SeriousTexture.Tests;

/// <summary>Serious Engine textures, in every storage the twelve public samples use.</summary>
/// <remarks>
/// Built here in each layout measured on <c>telparia.com/fileFormatSamples/image/croteamTextureFile</c>:
/// version-4 RGB and RGBA frames, a version-3 16-bit mip chain, and the DXT5 <c>FRMC</c> chunk. The
/// opt-in test reads the twelve samples themselves, which are not committed.
/// </remarks>
[TestFixture]
public sealed class SeriousTextureTests {

  private static byte[] _Tdat(int version, uint flags, int mexW, int mexH, int fine, int first, int frames, int frameSize = 0) {
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write("TVER"u8);
    w.Write(version);
    w.Write("TDAT"u8);
    w.Write(flags);
    w.Write(mexW);
    w.Write(mexH);
    w.Write(fine);
    if (version == 3)
      w.Write(fine);
    w.Write(first);
    if (version == 3)
      w.Write(frameSize);
    w.Write(frames);
    w.Flush();
    return ms.ToArray();
  }

  private static byte[] _Anim(params (string Name, float Seconds, int[] Frames)[] animations) {
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write("ANIM"u8);
    w.Write("ADAT"u8);
    w.Write(animations.Length);
    foreach (var (name, seconds, frames) in animations) {
      var field = new byte[32];
      System.Text.Encoding.ASCII.GetBytes(name).CopyTo(field, 0);
      w.Write(field);
      w.Write(seconds);
      w.Write(frames.Length);
      foreach (var f in frames)
        w.Write(f);
    }
    w.Flush();
    return ms.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void AVersion4RgbFrameIsReadAtTheMipShiftedSize() {
    var pixels = Enumerable.Range(0, 8 * 4 * 3).Select(static i => (byte)i).ToArray();
    var data = _Tdat(4, 0, 32, 16, 4, 2, 1).Concat("FRMS"u8.ToArray()).Concat(pixels).Concat(_Anim(("OnlyAnim", 0.02f, [0]))).ToArray();
    var file = SeriousTextureReader.FromBytes(data);
    Assert.Multiple(() => {
      Assert.That((file.Width, file.Height, file.MexWidth, file.FirstMipLevel), Is.EqualTo((8, 4, 32, 2)));
      Assert.That(file.Frames[0][..8], Is.EqualTo(new byte[] { 0, 1, 2, 255, 3, 4, 5, 255 }));
      Assert.That(file.Animations.Single().Name, Is.EqualTo("OnlyAnim"));
    });
  }

  [Test]
  [Category("Unit")]
  public void AVersion4RgbaTextureRoundTripsThroughTheWriter() {
    var rgba = new byte[16 * 16 * 4];
    new Random(4).NextBytes(rgba);
    var file = new SeriousTextureFile {
      Version = 4, Flags = SeriousTextureFile.AlphaChannelFlag | 2, MexWidth = 64, MexHeight = 64, FineMipLevels = 5, FirstMipLevel = 2,
      Width = 16, Height = 16, Frames = [rgba, rgba.Reverse().ToArray()],
      Animations = [new("Spin", 0.1f, [0, 1, 0])],
    };
    var back = SeriousTextureReader.FromBytes(SeriousTextureWriter.ToBytes(file));
    Assert.Multiple(() => {
      Assert.That(back.Flags, Is.EqualTo(file.Flags));
      Assert.That(back.Frames, Is.EqualTo(file.Frames));
      Assert.That(SeriousTextureFile.ImageCount(back), Is.EqualTo(2));
      Assert.That((back.Animations[0].Name, back.Animations[0].SecondsPerFrame, back.Animations[0].Frames), Is.EqualTo(("Spin", 0.1f, new[] { 0, 1, 0 })));
    });
  }

  [Test]
  [Category("Unit")]
  public void AVersion3FrameIsTheFirstMipOfASixteenBitChain() {
    // RGB565 with the low bit unused: pure red, then pure blue; then the rest of the chain.
    var chain = new byte[(4 + 1) * 2 * 2];
    BinaryPrimitives.WriteUInt16LittleEndian(chain, 0xF800);
    BinaryPrimitives.WriteUInt16LittleEndian(chain.AsSpan(2), 0x003E);
    var data = _Tdat(3, 0, 2, 2, 2, 0, 2, chain.Length / 2).Concat("FRMS"u8.ToArray()).Concat(chain).ToArray();
    var file = SeriousTextureReader.FromBytes(data);
    Assert.That(file.Frames[0][..8], Is.EqualTo(new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 }));
    Assert.That(file.Frames, Has.Count.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void AVersion3AlphaFrameIsRgba4444() {
    var chain = new byte[2 * 2];
    BinaryPrimitives.WriteUInt16LittleEndian(chain, 0x1234);
    var data = _Tdat(3, SeriousTextureFile.AlphaChannelFlag, 1, 1, 1, 0, 1, chain.Length).Concat("FRMS"u8.ToArray()).Concat(chain).ToArray();
    Assert.That(SeriousTextureReader.FromBytes(data).Frames[0], Is.EqualTo(new byte[] { 0x11, 0x22, 0x33, 0x44 }));
  }

  [Test]
  [Category("Unit")]
  public void CompressedFramesAreADxt5MipChain() {
    // 4x4: one block, white with alpha 255; then 2x2 and 1x1, a block each.
    var block = new byte[] { 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0 };
    using var ms = new MemoryStream();
    using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, true)) {
      w.Write(_Tdat(4, 0x25, 32, 32, 3, 3, 1));
      w.Write("FRMC"u8);
      w.Write(3 * (4 + 16));
      for (var i = 0; i < 3; ++i) {
        w.Write(16);
        w.Write(block);
      }
      w.Write(_Anim(("OnlyAnim", 0.02f, [0])));
    }
    var file = SeriousTextureReader.FromBytes(ms.ToArray());
    Assert.That(file.Compressed, Is.True);
    Assert.That(file.Frames[0].All(static b => b == 255), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void AnEffectTextureIsRefusedByName() {
    var data = _Tdat(4, 0, 4, 4, 1, 0, 1).Concat("FXDT"u8.ToArray()).Concat(new byte[16]).ToArray();
    var ex = Assert.Throws<NotSupportedException>(() => SeriousTextureReader.FromBytes(data));
    Assert.That(ex!.Message, Does.Contain("effect texture"));
  }

  [Test]
  [Category("Unit")]
  public void FramesThatRunPastTheEndAreRefused() {
    var data = _Tdat(4, 0, 64, 64, 1, 0, 1).Concat("FRMS"u8.ToArray()).Concat(new byte[100]).ToArray();
    Assert.Throws<InvalidDataException>(() => SeriousTextureReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImageGivesAPowerOfTwoTextureWithAlphaOnlyWhereNeeded() {
    var opaque = new RawImage { Width = 100, Height = 40, Format = PixelFormat.Rgb24, PixelData = new byte[100 * 40 * 3] };
    var file = SeriousTextureFile.FromRawImage(opaque);
    Assert.That((file.Width, file.Height, file.HasAlpha), Is.EqualTo((64, 32, false)));
    var bytes = SeriousTextureWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(8 + 28 + 4 + 64 * 32 * 3 + 4 + 4 + 4 + 40 + 4));
  }

  [Test]
  [Category("Unit")]
  public void ItIsFoundByItsSignatureAheadOfPaintShopProsTex() {
    var bytes = SeriousTextureWriter.ToBytes(SeriousTextureFile.FromRawImage(new RawImage { Width = 4, Height = 4, Format = PixelFormat.Rgb24, PixelData = new byte[48] }));
    Assert.That(FormatRegistry.DetectFromBytes(bytes), Is.EqualTo(ImageFormat.SeriousTexture));
  }

  /// <summary>Opt-in: point <c>SERIOUS_TEXTURE_SAMPLES</c> at telparia's <c>croteamTextureFile</c> folder.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs the croteamTextureFile samples; point SERIOUS_TEXTURE_SAMPLES at them.")]
  public void ThePublicSamplesReadAsTheyWereMeasured() {
    var directory = Environment.GetEnvironmentVariable("SERIOUS_TEXTURE_SAMPLES");
    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
      Assert.Ignore("SERIOUS_TEXTURE_SAMPLES does not name a directory.");
    (string File, int Version, int Width, int Height, bool Compressed)[] expected = [
      ("05_MoonMountains.tbn", 4, 128, 128, false), ("CEIL3_1L.tex", 4, 64, 64, false), ("ConanMechanic.tbn", 4, 128, 128, false),
      ("Tropics2.tex", 4, 1024, 1024, false), ("WatchPlayers.tbn", 3, 128, 128, false), ("Woman.tex", 4, 512, 512, false),
      ("mt_rd_s10_t.tex", 4, 256, 128, true), ("secretcity.tbn", 4, 128, 128, false),
    ];
    foreach (var (name, version, width, height, compressed) in expected) {
      var file = SeriousTextureReader.FromFile(new(Path.Combine(directory, name)));
      Assert.That((file.Version, file.Width, file.Height, file.Compressed), Is.EqualTo((version, width, height, compressed)), name);
      Assert.That(file.Animations.Single().Name, Is.EqualTo("OnlyAnim"), name);
      if (!compressed && version == 4) {
        // The engine leaves whatever was in memory after the animation name's nought; nothing reads it.
        var original = File.ReadAllBytes(Path.Combine(directory, name));
        var nameAt = original.Length - 4 - 4 - 32 - 4;
        for (var i = original.AsSpan(nameAt, 32).IndexOf((byte)0); i is >= 0 and < 32; ++i)
          original[nameAt + i] = 0;
        Assert.That(SeriousTextureWriter.ToBytes(file), Is.EqualTo(original), $"{name} written back");
      }
    }
  }
}
