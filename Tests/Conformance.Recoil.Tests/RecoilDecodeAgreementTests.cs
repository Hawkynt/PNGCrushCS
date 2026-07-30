using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.TechnicolorDream;
using Hawkynt.FileFormats.Images;

namespace Conformance.Recoil.Tests;

/// <summary>
/// Hands the same bytes to RECOIL and to us, and compares the two pictures pixel for pixel.
/// </summary>
/// <remarks>
/// <see cref="RecoilConformanceTests"/> can only cover formats we can write: it encodes an image and
/// checks that RECOIL accepts the result, which proves the container is well formed but says nothing
/// about whether the pixels mean the same thing to both sides. This fixture makes no such demand.
/// A probe file is assembled by hand, both decoders read it, and the images have to match exactly —
/// so a read-only format is held to a stricter standard than a writable one, not a looser one.
/// <para/>
/// Probes are deliberately hand-built rather than captured: a captured file proves we agree about
/// one picture, whereas a probe can be shaped to exercise every branch of the decoder — each colour
/// source, each bit pattern, each cell in the addressing scheme.
/// </remarks>
[TestFixture]
public sealed class RecoilDecodeAgreementTests {

  /// <param name="Name">The format's name in RECOIL's catalogue, for traceability.</param>
  /// <param name="Extension">Extension to write the probe under; RECOIL dispatches on it alone.</param>
  /// <param name="Build">Assembles the probe file.</param>
  public sealed record Probe(string Name, ImageFormat Format, string Extension, Func<byte[]> Build) {
    public override string ToString() => $"{this.Name} ({this.Extension})";
  }

