using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Mp4;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.QuickTimeRle.Tests;

/// <summary>
/// The QuickTime Animation encoder, checked by decoding what it writes with the decoder beside it.
/// </summary>
/// <remarks>
/// The coding is lossless, so the whole of the contract is that the decoder gets the input back
/// exactly — over a sequence, so that the line band and the skips are reached, and over pictures
/// that are random, so that runs and literal copies of every length are reached. The sample
/// description is checked against what the decoder reads out of it, because that is the
/// description a muxer will be handed.
/// </remarks>
[TestFixture]
public sealed class QuickTimeRleEncoderTests {

  // ============================================================================================
  // The description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheDescriptionIsAWholeSampleEntryTheDecoderReads([Values(24, 32)] int depth) {
    var encoder = QuickTimeRleEncoder.Create(_Requested(6, 3, depth));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("rle ")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("rle ")));
      Assert.That(stream.Width, Is.EqualTo(6));
      Assert.That(stream.Height, Is.EqualTo(3));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(depth));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(86));
    });

    var entry = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(entry), Is.EqualTo(86), "box length");
      Assert.That(entry[4..8], Is.EqualTo("rle "u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 24)), Is.EqualTo(6), "width");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 26)), Is.EqualTo(3), "height");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 74)), Is.EqualTo(depth), "depth");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 76)), Is.EqualTo(0xFFFF), "no colour table");
    });

    Assert.That(QuickTimeRleDecoder.Accepts(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<QuickTimeRleDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void AnEightBitDescriptionCarriesTheColourTable() {
    var encoder = QuickTimeRleEncoder.Create(_Requested(5, 2));
    Assert.That(encoder.TryEncode(_Indexed(5, 2, 1, 16), 0, out var packet), Is.True);

    var stream = encoder.DescribeStream();
    var entry = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(stream.BitsPerPixel, Is.EqualTo(8));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 74)), Is.EqualTo(8), "depth");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 76)), Is.EqualTo(0), "colour table follows");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(8 + 78 + 6)), Is.EqualTo(15), "entries less one");
      Assert.That(entry.Length, Is.EqualTo(8 + 78 + 8 + 16 * 8));
    });

    // Entry 3 is R=9, G=10, B=11; each channel is sixteen bits with the eight repeated.
    Assert.That(entry[(8 + 78 + 8 + 3 * 8)..(8 + 78 + 8 + 4 * 8)], Is.EqualTo(new byte[] { 0, 0, 9, 9, 10, 10, 11, 11 }));

    var decoder = QuickTimeRleDecoder.Create(stream);
    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.Palette!.AsSpan(0, 16 * 3).ToArray(), Is.EqualTo(_Palette(16)));
    Assert.That(frame.PixelData, Is.All.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void TheDescriptionCannotBeGivenBeforeTheDepthOrPaletteIsKnown() {
    var undecided = QuickTimeRleEncoder.Create(_Requested(4, 4));
    Assert.That(Assert.Throws<InvalidOperationException>(() => undecided.DescribeStream())!.Message, Does.Contain("depth"));

    var indexed = QuickTimeRleEncoder.Create(_Requested(4, 4, 8));
    Assert.That(Assert.Throws<InvalidOperationException>(() => indexed.DescribeStream())!.Message, Does.Contain("palette"));
  }

  [Test]
  [Category("Unit")]
  public void ADescriptionThatAlreadyCarriesAColourTableCanBeGivenBeforeTheFirstPicture() {
    var first = QuickTimeRleEncoder.Create(_Requested(4, 4));
    Assert.That(first.TryEncode(_Indexed(4, 4, 2, 16), 0, out _), Is.True);
    var described = first.DescribeStream();

    var second = QuickTimeRleEncoder.Create(described);
    var again = second.DescribeStream();

    Assert.That(again.CodecPrivateData.ToArray(), Is.EqualTo(described.CodecPrivateData.ToArray()));
    Assert.That(second.TryEncode(_Indexed(4, 4, 3, 16), 0, out _), Is.True, "a picture with the same sixteen colours is taken");
  }

  // ============================================================================================
  // The round trip
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ATwentyFourBitSequenceWithPartialChangesRoundTripsExactly([Values(1, 7, 64, 300)] int width) {
    var height = width switch { 1 => 1, 7 => 5, 64 => 40, _ => 3 };
    var pictures = _Sequence(width, height, PixelFormat.Rgb24, seed: width);

    _AssertRoundTrip(width, height, pictures, _KeyFrames(width, height));
  }

  [Test]
  [Category("Unit")]
  public void AThirtyTwoBitSequenceWithPartialChangesRoundTripsExactly([Values(1, 9, 48)] int width) {
    var height = width switch { 1 => 3, 9 => 7, _ => 32 };
    var pictures = _Sequence(width, height, PixelFormat.Rgba32, seed: width);

    _AssertRoundTrip(width, height, pictures, _KeyFrames(width, height));
  }

  [Test]
  [Category("Unit")]
  public void AnEightBitSequenceWithPartialChangesRoundTripsExactly([Values(1, 5, 7, 64, 301)] int width) {
    // Widths that are not a whole number of four-pixel units are coded padded and shown cropped.
    var height = width switch { 1 => 1, 5 => 9, 7 => 5, 64 => 40, _ => 3 };
    var pictures = _Sequence(width, height, PixelFormat.Indexed8, seed: width);

    _AssertRoundTrip(width, height, pictures, _KeyFrames(width, height));
  }

  [Test]
  [Category("Unit")]
  public void ALineLongerThanEveryOpcodeCanStateRoundTrips() {
    // 1000 pixels: runs longer than 128, literal stretches longer than 127 and unchanged stretches
    // longer than 254 all have to be split, and the decoder must land where the split says.
    var random = new Random(5);
    var pictures = new List<RawImage>();
    byte[]? before = null;
    for (var frame = 0; frame < 4; ++frame) {
      var pixels = new byte[1000 * 3];
      for (var i = 0; i < pixels.Length;) {
        var length = random.Next(1, 400) * 3;
        var colour = (byte)random.Next(0, frame == 0 ? 2 : 256);
        for (var n = 0; n < length && i < pixels.Length; ++n, ++i)
          pixels[i] = frame == 3 ? (byte)random.Next(0, 256) : colour;
      }

      if (before != null && frame is 1 or 2)
        Array.Copy(before, 0, pixels, 300, 900);

      pictures.Add(new() { Width = 1000, Height = 1, Format = PixelFormat.Rgb24, PixelData = pixels });
      before = pixels;
    }

    _AssertRoundTrip(1000, 1, pictures, null);
  }

  [Test]
  [Category("Unit")]
  public void TheFramesSurviveAQuickTimeFileAndComeBackThroughTheRegistry([Values(8, 24, 32)] int depth) {
    var format = depth switch { 8 => PixelFormat.Indexed8, 24 => PixelFormat.Rgb24, _ => PixelFormat.Rgba32 };
    var pictures = _Sequence(21, 10, format, seed: depth);
    var encoder = QuickTimeRleEncoder.Create(_Requested(21, 10));
    var packets = pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();

    var file = VideoIO.Mux<Mp4Writer>([encoder.DescribeStream()], packets);
    var container = Mp4Container.FromBytes(file);
    var stream = Mp4Container.Streams(container).Single();
    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("rle ")));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(depth));
    });

    var decoded = VideoFormatRegistry.DecodeFrames(file).Select(f => f.Image).ToList();
    Assert.That(decoded.Count, Is.EqualTo(pictures.Count));
    for (var i = 0; i < pictures.Count; ++i)
      Assert.That(decoded[i].PixelData, Is.EqualTo(pictures[i].PixelData), $"frame {i}");
  }

  // ============================================================================================
  // The packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TimestampsPassThroughUntouched() {
    var encoder = QuickTimeRleEncoder.Create(_Requested(4, 4, 24, index: 3));
    var picture = _Rgb(4, 4, 2);

    Assert.That(encoder.TryEncode(picture, 42, out var stamped), Is.True);
    Assert.That(encoder.TryEncode(picture, null, out var unstamped), Is.True);

    Assert.Multiple(() => {
      Assert.That(stamped.StreamIndex, Is.EqualTo(3));
      Assert.That(stamped.PresentationTimestamp, Is.EqualTo(42));
      Assert.That(stamped.DecodeTimestamp, Is.EqualTo(42));
      Assert.That(unstamped.PresentationTimestamp, Is.Null);
      Assert.That(unstamped.DecodeTimestamp, Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureIdenticalToTheOneBeforeIsTheSevenByteNothingChangedFrame() {
    var encoder = QuickTimeRleEncoder.Create(_Requested(4, 4, 24));
    var picture = _Rgb(4, 4, 9);

    Assert.That(encoder.TryEncode(picture, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(picture, 1, out var second), Is.True);

    Assert.Multiple(() => {
      Assert.That(first.IsKeyFrame, Is.True);
      Assert.That(second.IsKeyFrame, Is.False);
      Assert.That(second.Data.ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 7, 0, 0, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void AChangeInTheMiddleLinesIsWrittenAsABand() {
    var encoder = QuickTimeRleEncoder.Create(_Requested(2, 6, 24));
    var picture = _Rgb(2, 6, 1);
    Assert.That(encoder.TryEncode(picture, 0, out _), Is.True);

    var changed = (byte[])picture.PixelData.Clone();
    changed[2 * 3 * 2] = 7;   // line 2
    changed[2 * 3 * 3] = 7;   // line 3
    Assert.That(encoder.TryEncode(new() { Width = 2, Height = 6, Format = PixelFormat.Rgb24, PixelData = changed }, 1, out var band), Is.True);

    var data = band.Data.ToArray();
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4)), Is.EqualTo(8), "a band follows");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(6)), Is.EqualTo(2), "from line 2");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(10)), Is.EqualTo(2), "two lines");
      Assert.That(band.IsKeyFrame, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheFirstFrameOpensEveryLineWithASkipOfOneAndUsesNoSkip() {
    var encoder = QuickTimeRleEncoder.Create(_Requested(3, 1, 24));
    Assert.That(encoder.TryEncode(new() { Width = 3, Height = 1, Format = PixelFormat.Rgb24, PixelData = [1, 2, 3, 1, 2, 3, 1, 2, 3] }, 0, out var packet), Is.True);

    // Length, header 0, skip 1, run of three (0xFD), the unit, end of line 0xFF, end of frame 0.
    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 13, 0, 0, 1, 0xFD, 1, 2, 3, 0xFF, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void NothingIsHeldBack() {
    var encoder = QuickTimeRleEncoder.Create(_Requested(4, 4, 24));
    Assert.That(encoder.TryEncode(_Rgb(4, 4, 2), 0, out _), Is.True);

    Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
  }

  // ============================================================================================
  // The refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFormatThatIsNotWrittenIsRefusedByName([Values(PixelFormat.Bgra32, PixelFormat.Gray8, PixelFormat.Indexed4, PixelFormat.Rgb565)] PixelFormat format) {
    var encoder = QuickTimeRleEncoder.Create(_Requested(4, 4));
    var picture = new RawImage { Width = 4, Height = 4, Format = format, PixelData = new byte[4 * 4 * 4], Palette = _Palette(16), PaletteCount = 16 };

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain(format.ToString()));
  }

  [Test]
  [Category("Unit")]
  public void ADepthThatIsNotWrittenIsRefusedByName([Values(16, 4, 40)] int depth) {
    var failure = Assert.Throws<NotSupportedException>(() => QuickTimeRleEncoder.Create(_Requested(4, 4, depth)));
    Assert.That(failure!.Message, Does.Contain($"{depth} bits per pixel"));
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsRefused() {
    var sound = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Width = 4, Height = 4 };

    Assert.Throws<NotSupportedException>(() => QuickTimeRleEncoder.Create(sound));
  }

  [Test]
  [Category("Unit")]
  public void APictureOfAnotherDepthThanTheStreamIsRefused() {
    var encoder = QuickTimeRleEncoder.Create(_Requested(4, 4, 32));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Rgb(4, 4, 1), 0, out _));
    Assert.That(failure!.Message, Does.Contain("cannot change between frames"));
  }

  [Test]
  [Category("Unit")]
  public void APalettisedPictureWithoutAPaletteIsRefused() {
    var encoder = QuickTimeRleEncoder.Create(_Requested(4, 4));
    var bare = new RawImage { Width = 4, Height = 4, Format = PixelFormat.Indexed8, PixelData = new byte[16] };

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(bare, 0, out _));
    Assert.That(failure!.Message, Does.Contain("without a palette"));
  }

  [Test]
  [Category("Unit")]
  public void AnIndexPastTheEndOfThePaletteIsRefused() {
    var encoder = QuickTimeRleEncoder.Create(_Requested(4, 1));
    var picture = _Indexed(4, 1, 0, 16);
    picture.PixelData[2] = 16;

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure!.Message, Does.Contain("index 16"));
  }

  [Test]
  [Category("Unit")]
  public void APaletteThatChangesBetweenFramesIsRefused() {
    var encoder = QuickTimeRleEncoder.Create(_Requested(4, 4));
    Assert.That(encoder.TryEncode(_Indexed(4, 4, 1, 16), 0, out _), Is.True);

    var other = _Indexed(4, 4, 1, 16);
    other.Palette![0] ^= 0xFF;

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(other, 1, out _));
    Assert.That(failure!.Message, Does.Contain("different palette"));
  }

  [Test]
  [Category("Unit")]
  public void AGeometryChangeMidStreamIsRefused() {
    var encoder = QuickTimeRleEncoder.Create(_Requested(8, 8, 24));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Rgb(4, 4, 1), 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x8"));
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static void _AssertRoundTrip(int width, int height, IReadOnlyList<RawImage> pictures, bool[]? expectedKeyFrames) {
    var encoder = QuickTimeRleEncoder.Create(_Requested(width, height));
    var packets = new List<CodedPacket>();
    for (var i = 0; i < pictures.Count; ++i) {
      Assert.That(encoder.TryEncode(pictures[i], i, out var packet), Is.True);
      packets.Add(packet);
    }

    var stream = encoder.DescribeStream();
    var decoder = VideoFormatRegistry.CreateDecoder(stream);
    Assert.That(decoder, Is.InstanceOf<QuickTimeRleDecoder>());

    for (var i = 0; i < pictures.Count; ++i) {
      Assert.That(decoder.TryDecode(packets[i], out var decoded), Is.True);
      Assert.Multiple(() => {
        Assert.That(decoded.Width, Is.EqualTo(width));
        Assert.That(decoded.Height, Is.EqualTo(height));
        Assert.That(decoded.Format, Is.EqualTo(pictures[i].Format));
        Assert.That(decoded.PixelData, Is.EqualTo(pictures[i].PixelData), $"frame {i}");
        if (pictures[i].Palette != null)
          Assert.That(
            decoded.Palette!.AsSpan(0, pictures[i].PaletteCount * 3).ToArray(),
            Is.EqualTo(pictures[i].Palette!.AsSpan(0, pictures[i].PaletteCount * 3).ToArray()),
            $"palette of frame {i}");
      });
    }

    Assert.That(packets[0].IsKeyFrame, Is.True, "the first frame is written whole");
    if (expectedKeyFrames != null)
      Assert.That(packets.Take(expectedKeyFrames.Length).Select(p => p.IsKeyFrame), Is.EqualTo(expectedKeyFrames));
  }

  /// <summary>
  /// Six pictures: a random one, the same with a block changed, the same with a few lines changed,
  /// the same again with nothing changed, one with a single pixel changed, and a fully random one.
  /// </summary>
  private static List<RawImage> _Sequence(int width, int height, PixelFormat format, int seed) {
    var random = new Random(seed);
    var bytesPerPixel = RawImage.BytesPerPixel(format);
    var colours = format == PixelFormat.Indexed8 ? 16 : 256;
    var stride = width * bytesPerPixel;

    var first = new byte[stride * height];
    for (var i = 0; i < first.Length; ++i)
      first[i] = (byte)random.Next(0, colours);

    var block = (byte[])first.Clone();
    for (var y = height / 3; y < Math.Max(height / 3 + 1, height * 2 / 3); ++y)
    for (var x = width / 4 * bytesPerPixel; x < Math.Max(width / 4 + 1, width * 3 / 4) * bytesPerPixel; ++x)
      block[y * stride + x] = (byte)((block[y * stride + x] + 1) % colours);

    var rows = (byte[])block.Clone();
    for (var x = 0; x < stride; ++x)
      rows[x] = (byte)random.Next(0, colours);

    var pixel = (byte[])rows.Clone();
    pixel[^1] = (byte)((pixel[^1] + 1) % colours);

    var last = new byte[stride * height];
    for (var i = 0; i < last.Length; ++i)
      last[i] = (byte)random.Next(0, colours);

    return
    [
      _Picture(width, height, first, format),
      _Picture(width, height, block, format),
      _Picture(width, height, rows, format),
      _Picture(width, height, rows, format),
      _Picture(width, height, pixel, format),
      _Picture(width, height, last, format),
    ];
  }

  /// <summary>
  /// What the first five packets of <see cref="_Sequence"/> are flagged as: the first is whole, and
  /// the four after it skip something — unless the picture is a single pixel, in which case a change
  /// is every pixel changing and the frame is whole too. The frame that changes nothing is never a
  /// key frame, because it is the empty frame and not a picture.
  /// </summary>
  private static bool[] _KeyFrames(int width, int height)
    => width * height > 1 ? [true, false, false, false, false] : [true, true, true, false, true];

  private static RawImage _Picture(int width, int height, byte[] pixels, PixelFormat format) => new() {
    Width = width,
    Height = height,
    Format = format,
    PixelData = pixels,
    Palette = format == PixelFormat.Indexed8 ? _Palette(16) : null,
    PaletteCount = format == PixelFormat.Indexed8 ? 16 : 0,
  };

  private static RawImage _Rgb(int width, int height, byte fill) {
    var pixels = new byte[width * height * 3];
    Array.Fill(pixels, fill);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _Indexed(int width, int height, byte fill, int colours) {
    var pixels = new byte[width * height];
    Array.Fill(pixels, fill);
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _Palette(colours),
      PaletteCount = colours,
    };
  }

  private static byte[] _Palette(int colours) {
    var palette = new byte[colours * 3];
    for (var i = 0; i < colours; ++i) {
      palette[i * 3] = (byte)(i * 3);
      palette[i * 3 + 1] = (byte)(i * 3 + 1);
      palette[i * 3 + 2] = (byte)(i * 3 + 2);
    }

    return palette;
  }

  private static MediaStreamInfo _Requested(int width, int height, int bitsPerPixel = 0, int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("avc1"),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };
}
