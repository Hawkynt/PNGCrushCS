using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Microsoft RLE video encoder, checked by decoding what it writes with the decoder beside it.
/// </summary>
/// <remarks>
/// The coding is lossless, so the whole of the contract is that the decoder gets the input back
/// exactly — over a sequence, so that the delta and skip escapes are reached, and over pictures that
/// are random, so that every opcode is reached at every length. The stream description is checked
/// against what the decoder reads out of it rather than against a hexdump, because that is the
/// description a muxer will be handed.
/// </remarks>
[TestFixture]
public sealed class MicrosoftRleEncoderTests {

  private const int _BI_RLE8 = 1;
  private const int _BI_RLE4 = 2;

  // ============================================================================================
  // The description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheDescriptionIsWhatTheDecoderReads() {
    var encoder = MicrosoftRleEncoder.Create(_Requested(8, 4));
    Assert.That(encoder.TryEncode(_Picture(8, 4, 1, 16), 0, out _), Is.True);

    var stream = encoder.DescribeStream();
    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("MRLE")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("MRLE")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(stream.Width, Is.EqualTo(8));
      Assert.That(stream.Height, Is.EqualTo(4));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(8));
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(25, 1)));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(40 + 16 * 4));
    });

    var format = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan()), Is.EqualTo(40), "biSize");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(4)), Is.EqualTo(8), "biWidth");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(8)), Is.EqualTo(4), "biHeight, positive for bottom-up");
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(format.AsSpan(14)), Is.EqualTo(8), "biBitCount");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(16)), Is.EqualTo(_BI_RLE8), "biCompression");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(32)), Is.EqualTo(16), "biClrUsed");
    });

    // Entry 3 of the palette built below is R=9, G=10, B=11; the quad is blue first.
    Assert.That(format[(40 + 3 * 4)..(40 + 4 * 4)], Is.EqualTo(new byte[] { 11, 10, 9, 0 }));

    Assert.That(MicrosoftRleDecoder.Accepts(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<MicrosoftRleDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void TheDescriptionCannotBeGivenBeforeThePaletteIsKnown() {
    var encoder = MicrosoftRleEncoder.Create(_Requested(4, 4));

    var failure = Assert.Throws<InvalidOperationException>(() => encoder.DescribeStream());
    Assert.That(failure!.Message, Does.Contain("palette"));
  }

  [Test]
  [Category("Unit")]
  public void ADescriptionThatAlreadyCarriesAPaletteCanBeGivenBeforeTheFirstPicture() {
    // The stream a demuxer produced, handed straight back to the encoder for a re-encode.
    var first = MicrosoftRleEncoder.Create(_Requested(6, 3));
    Assert.That(first.TryEncode(_Picture(6, 3, 5, 16), 0, out _), Is.True);
    var described = first.DescribeStream();

    var second = MicrosoftRleEncoder.Create(described);
    var again = second.DescribeStream();

    Assert.That(again.CodecPrivateData.ToArray(), Is.EqualTo(described.CodecPrivateData.ToArray()));
    Assert.That(again.BitsPerPixel, Is.EqualTo(8));
  }

  // ============================================================================================
  // The round trip
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASequenceWithPartialChangesRoundTripsExactly([Values(1, 7, 64, 300, 5)] int width) {
    var height = width switch { 1 => 1, 7 => 5, 64 => 40, 300 => 3, _ => 9 };
    var pictures = _Sequence(width, height, 16, PixelFormat.Indexed8, seed: width);

    _AssertRoundTrip(width, height, pictures, expectedIndices: p => p.PixelData, expectedKeyFrames: _KeyFrames(width, height));
  }

  [Test]
  [Category("Unit")]
  public void AWholePictureOfUnchangedRowsCollapsesIntoAVerticalDeltaAndStillRoundTrips() {
    // 300 rows, of which only the topmost changes: 299 unchanged rows in a row, which is more than
    // one delta escape can step, and then the decoder must land on the right row.
    var pictures = new List<RawImage> { _Picture(8, 300, 3, 4) };
    var changed = (byte[])pictures[0].PixelData.Clone();
    for (var x = 0; x < 8; ++x)
      changed[x] = (byte)((changed[x] + 1) & 3);
    pictures.Add(_Picture(8, 300, changed, 4));

    _AssertRoundTrip(8, 300, pictures, expectedIndices: p => p.PixelData, expectedKeyFrames: [true, false]);
  }

  [Test]
  [Category("Unit")]
  public void ARowWithMoreThanTwoHundredAndFiftyFiveUnchangedPixelsRoundTrips() {
    // A horizontal delta steps at most 255 columns; a row 600 wide with one changed pixel at each
    // end needs two of them and then the decoder must land on the right column.
    var pictures = new List<RawImage> { _Picture(600, 2, 7, 8) };
    var changed = (byte[])pictures[0].PixelData.Clone();
    changed[0] ^= 1;
    changed[599] ^= 1;
    changed[600] ^= 1;
    pictures.Add(_Picture(600, 2, changed, 8));

    _AssertRoundTrip(600, 2, pictures, expectedIndices: p => p.PixelData, expectedKeyFrames: [true, false]);
  }

  [Test]
  [Category("Unit")]
  public void FullyRandomPicturesAtEveryRunLengthRoundTrip() {
    // Long runs and long literal stretches both: rows of 1000 make runs longer than 255 and
    // literal stretches longer than 254, so every opcode is split at least once.
    var random = new Random(11);
    var pictures = new List<RawImage>();
    for (var frame = 0; frame < 4; ++frame) {
      var pixels = new byte[1000 * 3];
      for (var i = 0; i < pixels.Length;) {
        var length = random.Next(1, 400);
        var value = (byte)random.Next(0, frame == 0 ? 2 : 16);
        for (var n = 0; n < length && i < pixels.Length; ++n, ++i)
          pixels[i] = frame == 3 ? (byte)random.Next(0, 16) : value;
      }

      pictures.Add(_Picture(1000, 3, pixels, 16));
    }

    _AssertRoundTrip(1000, 3, pictures, expectedIndices: p => p.PixelData, expectedKeyFrames: null);
  }

  [Test]
  [Category("Unit")]
  public void FourBitPicturesAreWrittenAsRle4AndRoundTrip([Values(8, 7, 33)] int width) {
    var height = width switch { 8 => 4, 7 => 5, _ => 6 };
    var pictures = _Sequence(width, height, 16, PixelFormat.Indexed4, seed: width);

    var encoder = MicrosoftRleEncoder.Create(_Requested(width, height));
    var packets = pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();

    var stream = encoder.DescribeStream();
    Assert.That(stream.BitsPerPixel, Is.EqualTo(4));
    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(stream.CodecPrivateData.Span[16..]), Is.EqualTo(_BI_RLE4));

    var decoder = VideoFormatRegistry.CreateDecoder(stream);
    for (var i = 0; i < pictures.Count; ++i) {
      Assert.That(decoder.TryDecode(packets[i], out var decoded), Is.True);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed8), "the decoder unpacks nibbles to a byte each");
      Assert.That(decoded.PixelData, Is.EqualTo(_Unpacked(pictures[i])), $"frame {i}");
      Assert.That(decoded.Palette!.AsSpan(0, 16 * 3).ToArray(), Is.EqualTo(pictures[i].Palette!.AsSpan(0, 16 * 3).ToArray()));
    }

    Assert.That(packets[0].IsKeyFrame, Is.True);
    Assert.That(packets[1].IsKeyFrame, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheFramesSurviveAnAviAndComeBackThroughTheRegistry() {
    var pictures = _Sequence(20, 12, 16, PixelFormat.Indexed8, seed: 3);
    var encoder = MicrosoftRleEncoder.Create(_Requested(20, 12));
    var packets = pictures.Select((picture, i) => {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      return packet;
    }).ToList();

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets);
    var container = AviContainer.FromBytes(avi);
    var stream = AviContainer.Streams(container).Single();
    Assert.That(stream.Codec.Value, Is.EqualTo((uint)_BI_RLE8), "an AVI names this codec by its biCompression");

    var decoded = VideoFormatRegistry.DecodeFrames(avi).Select(f => f.Image).ToList();
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
    var encoder = MicrosoftRleEncoder.Create(_Requested(4, 4, index: 3));
    var picture = _Picture(4, 4, 2, 4);

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
  public void APictureIdenticalToTheOneBeforeIsOneVerticalDeltaAndNotAKeyFrame() {
    var encoder = MicrosoftRleEncoder.Create(_Requested(16, 8));
    var picture = _Picture(16, 8, 9, 16);

    Assert.That(encoder.TryEncode(picture, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(picture, 1, out var second), Is.True);

    Assert.Multiple(() => {
      Assert.That(first.IsKeyFrame, Is.True);
      Assert.That(second.IsKeyFrame, Is.False);
      // 00 02 00 07 steps seven rows, the end-of-line is the eighth, and 00 01 ends the bitmap.
      Assert.That(second.Data.ToArray(), Is.EqualTo(new byte[] { 0, 2, 0, 7, 0, 0, 0, 1 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void NothingIsHeldBack() {
    var encoder = MicrosoftRleEncoder.Create(_Requested(4, 4));
    Assert.That(encoder.TryEncode(_Picture(4, 4, 2, 4), 0, out _), Is.True);

    Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
  }

  // ============================================================================================
  // The refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ADirectColourPictureIsRefusedByName() {
    var encoder = MicrosoftRleEncoder.Create(_Requested(4, 4));
    var rgb = new RawImage { Width = 4, Height = 4, Format = PixelFormat.Rgb24, PixelData = new byte[4 * 4 * 3] };

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(rgb, 0, out _));
    Assert.That(failure!.Message, Does.Contain("Rgb24"));
  }

  [Test]
  [Category("Unit")]
  public void ADepthTheCodingIsNotDefinedAtIsRefusedByName() {
    var failure = Assert.Throws<NotSupportedException>(() => MicrosoftRleEncoder.Create(_Requested(4, 4, bitsPerPixel: 24)));
    Assert.That(failure!.Message, Does.Contain("24 bits per pixel"));
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsRefused() {
    var sound = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Width = 4, Height = 4 };

    Assert.Throws<NotSupportedException>(() => MicrosoftRleEncoder.Create(sound));
  }

  [Test]
  [Category("Unit")]
  public void APalettisedPictureWithoutAPaletteIsRefused() {
    var encoder = MicrosoftRleEncoder.Create(_Requested(4, 4));
    var bare = new RawImage { Width = 4, Height = 4, Format = PixelFormat.Indexed8, PixelData = new byte[16] };

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(bare, 0, out _));
    Assert.That(failure!.Message, Does.Contain("without a palette"));
  }

  [Test]
  [Category("Unit")]
  public void AnIndexPastTheEndOfThePaletteIsRefused() {
    var encoder = MicrosoftRleEncoder.Create(_Requested(4, 1));
    var pixels = new byte[] { 0, 1, 2, 16 };

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Picture(4, 1, pixels, 16), 0, out _));
    Assert.That(failure!.Message, Does.Contain("index 16"));
  }

  [Test]
  [Category("Unit")]
  public void APaletteThatChangesBetweenFramesIsRefused() {
    var encoder = MicrosoftRleEncoder.Create(_Requested(4, 4));
    Assert.That(encoder.TryEncode(_Picture(4, 4, 1, 16), 0, out _), Is.True);

    var other = _Picture(4, 4, 1, 16);
    other.Palette![0] ^= 0xFF;

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(other, 1, out _));
    Assert.That(failure!.Message, Does.Contain("different palette"));
  }

  [Test]
  [Category("Unit")]
  public void ADepthThatChangesBetweenFramesIsRefused() {
    var encoder = MicrosoftRleEncoder.Create(_Requested(8, 2));
    Assert.That(encoder.TryEncode(_Picture(8, 2, 1, 16), 0, out _), Is.True);

    var four = new RawImage {
      Width = 8, Height = 2, Format = PixelFormat.Indexed4, PixelData = new byte[8], Palette = _Palette(16), PaletteCount = 16,
    };

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(four, 1, out _));
    Assert.That(failure!.Message, Does.Contain("cannot change between frames"));
  }

  [Test]
  [Category("Unit")]
  public void AGeometryChangeMidStreamIsRefused() {
    var encoder = MicrosoftRleEncoder.Create(_Requested(8, 8));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Picture(4, 4, 1, 16), 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x8"));
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static void _AssertRoundTrip(
    int width,
    int height,
    IReadOnlyList<RawImage> pictures,
    Func<RawImage, byte[]> expectedIndices,
    bool[]? expectedKeyFrames) {
    var encoder = MicrosoftRleEncoder.Create(_Requested(width, height));
    var packets = new List<CodedPacket>();
    for (var i = 0; i < pictures.Count; ++i) {
      Assert.That(encoder.TryEncode(pictures[i], i, out var packet), Is.True);
      packets.Add(packet);
    }

    var stream = encoder.DescribeStream();
    var decoder = VideoFormatRegistry.CreateDecoder(stream);
    Assert.That(decoder, Is.InstanceOf<MicrosoftRleDecoder>());

    for (var i = 0; i < pictures.Count; ++i) {
      Assert.That(decoder.TryDecode(packets[i], out var decoded), Is.True);
      Assert.Multiple(() => {
        Assert.That(decoded.Width, Is.EqualTo(width));
        Assert.That(decoded.Height, Is.EqualTo(height));
        Assert.That(decoded.PixelData, Is.EqualTo(expectedIndices(pictures[i])), $"frame {i}");
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
  /// Six pictures: a random one, the same with a block changed, the same with a few rows changed,
  /// the same again with nothing changed, one with a single pixel changed, and a fully random one.
  /// </summary>
  private static List<RawImage> _Sequence(int width, int height, int colours, PixelFormat format, int seed) {
    var random = new Random(seed);
    var first = new byte[width * height];
    for (var i = 0; i < first.Length; ++i)
      first[i] = (byte)random.Next(0, colours);

    var block = (byte[])first.Clone();
    for (var y = height / 3; y < Math.Max(height / 3 + 1, height * 2 / 3); ++y)
    for (var x = width / 4; x < Math.Max(width / 4 + 1, width * 3 / 4); ++x)
      block[y * width + x] = (byte)((block[y * width + x] + 1) % colours);

    var rows = (byte[])block.Clone();
    for (var x = 0; x < width; ++x)
      rows[x] = (byte)random.Next(0, colours);

    var pixel = (byte[])rows.Clone();
    pixel[^1] = (byte)((pixel[^1] + 1) % colours);

    var last = new byte[width * height];
    for (var i = 0; i < last.Length; ++i)
      last[i] = (byte)random.Next(0, colours);

    return
    [
      _Picture(width, height, first, colours, format),
      _Picture(width, height, block, colours, format),
      _Picture(width, height, rows, colours, format),
      _Picture(width, height, rows, colours, format),
      _Picture(width, height, pixel, colours, format),
      _Picture(width, height, last, colours, format),
    ];
  }

  /// <summary>
  /// What the first five packets of <see cref="_Sequence"/> are flagged as: the first is whole, and
  /// the four after it skip something — unless the picture is too small to hold the five unchanged
  /// pixels a skip needs, in which case every pixel is written again and the frame is whole too.
  /// </summary>
  private static bool[]? _KeyFrames(int width, int height)
    => width >= 5 && height >= 2 ? [true, false, false, false, false] : null;

  private static RawImage _Picture(int width, int height, byte fill, int colours) {
    var pixels = new byte[width * height];
    Array.Fill(pixels, fill);
    return _Picture(width, height, pixels, colours);
  }

  /// <summary>A picture from one index per pixel, packed to nibbles where the format asks for it.</summary>
  private static RawImage _Picture(int width, int height, byte[] indices, int colours, PixelFormat format = PixelFormat.Indexed8) {
    byte[] pixels;
    if (format == PixelFormat.Indexed8)
      pixels = indices;
    else {
      pixels = new byte[(indices.Length + 1) / 2];
      for (var i = 0; i < indices.Length; ++i)
        pixels[i >> 1] |= (byte)((i & 1) == 0 ? indices[i] << 4 : indices[i]);
    }

    return new() {
      Width = width,
      Height = height,
      Format = format,
      PixelData = pixels,
      Palette = _Palette(colours),
      PaletteCount = colours,
    };
  }

  private static byte[] _Unpacked(RawImage picture) {
    var indices = new byte[picture.Width * picture.Height];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = (byte)((i & 1) == 0 ? picture.PixelData[i >> 1] >> 4 : picture.PixelData[i >> 1] & 0x0F);

    return indices;
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