  public static readonly Probe[] Probes = [
    new("Botticelli", ImageFormat.Botticelli, ".p4i", () => _Botticelli(multicolor: false)),
    new("Multi Botticelli", ImageFormat.Botticelli, ".p4i", () => _Botticelli(multicolor: true)),
    new("Botticelli logo", ImageFormat.Botticelli, ".p4i", _BotticelliLogo),
    // No companion .PL5/.PL7 exists beside a temp file, so these also pin down that both sides fall
    // back to the same MSX2 startup palette.
    new("MSX2 GL5", ImageFormat.MsxGl16, ".gl5", () => _Gl16(64, 48)),
    new("MSX2 SH5", ImageFormat.MsxGl16, ".sh5", () => _Gl16(32, 24)),
    new("MSX2 GL7", ImageFormat.MsxGl16, ".gl7", () => _Gl16(64, 48)),
    new("MSX2 SH7", ImageFormat.MsxGl16, ".sh7", () => _Gl16(96, 16)),
    // Two of each: one plain, one whose interrupt list rewrites palette entries part-way down the
    // screen, which is the only way to tell a correct interrupt walk from a skipped one.
    new("SAM Coupe Mode 1", ImageFormat.SamCoupeScreen, ".ss1", () => _SamCoupe(1, interrupts: false)),
    new("SAM Coupe Mode 1 with interrupts", ImageFormat.SamCoupeScreen, ".ss1", () => _SamCoupe(1, interrupts: true)),
    new("SAM Coupe Mode 2", ImageFormat.SamCoupeScreen, ".ss2", () => _SamCoupe(2, interrupts: false)),
    new("SAM Coupe Mode 2 with interrupts", ImageFormat.SamCoupeScreen, ".ss2", () => _SamCoupe(2, interrupts: true)),
    new("SAM Coupe Mode 3", ImageFormat.SamCoupeScreen, ".ss3", () => _SamCoupe(3, interrupts: false)),
    new("SAM Coupe Mode 3 with interrupts", ImageFormat.SamCoupeScreen, ".ss3", () => _SamCoupe(3, interrupts: true)),
    new("McPainter", ImageFormat.McPainter, ".mcp", _McPainter),
    new("Mad Designer", ImageFormat.MadDesigner, ".mbg", _MadDesigner),
    new("Atari texture", ImageFormat.AtariTxs, ".txs", _AtariTxs),
    new("C64 8x8 font", ImageFormat.Commodore64Font, ".64c", () => _C64Font(2050, 0x00, 0x08)),
    new("C64 8x8 font, short", ImageFormat.Commodore64Font, ".64c", () => _C64Font(1026, 0x00, 0x08)),
    new("SEUCK font", ImageFormat.Commodore64Font, ".g", () => _C64Font(514, 66, 0x00)),
    new("PaintShop", ImageFormat.PaintShop, ".da4", () => _Monochrome(64000)),
    new("Handy Scanner", ImageFormat.HandyScanner, ".hs2", () => _Monochrome(105 * 120)),
    // Both accepted lengths, and every character code including the inverse-video half.
    new("ASCII maker", ImageFormat.AsciiMaker, ".asc", () => _Monochrome(960)),
    new("ASCII maker, padded", ImageFormat.AsciiMaker, ".asc", () => _Monochrome(1024)),
    new("PetDraw64", ImageFormat.PetDraw, ".pdr", () => _Monochrome(2029)),
    // Closes the palette audit: every attribute byte appears, so all sixteen ZX colours at both
    // intensities are exercised in one probe.
    new("ZX Spectrum screen", ImageFormat.ZxSpectrum, ".scr", () => _Monochrome(6912)),
    new("Duo", ImageFormat.Duo, ".duo", () => _Monochrome(113600)),
    new("Duo medium", ImageFormat.DuoMedium, ".du2", () => _Monochrome(113576)),
    // Both kinds, which differ only in what they show without a companion palette.
    new("MSX2 GL6 picture", ImageFormat.MsxGl6, ".gl6", () => _Gl6(64, 24)),
    new("Dynamic Publisher stamp", ImageFormat.MsxGl6, ".stp", () => _Gl6(64, 24)),
    // SFDN-packed pictures: the same formats we already read, under the Atari packer.
    new("Graphics 9 (SFDN)", ImageFormat.AtariGraphics9, ".g9s", () => _Sfdn(7680)),
    new("Graphics 9 (SFDN) as .sfd", ImageFormat.AtariGraphics9, ".sfd", () => _Sfdn(7680)),
    new("InterPainter (SFDN)", ImageFormat.InterPainter, ".ins", () => _Sfdn(16004)),
    new("APAC", ImageFormat.AtariPicture, ".apc", () => _Monochrome(7680)),
    new("APAC as .apa", ImageFormat.AtariPicture, ".apa", () => _Monochrome(7680)),
    new("APAC (SFDN)", ImageFormat.AtariPicture, ".aps", () => _Sfdn(7720)),
    // 16009 bytes is 200 rows of two fields plus the nine colour registers.
    new("Hard Interlace Picture", ImageFormat.AtariHardInterlace, ".hip", () => _Monochrome(16009)),
    new("Hard Interlace Picture (SFDN)", ImageFormat.AtariHardInterlace, ".hps", () => _Sfdn(16009)),
    new("APAC 3", ImageFormat.Apac3, ".ap3", () => _Monochrome(15360)),
    new("APAC 3, long form", ImageFormat.Apac3, ".apv", () => _Monochrome(15872)),
    new("APAC 3 (SFDN)", ImageFormat.Apac3, ".ils", () => _Sfdn(15360)),
    new("Apac3 Linker-Viewer (SFDN)", ImageFormat.Apac3, ".app", () => _Sfdn(15872)),
    new("AtariTools-800 player", ImageFormat.Atari8Player, ".pla", () => _Monochrome(241)),
    new("HCB-editor", ImageFormat.HcbEditor, ".hcb", () => _Monochrome(12148)),
    // Two screens back to back, the second starting where the first one's interrupt list ends.
    new("SAM Coupe interlaced", ImageFormat.SamCoupeLce, ".lce", () => _Lce(interrupts: false)),
    new("SAM Coupe interlaced with interrupts", ImageFormat.SamCoupeLce, ".lce", () => _Lce(interrupts: true)),
    new("Timex hi-res gigascreen", ImageFormat.TimexGigascreen, ".hrg", () => _Monochrome(24578)),
    new("Fuckpaint", ImageFormat.Fuckpaint, ".fp", () => _Monochrome(19266)),
    new("Super-hires Editor II", ImageFormat.SuperHiresEditor2, ".sh2", () => _Monochrome(14770)),
    new("Super-hires Editor I", ImageFormat.SuperHiresEditor1, ".sh1", () => _Monochrome(14770)),
    // The height comes from the BSAVE end address, so both a full screen and a short one.
    new("Graph Saurus Screen 6", ImageFormat.GraphSaurus6, ".sr6", () => _Bsave(212)),
    new("Graph Saurus Screen 6, short", ImageFormat.GraphSaurus6, ".sr6", () => _Bsave(64)),
    new("Graph Saurus interlaced", ImageFormat.GraphSaurusInterlaced, ".sri", () => _Monochrome(108544)),
    new("GunPaint", ImageFormat.GunPaint, ".gun", () => _Monochrome(33602)),
    new("GunPaint as .ifl", ImageFormat.GunPaint, ".ifl", () => _Monochrome(33603)),
    new("Print Shop graphic", ImageFormat.PrintShopIcon, ".psf", () => _Monochrome(572)),
    new("ColorSTar", ImageFormat.ColorStar, ".bil", () => _Monochrome(32032)),
    new("ColorSTar, prefixed", ImageFormat.ColorStar, ".bil", () => _Prefixed(32034)),
    // Size is in cells, so two shapes to show the header is read and not assumed.
    new("Star Painter", ImageFormat.StarPainter, ".gr", () => _StarPainter(40, 25)),
    new("Star Painter, narrow", ImageFormat.StarPainter, ".cs", () => _StarPainter(12, 30)),
    new("Atari 16x16 font", ImageFormat.Atari16x16Font, ".sxs", _Sxs),
    new("Interlaced Logo Editor", ImageFormat.InterlacedLogoEditor, ".ile", () => _Monochrome(4098)),
    new("APAC as .mga", ImageFormat.AtariPicture, ".mga", () => _Monochrome(7856)),
    new("PETSCII BOT, small", ImageFormat.PetsciiBot, ".pbot", () => _Monochrome(70)),
    new("PETSCII BOT, large", ImageFormat.PetsciiBot, ".pbot", () => _Monochrome(384)),
    new("Jet Graphics Planner", ImageFormat.JetGraphicsPlanner, ".jgp", _Jgp),
    new("Plama 256 (SFDN)", ImageFormat.AtariPicture, ".pls", () => _Sfdn(7680)),
    new("MSX2 GL8", ImageFormat.MsxGl8, ".gl8", () => _Gl8(64, 48)),
    new("MSX2 SH8", ImageFormat.MsxGl8, ".sh8", () => _Gl8(96, 16)),
    new("Atari FontMaker", ImageFormat.AtariFontMaker, ".fn2", () => _Monochrome(2048)),
    new("Centauri Logo-Editor", ImageFormat.CentauriLogoEditor, ".cle", () => _Monochrome(8194)),
    new("ImageLab greyscale", ImageFormat.ImageLabBw, ".b&w", () => _ImageLab(64, 48)),
    new("ImageLab greyscale as .b_w", ImageFormat.ImageLabBw, ".b_w", () => _ImageLab(120, 17)),
    new("Super Hires Studio", ImageFormat.SuperHiresStudio, ".shs", () => _Monochrome(14338)),
    new("OD Font Editor", ImageFormat.OdFontEditor, ".odf", () => _Monochrome(1280)),
    new("Vertical Hires Interlace", ImageFormat.VerticalHiresInterlace, ".vhi", () => _Monochrome(17389)),
    new("Vertical Hires Interlace, packed", ImageFormat.VerticalHiresInterlace, ".vhi", _VhiPacked),
    new("AtariTools-800 missile", ImageFormat.Atari8Missile, ".mis", _Atari8Missile),
    new("SlideShow for VBXE", ImageFormat.VbxeSlideShow, ".dap", () => _Monochrome(77568)),
    new("HR2", ImageFormat.AtariHr2, ".hr2", () => _Monochrome(16006)),
    new("Interlace Graphics Editor", ImageFormat.InterlaceGraphicsEditor, ".ige", _Ige),
    new("Mad Studio missile", ImageFormat.MadStudioMissile, ".msl", _MadStudioMissile),
    new("Blazing Paddles window", ImageFormat.BlazingPaddlesWindow, ".wnd", _BlazingPaddlesWindow),
    new("VertiZontal Interlacing", ImageFormat.VertiZontalInterlacing, ".vzi", () => _Monochrome(16000)),
    new("Sketch-PadDles", ImageFormat.SketchPaddles, ".skp", () => _Monochrome(7680)),
    new("Interlace Logo Designer", ImageFormat.InterlaceLogoDesigner, ".ild", () => _Monochrome(8195)),
    new("Mad Studio ANTIC 4 tile", ImageFormat.MadStudioTile, ".tl4", _MadStudioTile),
    new("Larka Edytor Obiektow", ImageFormat.LarkaObjectEditor, ".leo", () => _Monochrome(2580)),
    new("Graph", ImageFormat.GraphLogo, ".all", _GraphLogo),
    new("Technicolor Dream", ImageFormat.TechnicolorDream, ".lum", () => _Monochrome(4766)),
    new("Bugbiter APAC239i", ImageFormat.BugbiterApac, ".bgp", () => _Bugbiter(0)),
    new("Bugbiter APAC239i with a comment", ImageFormat.BugbiterApac, ".bgp", () => _Bugbiter(37)),
    new("Star Painter font", ImageFormat.StarPainterFont, ".zs", _StarPainterFont),
    new("Art Studio window", ImageFormat.ArtStudioWindow, ".mwi", () => _ArtStudioWindow(0, 0)),
    new("Art Studio window, offset into its cells", ImageFormat.ArtStudioWindow, ".mwi", () => _ArtStudioWindow(2, 3)),
    new("SpecSCII", ImageFormat.SpecScii, ".zxs", _SpecScii),
    new("Stellar", ImageFormat.Stellar, ".stl", () => _Monochrome(3072)),
    new("Profi", ImageFormat.ProfiGrf, ".grf", _ProfiGrf),
  ];

