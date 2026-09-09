using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Bpg;
using FileFormat.Core;
using Hawkynt.FileFormats.Images.Tests;
using NUnit.Framework;

namespace FileFormat.Bpg.Tests;

/// <summary>
/// What the writer puts in a BPG file, judged by the format's own specification and by the decoder
/// its author wrote.
/// </summary>
/// <remarks>
/// This format has a history that makes the second half of that sentence the whole point. The writer
/// this package used to have stored the caller's RGB bytes under a <c>YCbCr444</c> label with no
/// transform of any kind, and its round-trip test passed because the decode path caught its own
/// failure and handed the stored bytes straight back — two halves of one package agreeing with each
/// other about a file no other program could open. So the check that matters here is
/// <c>bpgdec</c>: Fabrice Bellard's own decoder, reading a file nothing of ours touched afterwards.
/// The round trip through our reader is kept as well, but it is the weaker of the two and is written
/// second on purpose.
/// </remarks>
[TestFixture]
public sealed class WriterConformanceTests {

  private static RawImage _Plasma(int width, int height) {
    var pixels = new byte[width * height * 3];
    var random = new Random(20260909);
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)((24 + 200L * x / Math.Max(1, width - 1)) ^ random.Next(0, 64));
        pixels[at + 1] = (byte)((32 + 176L * y / Math.Max(1, height - 1)) ^ random.Next(0, 64));
        pixels[at + 2] = (byte)((40 + 160L * (x + y) / Math.Max(1, width + height - 2)) ^ random.Next(0, 64));
      }

    return new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  /// <summary>The sizes worth trying: the tree block, one short of it, one past it, and neither.</summary>
  private static readonly (int Width, int Height)[] _Sizes = [
    (1, 1), (7, 5), (31, 31), (32, 32), (33, 17), (33, 33), (64, 48), (96, 80), (129, 3), (160, 120),
  ];

  /// <summary>
  /// The decode nothing of ours took part in. PCM coding units keep every sample and BPG's RGB
  /// colour space applies no matrix, so anything short of equality is a defect rather than a
  /// rounding.
  /// </summary>
  [Test]
  [Category("Conformance")]
  public void WrittenFile_DecodesInBpgdecToThePixelsItWasGiven() {
    var directory = Directory.CreateTempSubdirectory("bpg-writer-bpgdec");
    try {
      foreach (var (width, height) in _Sizes) {
        var source = _Plasma(width, height);
        var bpg = Path.Combine(directory.FullName, $"written-{width}x{height}.bpg");
        var ppm = Path.Combine(directory.FullName, $"decoded-{width}x{height}.ppm");
        File.WriteAllBytes(bpg, BpgWriter.ToBytes(BpgFile.FromRawImage(source)));

        using (var decode = ExternalTool.StartOrIgnore("bpgdec", $"-o \"{ppm}\" \"{bpg}\"")) {
          var complaint = decode.StandardError.ReadToEnd();
          decode.WaitForExit();
          Assert.That(decode.ExitCode, Is.Zero,
            $"bpgdec refused the {width} by {height} file: {complaint.Trim()}");
        }

        var decoded = _ReadPortablePixmap(ppm);
        Assert.That((decoded.Width, decoded.Height), Is.EqualTo((width, height)),
          "bpgdec must decode the picture at the size the writer stated");

        var differing = 0;
        for (var i = 0; i < source.PixelData.Length; ++i)
          if (source.PixelData[i] != decoded.Pixels[i])
            ++differing;

        Assert.That(differing, Is.Zero,
          $"{width}x{height}: {differing} of {source.PixelData.Length} samples came back changed");
      }
    } finally {
      try { directory.Delete(recursive: true); } catch (IOException) { /* the temp tree is the OS's problem now */ }
    }
  }

  /// <summary>
  /// The header has to describe the picture that follows it, because a BPG file carries no sequence
  /// parameter set and a decoder has nowhere else to learn any of this from.
  /// </summary>
  [Test]
  [Category("Conformance")]
  public void WrittenHeader_StatesTheProfileTheEncoderActuallyCoded() {
    var bytes = BpgWriter.ToBytes(BpgFile.FromRawImage(_Plasma(96, 80)));

    Assert.Multiple(() => {
      Assert.That(bytes[..4], Is.EqualTo(new byte[] { 0x42, 0x50, 0x47, 0xFB }));
      Assert.That(bytes[4] >> 5, Is.EqualTo(3), "pixel_format 3 is 4:4:4");
      Assert.That((bytes[4] >> 4) & 1, Is.Zero, "alpha1_flag: no alpha plane is written");
      Assert.That(bytes[4] & 0x0F, Is.Zero, "bit_depth_minus_8: eight bits a sample");
      Assert.That(bytes[5] >> 4, Is.EqualTo(1), "color_space 1 is RGB, stored as G, B and R");
      Assert.That(bytes[5] & 0x0F, Is.Zero,
        "extension_present_flag, alpha2_flag, limited_range_flag and animation_flag are all clear");
    });

    var file = BpgReader.FromBytes(bytes);
    Assert.That(bytes[^file.PixelData.Length..], Is.EqualTo(file.PixelData),
      "picture_data_length covers hevc_header_and_data() whole and it runs to the end of the file");
  }

  /// <summary>
  /// The sequence fields BPG keeps in its own header. Two of them are not free choices: a decoder
  /// derives the coded picture size from log2_min_luma_coding_block_size and nothing else, so the
  /// minimum coding block has to be the tree block for every tree block to lie inside the picture,
  /// and clause 7.4.3.2.1 caps a PCM coding block at Min(CtbLog2SizeY, 5) either way.
  /// </summary>
  [Test]
  [Category("Conformance")]
  public void WrittenSequenceHeader_CodesWholePcmTreeBlocksAtEightBits() {
    var bytes = BpgWriter.ToBytes(BpgFile.FromRawImage(_Plasma(96, 80)));
    var picture = BpgReader.FromBytes(bytes).PixelData;

    // hevc_header_length, then log2_min_luma_coding_block_size_minus3 and the rest as ue(v)/u(n).
    // Twenty-eight bits of fields and four of zero padding, so one byte states the length.
    Assert.That(picture[0], Is.EqualTo(4), "the header this profile writes is four bytes long");

    var bits = new BitCursor(picture.AsSpan(1, picture[0]));
    var log2MinCb = bits.ReadUnsignedExpGolomb() + 3;
    var log2DiffMaxMinCb = bits.ReadUnsignedExpGolomb();
    var log2MinTb = bits.ReadUnsignedExpGolomb() + 2;
    var log2MaxTb = log2MinTb + bits.ReadUnsignedExpGolomb();
    var transformDepth = bits.ReadUnsignedExpGolomb();
    var sao = bits.ReadBit();
    var pcm = bits.ReadBit();
    var pcmDepthLuma = bits.ReadBits(4) + 1;
    var pcmDepthChroma = bits.ReadBits(4) + 1;
    var log2MinPcm = bits.ReadUnsignedExpGolomb() + 3;
    var log2DiffMaxMinPcm = bits.ReadUnsignedExpGolomb();
    var pcmLoopFilterDisabled = bits.ReadBit();
    var strongIntraSmoothing = bits.ReadBit();
    var spsExtension = bits.ReadBit();

    Assert.Multiple(() => {
      Assert.That(log2MinCb, Is.EqualTo(5), "log2_min_luma_coding_block_size");
      Assert.That(log2DiffMaxMinCb, Is.Zero, "the tree block is the minimum coding block");
      Assert.That(log2MinTb, Is.EqualTo(2), "log2_min_transform_block_size");
      Assert.That(log2MaxTb, Is.EqualTo(5), "log2_max_transform_block_size");
      Assert.That(transformDepth, Is.Zero, "max_transform_hierarchy_depth_intra");
      Assert.That(sao, Is.Zero, "sample_adaptive_offset_enabled_flag");
      Assert.That(pcm, Is.EqualTo(1), "pcm_enabled_flag");
      Assert.That(pcmDepthLuma, Is.EqualTo(8), "pcm_sample_bit_depth_luma");
      Assert.That(pcmDepthChroma, Is.EqualTo(8), "pcm_sample_bit_depth_chroma");
      Assert.That(log2MinPcm, Is.EqualTo(5), "log2_min_pcm_luma_coding_block_size");
      Assert.That(log2DiffMaxMinPcm, Is.Zero, "log2_diff_max_min_pcm_luma_coding_block_size");
      Assert.That(log2MinPcm, Is.LessThanOrEqualTo(Math.Min(log2MinCb + log2DiffMaxMinCb, 5)),
        "clause 7.4.3.2.1: Log2MaxIpcmCbSizeY <= Min(CtbLog2SizeY, 5)");
      Assert.That(pcmLoopFilterDisabled, Is.EqualTo(1), "pcm_loop_filter_disabled_flag");
      Assert.That(strongIntraSmoothing, Is.Zero, "strong_intra_smoothing_enabled_flag");
      Assert.That(spsExtension, Is.Zero, "sps_extension_present_flag");
    });
  }

  /// <summary>
  /// The specification says the picture data begins after the first NAL's start code rather than at
  /// it, and that the video and sequence parameter sets shall not be in there at all.
  /// </summary>
  [Test]
  [Category("Conformance")]
  public void WrittenPictureData_OmitsTheFirstStartCodeAndBothParameterSets() {
    var picture = BpgReader.FromBytes(BpgWriter.ToBytes(BpgFile.FromRawImage(_Plasma(64, 48)))).PixelData;
    var at = 1 + picture[0];

    Assert.That((picture[at] >> 1) & 0x3F, Is.EqualTo(34),
      "the first NAL is the picture parameter set, and it is not preceded by a start code");

    var starts = 0;
    var types = new List<int>();
    for (var i = at; i + 4 < picture.Length; ++i)
      if (picture[i] == 0 && picture[i + 1] == 0 && picture[i + 2] == 1) {
        ++starts;
        types.Add((picture[i + 3] >> 1) & 0x3F);
      }

    Assert.Multiple(() => {
      Assert.That(starts, Is.EqualTo(1), "one start code, in front of the slice");
      Assert.That(types, Does.Not.Contain(32).And.Not.Contain(33),
        "neither a video nor a sequence parameter set belongs in BPG picture data");
      Assert.That(types[0], Is.EqualTo(20), "the coded picture is an IDR with no leading pictures");
    });
  }

  /// <summary>The weaker check, kept because a format the package cannot re-read is still broken.</summary>
  [Test]
  [Category("Integration")]
  public void WrittenFile_ReadsBackThroughOurOwnReaderUnchanged() {
    foreach (var (width, height) in _Sizes) {
      var source = _Plasma(width, height);
      var read = BpgFile.ToRawImage(BpgReader.FromBytes(BpgWriter.ToBytes(BpgFile.FromRawImage(source))));

      Assert.That((read.Width, read.Height), Is.EqualTo((width, height)));
      Assert.That(read.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(read.PixelData, Is.EqualTo(source.PixelData), $"{width}x{height} changed across a round trip");
    }
  }

  /// <summary>
  /// A picture whose samples are mostly zero is the one that exercises emulation prevention: three
  /// zero bytes in a row inside a NAL would end it, so the encoder has to break them up.
  /// </summary>
  [Test]
  [Category("Conformance")]
  public void WrittenFile_EscapesTheZeroRunsThatWouldOtherwiseEndTheNal() {
    var black = new RawImage {
      Width = 64, Height = 64, Format = PixelFormat.Rgb24, PixelData = new byte[64 * 64 * 3],
    };

    var bytes = BpgWriter.ToBytes(BpgFile.FromRawImage(black));
    var read = BpgFile.ToRawImage(BpgReader.FromBytes(bytes));

    Assert.That(bytes.Length, Is.GreaterThan(64 * 64 * 3),
      "an all-zero picture must be larger than its samples, because every zero run is escaped");
    Assert.That(read.PixelData, Is.EqualTo(black.PixelData));
  }

  /// <summary>Header fields the container cannot spell are refused by name rather than truncated.</summary>
  [Test]
  [Category("Unit")]
  public void Header_RefusesTheCombinationsBpgHasNoBitsFor() {
    Assert.Multiple(() => {
      Assert.Throws<NotSupportedException>(() => BpgWriter.ToBytes(new BpgFile {
        Width = 4, Height = 4, BitDepth = 16,
        PixelFormat = BpgPixelFormat.YCbCr444, ColorSpace = BpgColorSpace.Rgb, PixelData = [0],
      }), "bit_depth_minus_8 is four bits and the specification caps it well below sixteen");

      Assert.Throws<NotSupportedException>(() => BpgWriter.ToBytes(new BpgFile {
        Width = 4, Height = 4, BitDepth = 8,
        PixelFormat = BpgPixelFormat.Grayscale, ColorSpace = BpgColorSpace.Rgb, PixelData = [0],
      }), "one plane leaves no colour matrix to name");

      Assert.Throws<ArgumentOutOfRangeException>(() => BpgWriter.ToBytes(new BpgFile {
        Width = 0, Height = 4, BitDepth = 8,
        PixelFormat = BpgPixelFormat.YCbCr444, ColorSpace = BpgColorSpace.Rgb, PixelData = [0],
      }), "picture_width of zero is not allowed");
    });
  }

  private static (int Width, int Height, byte[] Pixels) _ReadPortablePixmap(string path) {
    var bytes = File.ReadAllBytes(path);
    var at = 0;

    string Token() {
      while (at < bytes.Length && (char.IsWhiteSpace((char)bytes[at]) || bytes[at] == '#'))
        if (bytes[at] == '#')
          while (at < bytes.Length && bytes[at] != '\n')
            ++at;
        else
          ++at;

      var start = at;
      while (at < bytes.Length && !char.IsWhiteSpace((char)bytes[at]))
        ++at;

      return Encoding.ASCII.GetString(bytes, start, at - start);
    }

    Assert.That(Token(), Is.EqualTo("P6"), "bpgdec was asked for a binary portable pixmap");
    var width = int.Parse(Token());
    var height = int.Parse(Token());
    Assert.That(int.Parse(Token()), Is.EqualTo(255), "eight bits a sample");
    ++at; // the single whitespace byte between the header and the samples

    var pixels = new byte[width * height * 3];
    Array.Copy(bytes, at, pixels, 0, Math.Min(pixels.Length, bytes.Length - at));
    return (width, height, pixels);
  }

  /// <summary>Enough of a bit reader to check the header this writer wrote, and no more.</summary>
  private ref struct BitCursor(ReadOnlySpan<byte> data) {

    private readonly ReadOnlySpan<byte> _data = data;
    private int _position;

    internal int ReadBit() {
      var position = this._position++;
      return (this._data[position >> 3] >> (7 - (position & 7))) & 1;
    }

    internal int ReadBits(int count) {
      var value = 0;
      for (var i = 0; i < count; ++i)
        value = (value << 1) | this.ReadBit();

      return value;
    }

    internal int ReadUnsignedExpGolomb() {
      var zeroes = 0;
      while (this.ReadBit() == 0)
        ++zeroes;

      return zeroes == 0 ? 0 : (1 << zeroes) - 1 + this.ReadBits(zeroes);
    }
  }
}