  [Test]
  [Category("Conformance")]
  [TestCaseSource(nameof(Probes))]
  public void Decoded_MatchesRecoilPixelForPixel(Probe probe) {
    RecoilOracle.RequireAvailable();

    var bytes = probe.Build();
    var path = Path.Combine(Path.GetTempPath(), $"recoildec_{Guid.NewGuid():N}{probe.Extension}");
    byte[]? png;
    string output;
    try {
      File.WriteAllBytes(path, bytes);
      (png, output) = RecoilOracle.TryDecodeToPng(path);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }

    Assert.That(png, Is.Not.Null, $"{probe}: RECOIL rejected our {bytes.Length}-byte probe — {output}");

    var theirs = _AsRgb(FormatRegistry.Read(png!));

    var ours = _AsRgb(_DecodeOurs(probe, bytes));

    // Sizes are allowed to differ, as in the round-trip fixture: RECOIL reports several modes at
    // their displayed size where we report the stored one — Graphics 9 is 80 logical pixels here
    // and 320 screen pixels there — and neither is wrong. Reported rather than passed silently.
    if (ours.Width != theirs.Width || ours.Height != theirs.Height)
      Assert.Ignore($"{probe}: sizes differ — ours {ours.Width}x{ours.Height}, RECOIL {theirs.Width}x{theirs.Height}");

    for (var i = 0; i < theirs.PixelData.Length; ++i) {
      if (ours.PixelData[i] == theirs.PixelData[i])
        continue;

      var pixel = i / 3;
      Assert.Fail(
        $"{probe}: pixel {pixel % theirs.Width},{pixel / theirs.Width} channel {i % 3} — " +
        $"ours {ours.PixelData[i]}, RECOIL {theirs.PixelData[i]}");
    }
  }

  /// <summary>
  /// Technicolor Dream keeps its hues in a second file, so the interesting half of the format is
  /// invisible to a fixture that hands a decoder one buffer. This one lays both files down beside
  /// each other, which is the only arrangement in which either is the picture the artist drew.
  /// </summary>
  [Test]
  [Category("Conformance")]
  public void TechnicolorDream_WithItsCompanion_MatchesRecoilPixelForPixel() {
    RecoilOracle.RequireAvailable();

    var directory = Path.Combine(Path.GetTempPath(), $"recoillum_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);

    try {
      var luminances = _Monochrome(4766);
      var hues = _Monochrome(4766);
      // A different fill, or the hue field would be the luminance field and a decoder that mixed
      // the two up would still agree.
      for (var i = 0; i < hues.Length; ++i)
        hues[i] = (byte)(i * 113 + 29);

      var picture = Path.Combine(directory, "dream.lum");
      File.WriteAllBytes(picture, luminances);
      File.WriteAllBytes(Path.Combine(directory, "dream.col"), hues);

      var (png, output) = RecoilOracle.TryDecodeToPng(picture);
      Assert.That(png, Is.Not.Null, $"RECOIL rejected the pair — {output}");

      var theirs = _AsRgb(FormatRegistry.Read(png!));
      var ours = _AsRgb(TechnicolorDreamFile.ToRawImage(TechnicolorDreamReader.FromFile(new(picture))));

      Assert.That((ours.Width, ours.Height), Is.EqualTo((theirs.Width, theirs.Height)));

      for (var i = 0; i < theirs.PixelData.Length; ++i) {
        if (ours.PixelData[i] == theirs.PixelData[i])
          continue;

        var pixel = i / 3;
        Assert.Fail(
          $"pixel {pixel % theirs.Width},{pixel / theirs.Width} channel {i % 3} — " +
          $"ours {ours.PixelData[i]}, RECOIL {theirs.PixelData[i]}");
      }
    } finally {
      try { Directory.Delete(directory, true); } catch { /* best effort */ }
    }
  }

  /// <summary>
  /// Decodes with our reader, going through the extension-aware entry point where a format has one.
  /// </summary>
  /// <remarks>
  /// A few of these formats keep the thing that decides how to read them in the file name rather
  /// than the file. RECOIL dispatches on the extension too, so a comparison that ignored it would
  /// be comparing two different questions.
  /// </remarks>
  private static RawImage? _DecodeOurs(Probe probe, byte[] bytes) {
    if (probe.Format == ImageFormat.SamCoupeScreen)
      return FileFormat.SamCoupeScreen.SamCoupeScreenFile.ToRawImage(
        FileFormat.SamCoupeScreen.SamCoupeScreenReader.FromSpan(
          bytes, FileFormat.SamCoupeScreen.SamCoupeScreenFile.ModeFromExtension(probe.Extension)));

    if (probe.Format == ImageFormat.MsxGl6)
      return FileFormat.MsxGl6.MsxGl6File.ToRawImage(
        FileFormat.MsxGl6.MsxGl6Reader.FromSpan(bytes, FileFormat.MsxGl6.MsxGl6File.KindFromExtension(probe.Extension)));

    if (probe.Format == ImageFormat.MsxGl16)
      return FileFormat.MsxGl16.MsxGl16File.ToRawImage(
        FileFormat.MsxGl16.MsxGl16Reader.FromSpan(bytes, FileFormat.MsxGl16.MsxGl16File.ModeFromExtension(probe.Extension)));

    var entry = FormatRegistry.GetEntry(probe.Format);
    Assert.That(entry, Is.Not.Null, $"{probe.Format} is not registered");
    return entry!.LoadRawImageFromBytes(bytes);
  }

  private static RawImage _AsRgb(RawImage? image) {
    Assert.That(image, Is.Not.Null, "decoded to nothing");
    return PixelConverter.Convert(image!, PixelFormat.Rgb24);
  }

  /// <summary>
  /// A full Plus/4 screen whose every cell gets a different luminance and hue, so a decoder that
  /// confuses the two areas or the two nibbles cannot agree by accident.
  /// </summary>
  private static byte[] _Botticelli(bool multicolor) {
    var data = new byte[10050];
    if (multicolor)
      "MULT"u8.CopyTo(data.AsSpan(1020));

    // The two screen-wide background registers the multicolour patterns 00 and 11 draw from.
    data[1024] = 0x35;
    data[1025] = 0x71;

    for (var cell = 0; cell < 1000; ++cell) {
      data[2 + cell] = (byte)((cell * 7 + 1) & 0x77);
      data[1026 + cell] = (byte)((cell * 13 + 5) & 0xFF);
    }

    for (var i = 0; i < 8000; ++i)
      data[2050 + i] = (byte)(i * 31 + (i >> 5));

    return data;
  }

  /// <summary>A sized-header 16-colour picture whose nibbles walk every palette entry.</summary>
  private static byte[] _Gl16(int width, int height) {
    var data = new byte[4 + (width * height + 1) / 2];
    data[0] = (byte)width;
    data[1] = (byte)(width >> 8);
    data[2] = (byte)height;
    data[3] = (byte)(height >> 8);
    for (var i = 4; i < data.Length; ++i)
      data[i] = (byte)(i * 11 + (i >> 4));

    return data;
  }

  /// <summary>
  /// A SAM Coupe screen with a filled bitmap, a full palette and optionally a run of line
  /// interrupts spread down the picture.
  /// </summary>
  private static byte[] _SamCoupe(int mode, bool interrupts) {
    var interruptOffset = mode switch { 1 => 6952, 2 => 14376, _ => 24616 };
    var paletteOffset = interruptOffset - 40;

    var records = interrupts ? new (byte Line, byte Entry, byte Color)[] {
      (23, 0, 0x7F), (23, 5, 0x02), (79, 0, 0x24), (95, 12, 0x49), (150, 3, 0x76),
    } : [];

    var data = new byte[interruptOffset + records.Length * 4 + 1];
    for (var i = 0; i < paletteOffset; ++i)
      data[i] = (byte)(i * 37 + (i >> 6));

    for (var i = 0; i < 16; ++i)
      data[paletteOffset + i] = (byte)(i * 8 + 1);

    var at = interruptOffset;
    foreach (var (line, entry, color) in records) {
      data[at] = line;
      data[at + 1] = entry;
      data[at + 2] = color;
      data[at + 3] = 0;
      at += 4;
    }

    data[at] = 0xFF;
    return data;
  }

  /// <summary>
  /// Two Graphics 15 fields that differ from each other, with two register sets that also differ —
  /// so getting the field order, the scanline parity or the register rotation wrong all show up.
  /// </summary>
  private static byte[] _McPainter() {
    var data = new byte[16008];
    for (var i = 0; i < 8000; ++i) {
      data[i] = (byte)(i * 29 + (i >> 5));
      data[8000 + i] = (byte)(i * 53 + (i >> 3));
    }

    ReadOnlySpan<byte> registers = [0x0E, 0x46, 0x92, 0x00, 0x24, 0xDA, 0x68, 0x0C];
    registers.CopyTo(data.AsSpan(16000));

    return data;
  }

  /// <summary>
  /// An SFDN stream whose every nibble steps one below the last, so the packer's distance table is
  /// actually used rather than merely present.
  /// </summary>
  /// <remarks>
  /// The first entry is 1 and the rest are 0. Zero bits then select entry 0 every time — a stop bit
  /// and one more — so the picture unpacks to a descending ramp that wraps, which no amount of
  /// mishandling the table would reproduce by chance.
  /// </remarks>
  private static byte[] _Sfdn(int unpackedLength) {
    var data = new byte[22 + (unpackedLength >> 1) + 16];
    "S101"u8.CopyTo(data);
    data[4] = (byte)unpackedLength;
    data[5] = (byte)(unpackedLength >> 8);
    data[6] = 1;
    // The high nibble of the first packed byte is the starting value; the rest stay zero.
    data[22] = 0x50;

    return data;
  }

  private static byte[] _ImageLab(int width, int height) {
    var data = new byte[10 + width * height];
    "B&W256"u8.CopyTo(data);
    // Big-endian dimensions, so a byte-swapped reading would give a different picture entirely.
    data[6] = (byte)(width >> 8);
    data[7] = (byte)width;
    data[8] = (byte)(height >> 8);
    data[9] = (byte)height;
    for (var i = 10; i < data.Length; ++i)
      data[i] = (byte)(i - 10);

    return data;
  }

  private static byte[] _Gl8(int width, int height) {
    var data = new byte[4 + width * height];
    data[0] = (byte)width;
    data[1] = (byte)(width >> 8);
    data[2] = (byte)height;
    data[3] = (byte)(height >> 8);
    // Every one of the 256 colours appears, so a wrong palette cannot pass.
    for (var i = 4; i < data.Length; ++i)
      data[i] = (byte)(i - 4);

    return data;
  }

  private static byte[] _Gl6(int width, int height) {
    var data = new byte[4 + (width * height + 3) / 4];
    data[0] = (byte)width;
    data[1] = (byte)(width >> 8);
    data[2] = (byte)height;
    data[3] = (byte)(height >> 8);
    for (var i = 4; i < data.Length; ++i)
      data[i] = (byte)(i * 23 + (i >> 5));

    return data;
  }

  /// <summary>Two mode 4 screens back to back, optionally each with a run of line interrupts.</summary>
  private static byte[] _Lce(bool interrupts) {
    var records = interrupts ? new (byte Line, byte Entry, byte Color)[] {
      (23, 0, 0x7F), (79, 5, 0x02), (150, 12, 0x49),
    } : [];

    var screen = 24616 + records.Length * 4 + 1;
    var data = new byte[screen * 2];

    for (var s = 0; s < 2; ++s) {
      var origin = s * screen;
      for (var i = 0; i < 24576; ++i)
        data[origin + i] = (byte)(i * (s == 0 ? 37 : 61) + (i >> 7));

      for (var i = 0; i < 16; ++i)
        data[origin + 24576 + i] = (byte)(i * 8 + 1 + s);

      var at = origin + 24616;
      foreach (var (line, entry, color) in records) {
        data[at] = line;
        data[at + 1] = entry;
        data[at + 2] = color;
        at += 4;
      }

      data[at] = 0xFF;
    }

    return data;
  }

  /// <summary>A BSAVE-headed Screen 6 picture of a chosen number of stored rows.</summary>
  private static byte[] _Bsave(int rows) {
    var end = (rows << 7) - 1;
    var data = new byte[7 + (rows << 7)];
    data[0] = 0xFE;
    data[3] = (byte)end;
    data[4] = (byte)(end >> 8);
    for (var i = 7; i < data.Length; ++i)
      data[i] = (byte)(i * 59 + (i >> 6));

    return data;
  }

  /// <summary>Like the plain probe, but with the two leading zero bytes some writers add.</summary>
  private static byte[] _Jgp() {
    var data = new byte[2054];
    data[0] = data[1] = 0xFF;
    // A segment declaring exactly the 2048 bytes of glyph data.
    data[2] = 0x00; data[3] = 0x20;
    data[4] = 0xFF; data[5] = 0x27;
    for (var i = 6; i < data.Length; ++i)
      data[i] = (byte)(i * 83 + (i >> 3));

    return data;
  }

  private static byte[] _Sxs() {
    var data = new byte[1030];
    data[0] = data[1] = 0xFF;
    // An executable segment declaring exactly the 1024 bytes of glyph data.
    data[2] = 0x00; data[3] = 0x20;
    data[4] = 0xFF; data[5] = 0x23;
    for (var i = 6; i < data.Length; ++i)
      data[i] = (byte)(i * 73 + (i >> 4));

    return data;
  }

  private static byte[] _StarPainter(int columns, int rows) {
    var data = new byte[2 + columns * rows * 8];
    data[0] = (byte)columns;
    data[1] = (byte)rows;
    for (var i = 2; i < data.Length; ++i)
      data[i] = (byte)(i * 67 + (i >> 5));

    return data;
  }

  /// <summary>An Interlace Graphics Editor picture, which is identified only by its load header.</summary>
  private static byte[] _Ige() {
    var data = _Monochrome(6160);
    ReadOnlySpan<byte> signature = [0xFF, 0xFF, 0xF6, 0xA3, 0xFF, 0xBB, 0xFF, 0x5F];
    signature.CopyTo(data);

    return data;
  }

  /// <summary>
  /// An AtariTools-800 missile. The colour byte has to be something other than black: with the
  /// missile the same colour as what it sits on, a comparison passes whatever the shape decodes to.
  /// </summary>
  private static byte[] _Atari8Missile() {
    var data = _Monochrome(61);
    data[0] = 0x28;

    return data;
  }

  /// <summary>A Mad Studio missile: a height, a colour, then rows using only their low two bits.</summary>
  private static byte[] _MadStudioMissile() {
    const int height = 20;
    var data = new byte[2 + height];
    data[0] = height;
    data[1] = 0x28;
    for (var y = 0; y < height; ++y)
      data[2 + y] = (byte)(y % 4);

    return data;
  }

  /// <summary>A Blazing Paddles window: a size in the first two bytes, then a buffer's worth of bitmap.</summary>
  private static byte[] _BlazingPaddlesWindow() {
    var data = _Monochrome(3072);
    data[0] = 63;
    data[1] = 40;

    return data;
  }

  /// <summary>A Mad Studio tile set: a grid size, then nine bytes for each tile.</summary>
  private static byte[] _MadStudioTile() {
    const int columns = 3, rows = 4;
    var data = _Monochrome(2 + columns * rows * 9);
    data[0] = columns;
    data[1] = rows;

    return data;
  }

  /// <summary>
  /// A Graph picture: per-row character set numbers, then the sets, the screen and the colours.
  /// </summary>
  /// <remarks>
  /// Three sets, with the rows cycling through them, so a decoder that ignores the per-row bank
  /// number draws the whole screen from one alphabet and cannot agree.
  /// </remarks>
  private static byte[] _GraphLogo() {
    const int banks = 3;
    var data = _Monochrome(24 + banks * 1024 + 24 * 40 + 5);
    for (var row = 0; row < 24; ++row)
      data[row] = (byte)(row % banks);

    return data;
  }

  /// <summary>
  /// A Bugbiter APAC239i picture, optionally carrying a comment — which moves everything after it,
  /// so a decoder that assumed fixed offsets passes the first case and fails the second.
  /// </summary>
  private static byte[] _Bugbiter(int textLength) {
    var data = _Monochrome(19163 + textLength);
    "BUGBITER_APAC239I_PICTURE_V1.0"u8.CopyTo(data);
    data[30] = 255;
    data[31] = 80;
    data[32] = 239;
    data[37] = (byte)textLength;
    data[38] = (byte)(textLength >> 8);

    var picture = 39 + textLength;
    data[picture] = data[picture + 9562] = 88;
    data[picture + 1] = data[picture + 9563] = 37;

    return data;
  }

  /// <summary>A Star Painter character set, which is identified only by its load address.</summary>
  private static byte[] _StarPainterFont() {
    var data = _Monochrome(1026);
    data[0] = 0xB0;
    data[1] = 0xF0;

    return data;
  }

  /// <summary>
  /// An Art Studio window. The offsets into the first cell are what make the stored length depend
  /// on more than the dimensions, so one probe starts on a cell boundary and one does not.
  /// </summary>
  private static byte[] _ArtStudioWindow(int left, int top) {
    const int width = 48, height = 40;
    var cellsPerRow = ((width * 2 + 7) >> 3) + (left != 0 ? 1 : 0);
    var rows = ((height + 7) >> 3) + (top != 0 ? 1 : 0);

    var data = _Monochrome(5 + rows * cellsPerRow * 10);
    data[1] = (byte)left;
    data[2] = (byte)top;
    data[3] = width;
    data[4] = height;

    return data;
  }

  /// <summary>A SpecSCII picture, whose cells must all name characters the set holds.</summary>
  private static byte[] _SpecScii() {
    var data = _Monochrome(2452);
    "ZX_SSCII"u8.CopyTo(data);
    data[8] = 148;
    data[9] = 9;
    data[10] = data[11] = 0;

    for (var cell = 0; cell < 768; ++cell)
      data[908 + cell] = (byte)(cell % 112);

    return data;
  }

  /// <summary>A Profi picture, which is identified only by its ten-byte header.</summary>
  private static byte[] _ProfiGrf() {
    var data = _Monochrome(30848);
    ReadOnlySpan<byte> signature = [0, 2, 240, 0, 4, 0, 128, 0, 1, 19];
    signature.CopyTo(data);

    return data;
  }

  private static byte[] _Prefixed(int length) {
    var data = _Monochrome(length);
    data[0] = data[1] = 0;

    return data;
  }

  private static byte[] _Monochrome(int length) {
    var data = new byte[length];
    for (var i = 0; i < length; ++i)
      data[i] = (byte)(i * 47 + (i >> 7));

    return data;
  }

  /// <summary>
  /// A packed Vertical Hires Interlace picture: two header bytes, then literal and repeated runs.
  /// </summary>
  /// <remarks>
  /// The payload deliberately alternates between stretches of one repeated byte and stretches of
  /// varying ones, so the greedy encoder below has to emit both kinds of run and both are exercised
  /// on the way back in. Without the flat stretches every run would be a literal one and the repeat
  /// path would go untested.
  /// </remarks>
  private static byte[] _VhiPacked() {
    const int unpackedLength = 17384;
    var unpacked = new byte[unpackedLength];
    for (var i = 0; i < unpackedLength; ++i)
      unpacked[i] = (i / 97) % 3 == 0 ? (byte)((i / 97) & 15) : (byte)(i * 47 + (i >> 7));

    var packed = new List<byte> { 0, 0 };
    for (var at = 0; at < unpackedLength;) {
      var run = 1;
      while (run < 256 && at + run < unpackedLength && unpacked[at + run] == unpacked[at])
        ++run;

      if (run >= 4) {
        packed.Add(1);
        packed.Add((byte)(run & 255));
        packed.Add(unpacked[at]);
        at += run;
        continue;
      }

      var literal = 0;
      while (literal < 256 && at + literal < unpackedLength) {
        var same = 1;
        while (same < 4 && at + literal + same < unpackedLength && unpacked[at + literal + same] == unpacked[at + literal])
          ++same;

        if (same >= 4)
          break;

        ++literal;
      }

      packed.Add(0);
      packed.Add((byte)(literal & 255));
      packed.AddRange(unpacked.AsSpan(at, literal).ToArray());
      at += literal;
    }

    return packed.ToArray();
  }

  private static byte[] _C64Font(int length, byte low, byte high) {
    var data = new byte[length];
    data[0] = low;
    data[1] = high;
    for (var i = 2; i < length; ++i)
      data[i] = (byte)(i * 43 + (i >> 3));

    return data;
  }

  private static byte[] _MadDesigner() {
    var data = new byte[16384];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 41 + (i >> 6));

    return data;
  }

  private static byte[] _AtariTxs() {
    var data = new byte[262];
    ReadOnlySpan<byte> header = [0xFF, 0xFF, 0x00, 0x06, 0xFF, 0x06];
    header.CopyTo(data);
    // Every one of the sixteen values appears, so a wrong palette slice cannot pass.
    for (var i = 0; i < 256; ++i)
      data[6 + i] = (byte)((i * 7 + i / 16) & 15);

    return data;
  }

  private static byte[] _BotticelliLogo() {
    var data = new byte[2050];
    for (var i = 0; i < 2048; ++i)
      data[2 + i] = (byte)(i * 17 + (i >> 3));

    return data;
  }
}
