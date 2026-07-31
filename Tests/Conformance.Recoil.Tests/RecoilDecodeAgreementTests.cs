using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.AmstradMode5;
using FileFormat.ColrObjectEditor;
using FileFormat.PerfectPix;
using FileFormat.Picasso;
using FileFormat.OcpArtStudioWindow;
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
    new("MSX Screen 2", ImageFormat.MsxScreen2, ".sc2", () => _Bsave(112)),
    new("MSX Screen 2 with sprites", ImageFormat.MsxScreen2, ".sc2", _MsxSpriteScreen),
    new("MSX Screen 3", ImageFormat.MsxScreen3, ".sc3", () => _Bsave(22)),
    new("MSX Screen 3, long", ImageFormat.MsxScreen3, ".sc3", () => _Bsave(65)),
    new("MSX Screen 3 with sprites", ImageFormat.MsxScreen3, ".sc3", _MsxSpriteScreen),
    new("MSX Screen 4", ImageFormat.MsxScreen4, ".sc4", () => _Bsave(112)),
    new("MSX Screen 4 with sprites", ImageFormat.MsxScreen4, ".sc4", _MsxSpriteScreen),
    new("Color Computer P11", ImageFormat.CocoP11, ".p11", () => _CocoP11(3083)),
    new("Color Computer P11, long", ImageFormat.CocoP11, ".p11", () => _CocoP11(3243)),
    new("BK monochrome", ImageFormat.BkScreen, ".bks", () => _Monochrome(16384)),
    new("BK monochrome, two screens", ImageFormat.BkScreen, ".bks", () => _Monochrome(32768)),
    new("BK colour", ImageFormat.BkScreen, ".bks", () => _BkColor(1)),
    new("BK colour, two screens", ImageFormat.BkScreen, ".bks", () => _BkColor(2)),
    new("PC-98 EBD", ImageFormat.Pc98Ebd, ".ebd", () => _Ebd(false)),
    new("PC-98 EBD with a widened palette", ImageFormat.Pc98Ebd, ".ebd", () => _Ebd(true)),
    new("RAG-D, four planes", ImageFormat.RagD, ".rag", () => _RagD(4, 32)),
    new("RAG-D, eight planes", ImageFormat.RagD, ".rag", () => _RagD(8, 1024)),
    new("RAG-D, true colour", ImageFormat.RagD, ".rag", () => _RagD(16, 1024)),
    new("Music Compile 2 chunky", ImageFormat.RagD, ".ragc", () => _RagD(8, 1024)),
    new("SAM Coupe mode 1", ImageFormat.SamCoupeSsx, ".ssx", () => _Monochrome(6928)),
    new("SAM Coupe mode 2", ImageFormat.SamCoupeSsx, ".ssx", () => _Monochrome(12304)),
    new("SAM Coupe mode 3", ImageFormat.SamCoupeSsx, ".ssx", () => _Monochrome(24580)),
    new("SAM Coupe mode 4", ImageFormat.SamCoupeSsx, ".ssx", () => _Monochrome(24592)),
    new("SAM Coupe rendered", ImageFormat.SamCoupeSsx, ".ssx", _SamCoupeChunky),
    new("PI8 in Graphics 15", ImageFormat.AtariPi8, ".pi8", () => _Monochrome(7680)),
    new("PI8 in Graphics 8", ImageFormat.AtariPi8, ".pi8", () => _Monochrome(7685)),
    new("PI8 in Graphics 8, as an executable", ImageFormat.AtariPi8, ".pi8", _Pi8Executable),
    new("PI9 in Graphics 9", ImageFormat.AtariPi9, ".pi9", () => _Monochrome(7684)),
    new("PI9 in Graphics 9, padded", ImageFormat.AtariPi9, ".pi9", () => _Monochrome(7936)),
    new("PI9 in APAC", ImageFormat.AtariPi9, ".pi9", () => _Monochrome(7720)),
    new("PI9 on a Falcon", ImageFormat.AtariPi9, ".pi9", () => _Monochrome(65024)),
    new("PI9 on a Falcon, taller", ImageFormat.AtariPi9, ".pi9", () => _Monochrome(77824)),
    new("ZZ_ROUGH", ImageFormat.ZzRough, ".rgh", _ZzRough),
    new("Taquart Interlace Picture", ImageFormat.TaquartInterlace, ".tip", () => _Tip(160, 119)),
    new("Taquart Interlace Picture, narrow", ImageFormat.TaquartInterlace, ".tip", () => _Tip(64, 40)),
    new("VDC BitMap version 2", ImageFormat.VdcBitmap, ".vbm", () => _Vbm(2, false)),
    new("VDC BitMap version 3", ImageFormat.VdcBitmap, ".vbm", () => _Vbm(3, false)),
    new("VDC BitMap version 3, packed", ImageFormat.VdcBitmap, ".vbm", () => _Vbm(3, true)),
    new("Atari Player Editor", ImageFormat.AtariPlayerEditor, ".apl", () => _PlayerEditor(9, 24, 5)),
    new("Atari Player Editor, one frame", ImageFormat.AtariPlayerEditor, ".apl", () => _PlayerEditor(1, 48, 0)),
    new("PMG Designer", ImageFormat.PmgDesigner, ".pmd", () => _PmgDesigner(4, 3, 5, 21)),
    new("PMG Designer, one row", ImageFormat.PmgDesigner, ".pmd", () => _PmgDesigner(2, 2, 2, 16)),
    new("Ludek Maker", ImageFormat.LudekMaker, ".ldm", () => _LudekMaker(19)),
    new("Ludek Maker, one row", ImageFormat.LudekMaker, ".ldm", () => _LudekMaker(6)),
    new("Daisy-Dot font", ImageFormat.DaisyDotFont, ".nlq", _DaisyDot),
    new("Atari Graphics Studio, interleaved", ImageFormat.AtariGraphicsStudio, ".ags", () => _Ags(11, 40, 96)),
    new("Atari Graphics Studio, quadrupled", ImageFormat.AtariGraphicsStudio, ".ags", () => _Ags(19, 40, 48)),
    new("DEGAS Elite brush", ImageFormat.DegasBrush, ".bru", _DegasBrush),
    new("Atari Image Manager", ImageFormat.AtariImageManager, ".im", () => _Monochrome(16384)),
    new("Atari Image Manager, large", ImageFormat.AtariImageManager, ".im", () => _Monochrome(65536)),
    new("Grafix", ImageFormat.Grafix, ".grx", () => _Grafix(320, 200, 16)),
    new("Grafix, monochrome", ImageFormat.Grafix, ".grx", () => _Grafix(640, 400, 2)),
    new("Grafix, packed", ImageFormat.Grafix, ".grx", _PackedGrafix),
    new("InShape monochrome", ImageFormat.InShape, ".iim", () => _InShape(0, 200, 150)),
    new("InShape greyscale", ImageFormat.InShape, ".iim", () => _InShape(1, 160, 100)),
    new("InShape true colour", ImageFormat.InShape, ".iim", () => _InShape(4, 64, 48)),
    new("InShape true colour, padded", ImageFormat.InShape, ".iim", () => _InShape(5, 64, 48)),
    new("Picworks", ImageFormat.AtariPicworks, ".cp3", _Picworks),
    new("Best Paint", ImageFormat.BestPaint, ".bp", _BestPaint),
    new("Cranach monochrome", ImageFormat.CranachPaint, ".esm", () => _Cranach(1, 200, 150)),
    new("Cranach palette", ImageFormat.CranachPaint, ".esm", () => _Cranach(8, 160, 100)),
    new("Cranach true colour", ImageFormat.CranachPaint, ".esm", () => _Cranach(24, 64, 48)),
    new("SymbOS graphic", ImageFormat.SymbOsGraphic, ".sgx", () => _SymbOs(false)),
    new("SymbOS graphic, sixteen colours", ImageFormat.SymbOsGraphic, ".sgx", () => _SymbOs(true)),
    new("SEUCK sprites", ImageFormat.SeuckSprites, ".a", _Seuck),
    new("MINIPAINT", ImageFormat.MiniPaint, ".mg", _MiniPaint),
    new("PaintShop, compressed", ImageFormat.PaintShopCompressed, ".psc", () => _PaintShop(false)),
    new("PaintShop, stored", ImageFormat.PaintShopCompressed, ".psc", () => _PaintShop(true)),
    new("Kompresor do Animatora", ImageFormat.AnimatorCompressor, ".kpr", () => _Animator(4, 5, 3, 24)),
    new("Kompresor do Animatora, one frame", ImageFormat.AnimatorCompressor, ".kpr", () => _Animator(1, 8, 8, 40)),
    new("Trzmiel, stored", ImageFormat.TrzmielCompressed, ".cpr", () => _Trzmiel(0)),
    new("Trzmiel, packed by column", ImageFormat.TrzmielCompressed, ".cpr", () => _Trzmiel(1)),
    new("Trzmiel, packed linearly", ImageFormat.TrzmielCompressed, ".cpr", () => _Trzmiel(2)),
    new("Grass' Slideshow", ImageFormat.GrassSlideshow, ".hpm", () => _GrassSlideshow(81)),
    new("Grass' Slideshow, unnamed palette", ImageFormat.GrassSlideshow, ".hpm", () => _GrassSlideshow(200)),
    new("XL-Paint, marked", ImageFormat.XlPaint, ".xlp", () => _XlPaint(true, 192)),
    new("XL-Paint, unmarked 200 rows", ImageFormat.XlPaint, ".xlp", () => _XlPaint(false, 200)),
    new("XL-Paint, unmarked 192 rows", ImageFormat.XlPaint, ".xlp", () => _XlPaint(false, 192)),
    new("DelmPaint", ImageFormat.DelmPaint, ".del", () => _DelmPaint(2)),
    new("DelmPaint, four quadrants", ImageFormat.DelmPaint, ".dph", () => _DelmPaint(10)),
    new("D-GRAPH, compressed", ImageFormat.DGraphCompressed, ".p3c", _DGraph),
    new("Champions' Interlace, per-row colours", ImageFormat.AtariChampionsInterlace, ".cin", () => _Monochrome(16384)),
    new("Champions' Interlace, no colours", ImageFormat.AtariChampionsInterlace, ".cin", () => _Monochrome(15360)),
    new("Champions' Interlace, one colour set", ImageFormat.AtariChampionsInterlace, ".cin", () => _Monochrome(16004)),
    new("Champions' Interlace, compressed", ImageFormat.AtariChampionsInterlace, ".cci", _Cci),
    new("CharPad, characters only", ImageFormat.CharPad, ".ctm", () => _CharPad(0, false, false, false)),
    new("CharPad, tiles", ImageFormat.CharPad, ".ctm", () => _CharPad(0, true, false, false)),
    new("CharPad, tiles with implied characters", ImageFormat.CharPad, ".ctm", () => _CharPad(0, true, true, false)),
    new("CharPad, colour per tile", ImageFormat.CharPad, ".ctm", () => _CharPad(1, true, false, true)),
    new("CharPad, colour per character", ImageFormat.CharPad, ".ctm", () => _CharPad(2, false, false, true)),
    new("AMOS sprites", ImageFormat.AmosBank, ".abk", () => _AmosSprites("AmSp")),
    new("AMOS icons", ImageFormat.AmosBank, ".abk", () => _AmosSprites("AmIc")),
    new("AMOS packed screen", ImageFormat.AmosBank, ".abk", _AmosScreen),
    new("Super Hires FLI, with sprites", ImageFormat.SuperHiresFli, ".shf", () => _Monochrome(15874)),
    new("Super Hires FLI, packed", ImageFormat.SuperHiresFli, ".shf", _PackedShf),
    new("Extend Super Hires", ImageFormat.ExtendSuperHires, ".esh", _Esh),
    new("Extend Super Hires, packed", ImageFormat.ExtendSuperHires, ".esh", _PackedEsh),
    new("UIFLI-editor", ImageFormat.UifliEditor, ".uif", _Uifli),
    new("SHF-XL Edit", ImageFormat.ShfXlEdit, ".shx", () => _Monochrome(15362)),
    new("SHF-XL Edit, packed", ImageFormat.ShfXlEdit, ".shx", _PackedShx),
    new("Commodore Grafix", ImageFormat.CommodoreGrafix, ".cgx", () => _Grafix(3, 2, 4, 3)),
    new("Commodore Grafix, one frame", ImageFormat.CommodoreGrafix, ".cgx", () => _Grafix(1, 1, 8, 8)),
    new("3201", ImageFormat.Apple3201, ".3201", _Apple3201),
    new("Anime 4ever", ImageFormat.Anime4Ever, ".a4r", _Anime4Ever),
    new("Boogie Down Paint, oldest", ImageFormat.BoogieDownPaint, ".bdp", () => _Bdp(0)),
    new("Boogie Down Paint, with a loader", ImageFormat.BoogieDownPaint, ".bdp", () => _Bdp(1)),
    new("Boogie Down Paint 5.00", ImageFormat.BoogieDownPaint, ".bdp", () => _Bdp(2)),
    new("Hard Color Map", ImageFormat.HardColorMap, ".hcm", () => _Hcm(0)),
    new("Hard Color Map, the other arrangement", ImageFormat.HardColorMap, ".hcm", () => _Hcm(2)),
    new("GED", ImageFormat.GedPicture, ".ged", () => _Ged(0, 0)),
    new("GED, latest timing", ImageFormat.GedPicture, ".ged", () => _Ged(7, 0)),
    new("GED, missiles as a fifth colour", ImageFormat.GedPicture, ".ged", () => _Ged(3, 16)),
    new("PowerGraphics, forty columns", ImageFormat.PowerGraphics, ".pgr", () => _PowerGraphics(50)),
    new("PowerGraphics, thirty-two columns", ImageFormat.PowerGraphics, ".pgr", () => _PowerGraphics(49)),
    new("Graph2Font MCH", ImageFormat.Graph2FontMch, ".mch", () => _Mch(12000, 4)),
    new("Graph2Font MCH with sprites", ImageFormat.Graph2FontMch, ".mch", () => _Mch(30833, 4, 1)),
    new("Graph2Font MCH in hi-res", ImageFormat.Graph2FontMch, ".mch", () => _Mch(12000, 0)),
    new("Graph2Font MCH in a GTIA mode", ImageFormat.Graph2FontMch, ".mch", () => _Mch(30833, 24, 1)),
    new("Graph2Font", ImageFormat.Graph2Font, ".g2f", () => _G2f(40, false)),
    new("Graph2Font, narrow", ImageFormat.Graph2Font, ".g2f", () => _G2f(32, false)),
    new("Graph2Font, compressed", ImageFormat.Graph2Font, ".g2f", () => _G2f(40, true)),
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

    // Where RECOIL reports exactly twice our width it is counting screen pixels and we are counting
    // the picture's own — a C64 multicolour pixel is two screen pixels wide. Doubling ours is exact
    // rather than a resample, so the two can still be compared pixel for pixel.
    if (theirs.Width == ours.Width * 2 && theirs.Height == ours.Height)
      ours = _DoubleWidth(ours);

    // Any other difference is a genuine disagreement about what the format says, and there are
    // modes where neither answer is wrong — Graphics 9 is 80 logical pixels here and 320 screen
    // pixels there. Reported rather than passed silently.
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
  /// A C.O.L.R. object is a bare bitmap whose colours live in a .pal file beside it, and the
  /// reference decoder refuses the picture outright without one — so this is the only arrangement
  /// in which the format decodes at all.
  /// </summary>
  [Test]
  [Category("Conformance")]
  public void ColrObject_WithItsPalette_MatchesRecoilPixelForPixel() {
    RecoilOracle.RequireAvailable();

    var directory = Path.Combine(Path.GetTempPath(), $"recoilmur_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);

    try {
      // Intensities per thousand, so the values have to stay under 1000 to mean anything but white.
      var palette = new byte[96];
      for (var i = 0; i < 16; ++i)
      for (var channel = 0; channel < 3; ++channel) {
        var thousandths = (i * 61 + channel * 211) % 1001;
        palette[i * 6 + channel * 2] = (byte)(thousandths >> 8);
        palette[i * 6 + channel * 2 + 1] = (byte)thousandths;
      }

      var picture = Path.Combine(directory, "object.mur");
      File.WriteAllBytes(picture, _Monochrome(32000));
      File.WriteAllBytes(Path.Combine(directory, "object.pal"), palette);

      var (png, output) = RecoilOracle.TryDecodeToPng(picture);
      Assert.That(png, Is.Not.Null, $"RECOIL rejected the pair — {output}");

      var theirs = _AsRgb(FormatRegistry.Read(png!));
      var ours = _AsRgb(ColrObjectEditorFile.ToRawImage(ColrObjectEditorReader.FromFile(new(picture))));

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
  /// A Picasso bitmap holds only its two screen-wide colours; the per-cell one is in the .pic1
  /// beside it, and every cell of that has to be marked multicoloured.
  /// </summary>
  [Test]
  [Category("Conformance")]
  public void Picasso_WithItsColours_MatchesRecoilPixelForPixel() {
    RecoilOracle.RequireAvailable();

    var directory = Path.Combine(Path.GetTempPath(), $"recoilpic_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);

    try {
      var bitmap = _Monochrome(3890);
      bitmap[0] = 0;
      bitmap[1] = 13;
      bitmap[3876] = 150;
      bitmap[3877] = 23;
      bitmap[3879] = 140;

      var colors = _Monochrome(244);
      for (var cell = 0; cell < 242; ++cell)
        colors[2 + cell] = (byte)(8 | (cell % 8));

      var picture = Path.Combine(directory, "art.pic0");
      File.WriteAllBytes(picture, bitmap);
      File.WriteAllBytes(Path.Combine(directory, "art.pic1"), colors);

      var (png, output) = RecoilOracle.TryDecodeToPng(picture);
      Assert.That(png, Is.Not.Null, $"RECOIL rejected the pair — {output}");

      var theirs = _AsRgb(FormatRegistry.Read(png!));
      var ours = _AsRgb(PicassoFile.ToRawImage(PicassoReader.FromFile(new(picture))));

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
  /// Perfect Pix keeps its two fields in .odd and .eve beside the head file, which holds no picture
  /// at all — only the size, the mode and the colours the fields share.
  /// </summary>
  [TestCase((byte)3)]
  [TestCase((byte)4)]
  [TestCase((byte)5)]
  [Category("Conformance")]
  public void PerfectPix_WithItsFields_MatchesRecoilPixelForPixel(byte mode) {
    RecoilOracle.RequireAvailable();

    var directory = Path.Combine(Path.GetTempPath(), $"recoilpph_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);

    try {
      var width = 160;
      var height = 100;
      var head = _PerfectPixHead(mode, width, height);

      var picture = Path.Combine(directory, "pic.pph");
      File.WriteAllBytes(picture, head);
      File.WriteAllBytes(Path.Combine(directory, "pic.odd"), _Monochrome(height * (width >> 2)));

      var even = _Monochrome(height * (width >> 2));
      for (var i = 0; i < even.Length; ++i)
        even[i] = (byte)(i * 113 + 29);

      File.WriteAllBytes(Path.Combine(directory, "pic.eve"), even);

      var (png, output) = RecoilOracle.TryDecodeToPng(picture);
      Assert.That(png, Is.Not.Null, $"RECOIL rejected mode {mode} — {output}");

      var theirs = _AsRgb(FormatRegistry.Read(png!));
      var ours = _AsRgb(PerfectPixFile.ToRawImage(PerfectPixReader.FromFile(new(picture))));

      Assert.That((ours.Width, ours.Height), Is.EqualTo((theirs.Width, theirs.Height)));

      for (var i = 0; i < theirs.PixelData.Length; ++i) {
        if (ours.PixelData[i] == theirs.PixelData[i])
          continue;

        var pixel = i / 3;
        Assert.Fail(
          $"mode {mode}: pixel {pixel % theirs.Width},{pixel / theirs.Width} channel {i % 3} — " +
          $"ours {ours.PixelData[i]}, RECOIL {theirs.PixelData[i]}");
      }
    } finally {
      try { Directory.Delete(directory, true); } catch { /* best effort */ }
    }
  }

  /// <summary>
  /// A Perfect Pix head file. The striped mode's size follows from how many palettes it carries,
  /// and each but the last is followed by the number of rows it covers.
  /// </summary>
  private static byte[] _PerfectPixHead(byte mode, int width, int height) {
    if (mode != 5) {
      var wide = new byte[22];
      wide[0] = mode;
      wide[1] = (byte)width;
      wide[2] = (byte)(width >> 8);
      wide[3] = (byte)height;
      wide[4] = (byte)(height >> 8);
      wide[5] = 1;
      for (var i = 0; i < 16; ++i)
        wide[6 + i] = (byte)(i * 5 % 27);

      return wide;
    }

    // Four palettes: the first three each state how many rows they cover, the last runs to the end.
    var palettes = 4;
    var data = new byte[(1 + palettes) * 5];
    data[0] = mode;
    data[1] = (byte)width;
    data[2] = (byte)(width >> 8);
    data[3] = (byte)height;
    data[4] = (byte)(height >> 8);
    data[5] = (byte)palettes;

    var at = 6;
    for (var palette = 0; palette < palettes; ++palette) {
      for (var i = 0; i < 4; ++i)
        data[at++] = (byte)((palette * 7 + i * 3) % 27);

      if (at < data.Length)
        data[at++] = 20;
    }

    return data;
  }

  /// <summary>
  /// A Mode 5 file holds only colours; its bitmap is in the .gfx beside it, so the pair is the
  /// only arrangement in which either is a picture.
  /// </summary>
  [Test]
  [Category("Conformance")]
  public void AmstradMode5_WithItsBitmap_MatchesRecoilPixelForPixel() {
    RecoilOracle.RequireAvailable();

    var directory = Path.Combine(Path.GetTempPath(), $"recoilcm5_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);

    try {
      // Every colour byte must name one of the thirty-two the Gate Array can make.
      var colors = _Monochrome(2049);
      for (var i = 0; i < colors.Length; ++i)
        colors[i] = (byte)(64 + (i * 7 + (i >> 5)) % 32);

      var picture = Path.Combine(directory, "pic.cm5");
      File.WriteAllBytes(picture, colors);
      File.WriteAllBytes(Path.Combine(directory, "pic.gfx"), _Monochrome(18432));

      var (png, output) = RecoilOracle.TryDecodeToPng(picture);
      Assert.That(png, Is.Not.Null, $"RECOIL rejected the pair — {output}");

      var theirs = _AsRgb(FormatRegistry.Read(png!));
      var ours = _AsRgb(AmstradMode5File.ToRawImage(AmstradMode5Reader.FromFile(new(picture))));

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
  /// An OCP Art Studio window stores no colours at all, so without the .pal beside it there is no
  /// picture — the reference decoder refuses it outright, and so does ours.
  /// </summary>
  [TestCase(false)]
  [TestCase(true)]
  [Category("Conformance")]
  public void OcpWindow_WithItsPalette_MatchesRecoilPixelForPixel(bool packed) {
    RecoilOracle.RequireAvailable();

    var directory = Path.Combine(Path.GetTempPath(), $"recoilwin_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);

    try {
      var picture = Path.Combine(directory, "clip.win");
      File.WriteAllBytes(picture, _OcpWindow(packed));
      File.WriteAllBytes(Path.Combine(directory, "clip.pal"), _OcpPalette());

      var (png, output) = RecoilOracle.TryDecodeToPng(picture);
      Assert.That(png, Is.Not.Null, $"RECOIL rejected the pair — {output}");

      var theirs = _AsRgb(FormatRegistry.Read(png!));
      var ours = _AsRgb(OcpArtStudioWindowFile.ToRawImage(OcpArtStudioWindowReader.FromFile(new(picture))));

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
  /// A companion palette: a mode byte and then sixteen colours twelve bytes apart, each biased by
  /// 64 so it stays a printable value.
  /// </summary>
  private static byte[] _OcpPalette() {
    var data = _Monochrome(239);
    data[0] = 0;
    for (var i = 0; i < 16; ++i)
      data[3 + i * 12] = (byte)(64 + i * 2);

    return data;
  }

  /// <summary>
  /// An OCP window, whose size is in its last five bytes. The packed form stores its runs in named
  /// blocks that the coding runs on across, so a run may be counted against one and finish in the
  /// next — which the probe does on purpose.
  /// </summary>
  private static byte[] _OcpWindow(bool packed) {
    var width = 96;
    var height = 40;
    var stride = (width + 7) >> 3;
    var bitmap = new byte[stride * height];
    for (var i = 0; i < bitmap.Length; ++i)
      bitmap[i] = (byte)(i * 47 + (i >> 5));

    var body = new System.Collections.Generic.List<byte>();

    if (!packed)
      body.AddRange(bitmap);
    else {
      var at = 0;
      while (at < bitmap.Length) {
        // Blocks of a fixed length, so runs straddle their boundaries rather than aligning to them.
        var block = Math.Min(200, bitmap.Length - at);
        body.AddRange([(byte)'M', (byte)'J', (byte)'H', (byte)block, (byte)(block >> 8)]);

        for (var written = 0; written < block;) {
          var left = block - written;
          if (left >= 6) {
            body.AddRange([1, 6, bitmap[at + written]]);
            written += 6;
          } else {
            body.Add(bitmap[at + written]);
            ++written;
          }
        }

        at += block;
      }
    }

    body.AddRange([0, (byte)width, (byte)(width >> 8), (byte)height, 0]);

    return body.ToArray();
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

    if (probe.Format == ImageFormat.AtariChampionsInterlace)
      return FileFormat.AtariChampionsInterlace.AtariChampionsInterlaceFile.ToRawImage(
        FileFormat.AtariChampionsInterlace.AtariChampionsInterlaceReader.FromBytes(bytes));

    if (probe.Format == ImageFormat.DelmPaint)
      return FileFormat.DelmPaint.DelmPaintFile.ToRawImage(
        FileFormat.DelmPaint.DelmPaintReader.FromBytes(bytes, probe.Extension));

    if (probe.Format == ImageFormat.RagD)
      return FileFormat.RagD.RagDFile.ToRawImage(FileFormat.RagD.RagDReader.FromBytes(bytes, probe.Extension));

    var entry = FormatRegistry.GetEntry(probe.Format);
    Assert.That(entry, Is.Not.Null, $"{probe.Format} is not registered");
    return entry!.LoadRawImageFromBytes(bytes);
  }

  /// <summary>Repeats every column, which is what a display showing double-width pixels does.</summary>
  private static RawImage _DoubleWidth(RawImage image) {
    var doubled = new byte[image.PixelData.Length * 2];
    for (var i = 0; i < image.Width * image.Height; ++i) {
      image.PixelData.AsSpan(i * 3, 3).CopyTo(doubled.AsSpan(i * 6));
      image.PixelData.AsSpan(i * 3, 3).CopyTo(doubled.AsSpan(i * 6 + 3));
    }

    return new() {
      Width = image.Width * 2,
      Height = image.Height,
      Format = image.Format,
      PixelData = doubled,
    };
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

  /// <summary>
  /// A BSAVE screen large enough to carry the sprite plane, with attributes that place a handful of
  /// sprites on screen rather than leaving them wherever the fill pattern puts them.
  /// </summary>
  private static byte[] _MsxSpriteScreen() {
    var data = _Bsave(128);

    // Both generations keep their attributes in a different corner, so seed both.
    foreach (var attributes in (int[])[7 + 0x1B00, 7 + 0x1E00]) {
      for (var sprite = 0; sprite < 8; ++sprite) {
        var at = attributes + sprite * 4;
        data[at] = (byte)(sprite * 20 + 8);
        data[at + 1] = (byte)(sprite * 28 + 4);
        data[at + 2] = (byte)(sprite * 4);
        data[at + 3] = (byte)(sprite | (sprite << 4));
      }

      // The list has to end somewhere, or every remaining sprite competes for the line budget.
      data[attributes + 8 * 4] = 216;
    }

    return data;
  }

  /// <summary>A P11 picture, which is identified only by four bytes of its header.</summary>
  private static byte[] _CocoP11(int length) {
    var data = _Monochrome(length);
    data[0] = 0;
    data[1] = 12;
    data[3] = 14;
    data[4] = 0;

    return data;
  }

  /// <summary>
  /// A BK colour screen. Each frame's trailing byte names one of sixteen colour sets, and they are
  /// deliberately different sets, so a decoder that used one frame's for both cannot agree.
  /// </summary>
  private static byte[] _BkColor(int frames) {
    var data = _Monochrome(16384 * frames + frames);
    for (var frame = 0; frame < frames; ++frame)
      data[16384 * frames + frame] = (byte)(frame * 7 + 3);

    return data;
  }

  /// <summary>
  /// A PC-98 EBD picture. The palette is stored one of two ways and nothing says which, so one
  /// probe uses bare nibbles and one uses channels already widened to eight bits.
  /// </summary>
  private static byte[] _Ebd(bool widened) {
    var data = _Monochrome(48 + 320 * 200);
    for (var i = 0; i < 48; ++i) {
      var nibble = (i * 5 + 1) & 15;
      data[i] = (byte)(widened ? nibble * 17 : nibble);
    }

    return data;
  }

  /// <summary>
  /// A RAG-D picture. Eight bitplanes and one byte a pixel occupy exactly the same space, so the
  /// same bytes stand in for both and only the extension separates them.
  /// </summary>
  private static byte[] _RagD(int planes, int paletteLength) {
    const int width = 64, height = 40;
    var bitmap = planes == 16 ? width * height * 2 : height * (width >> 3) * planes;
    var data = _Monochrome(30 + paletteLength + bitmap);

    "RAG-D!"u8.CopyTo(data);
    data[6] = data[7] = 0;
    data[12] = (byte)(width >> 8);
    data[13] = (byte)width;
    data[14] = (byte)((height - 1) >> 8);
    data[15] = (byte)(height - 1);
    data[16] = 0;
    data[17] = (byte)planes;
    data[18] = data[19] = 0;
    data[20] = (byte)(paletteLength >> 8);
    data[21] = (byte)paletteLength;

    return data;
  }

  /// <summary>A rendered SAM Coupe dump, whose every byte must name one of the 128 colours.</summary>
  private static byte[] _SamCoupeChunky() {
    var data = _Monochrome(98304);
    for (var i = 0; i < data.Length; ++i)
      data[i] &= 127;

    return data;
  }

  /// <summary>
  /// A PI8 picture wrapped in an Atari executable header, which is only a header when the address
  /// range it declares accounts for the rest of the file — so the picture is a row shorter.
  /// </summary>
  private static byte[] _Pi8Executable() {
    var data = _Monochrome(7685);
    var start = 0x4000;
    var end = start + 7685 - 6 - 1;
    data[0] = data[1] = 0xFF;
    data[2] = (byte)start;
    data[3] = (byte)(start >> 8);
    data[4] = (byte)end;
    data[5] = (byte)(end >> 8);

    return data;
  }

  /// <summary>
  /// A ZZ_ROUGH picture: the count stream's length written as decimal text, then the palette and
  /// the two streams. The counts avoid zero, which would be a run that never advances.
  /// </summary>
  private static byte[] _ZzRough() {
    // The traversal visits 8000 four-byte groups, so the counts have to add up to exactly that or
    // the decoder runs off one end of the file or the other.
    const int run = 10, countLength = 8000 / run;
    var header = System.Text.Encoding.ASCII.GetBytes($"(c)F.MARCHAL{countLength}\r\n");

    var data = _Monochrome(header.Length + 32 + countLength + countLength * 4 + 8);
    header.CopyTo(data, 0);

    var counts = header.Length + 32;
    for (var i = 0; i < countLength; ++i)
      data[counts + i] = run;

    return data;
  }

  /// <summary>A Taquart Interlace Picture: three fields of equal length behind a five-byte header.</summary>
  private static byte[] _Tip(int width, int height) {
    var fieldLength = (width >> 2) * height;
    var data = _Monochrome(9 + 3 * fieldLength);

    "TIP"u8.CopyTo(data);
    data[3] = 1;
    data[4] = 0;
    data[5] = (byte)width;
    data[6] = (byte)height;
    data[7] = (byte)fieldLength;
    data[8] = (byte)(fieldLength >> 8);

    return data;
  }

  /// <summary>
  /// A VDC BitMap. Version 3's five escape bytes are chosen by the file, so the packed probe names
  /// values it then avoids as literals and exercises all five kinds of run.
  /// </summary>
  private static byte[] _Vbm(int version, bool packed) {
    const int width = 80, height = 50;
    var stride = (width + 7) >> 3;

    if (version == 2) {
      var raw = _Monochrome(8 + stride * height);
      raw[0] = (byte)'B';
      raw[1] = (byte)'M';
      raw[2] = 0xCB;
      raw[3] = 2;
      raw[4] = (byte)(width >> 8);
      raw[5] = (byte)width;
      raw[6] = (byte)(height >> 8);
      raw[7] = (byte)height;

      return raw;
    }

    var body = new System.Collections.Generic.List<byte>();
    if (packed) {
      // Run of a named value, run of zeros, run of ones, a pair of each, then literals.
      for (var i = 0; i < stride * height / 12 + 1; ++i) {
        body.AddRange([0xF0, (byte)(i * 37), 3]);
        body.AddRange([0xF1, 2]);
        body.AddRange([0xF2, 2]);
        body.Add(0xF3);
        body.Add(0xF4);
        body.Add((byte)(i * 91));
      }
    } else
      for (var i = 0; i < stride * height; ++i)
        body.Add((byte)(i * 47 + (i >> 7)));

    var data = new byte[18 + body.Count];
    data[0] = (byte)'B';
    data[1] = (byte)'M';
    data[2] = 0xCB;
    data[3] = 3;
    data[4] = (byte)(width >> 8);
    data[5] = (byte)width;
    data[6] = (byte)(height >> 8);
    data[7] = (byte)height;
    data[8] = (byte)(packed ? 1 : 0);
    data[9] = 0xF0;
    data[10] = 0xF1;
    data[11] = 0xF2;
    data[12] = 0xF3;
    data[13] = 0xF4;
    data[16] = data[17] = 0;
    body.CopyTo(data, 18);

    return data;
  }

  /// <summary>An Atari Player Editor sheet, whose size is fixed however few frames it holds.</summary>
  private static byte[] _PlayerEditor(int frames, int height, int gap) {
    var data = _Monochrome(1677);
    ReadOnlySpan<byte> signature = [154, 248, 57, 33];
    signature.CopyTo(data);
    data[4] = (byte)frames;
    data[5] = (byte)height;
    data[6] = (byte)gap;

    return data;
  }

  /// <summary>
  /// A PMG Designer sheet. One row and several rows are laid out to different widths, so both are
  /// worth a probe.
  /// </summary>
  private static byte[] _PmgDesigner(int sprites, int shapesX, int shapesY, int height) {
    var shapes = shapesX * shapesY;
    var data = _Monochrome(11 + sprites * shapes * height);
    ReadOnlySpan<byte> signature = [240, 237, 228];
    signature.CopyTo(data);
    data[7] = (byte)sprites;
    data[8] = (byte)shapesX;
    data[9] = (byte)shapesY;
    data[10] = (byte)height;

    return data;
  }

  /// <summary>A Ludek Maker sheet, identified by its title written with every high bit set.</summary>
  private static byte[] _LudekMaker(int shapes) {
    var data = _Monochrome(281 + shapes * 120);
    const string title = "Ludek Maker data file";
    for (var i = 0; i < title.Length; ++i)
      data[i] = (byte)(title[i] + 128);

    data[23] = 0;
    data[24] = (byte)shapes;

    return data;
  }

  /// <summary>
  /// A Daisy-Dot font. Characters are variable width with no index, so their widths vary across the
  /// ninety-one on purpose — a decoder that assumed a fixed stride would drift into the next one.
  /// </summary>
  private static byte[] _DaisyDot() {
    var body = new System.Collections.Generic.List<byte>();
    for (var i = 0; i < 91; ++i) {
      var width = i % 19 + 1;
      body.Add((byte)width);
      for (var column = 0; column < width * 2; ++column)
        body.Add((byte)(i * 31 + column * 13));

      body.Add(155);
    }

    var data = new byte[19 + body.Count];
    System.Text.Encoding.ASCII.GetBytes("DAISY-DOT NLQ FONT").CopyTo(data, 0);
    data[18] = 155;
    body.CopyTo(data, 19);

    return data;
  }

  /// <summary>An Atari Graphics Studio picture, whose mode byte picks between two unrelated screens.</summary>
  private static byte[] _Ags(int mode, int stored, int rows) {
    var data = _Monochrome(16 + (stored * rows << 1));
    System.Text.Encoding.ASCII.GetBytes("AGS").CopyTo(data, 0);
    data[3] = (byte)mode;
    data[4] = (byte)stored;
    data[5] = (byte)rows;
    data[6] = (byte)(rows >> 8);

    return data;
  }

  /// <summary>A DEGAS Elite brush, whose every byte must be exactly zero or one.</summary>
  private static byte[] _DegasBrush() {
    var data = new byte[64];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)((i * 7 + (i >> 3)) & 1);

    return data;
  }

  /// <summary>An unpacked Grafix picture, whose header is 1586 bytes for a VDI palette of 96.</summary>
  private static byte[] _Grafix(int width, int height, int colors) {
    var planes = colors switch { 2 => 1, 4 => 2, 16 => 4, _ => 8 };
    var stride = ((width + 15) >> 4) * planes * 2;
    var bitmapLength = stride * height;

    var data = _Monochrome(1586 + bitmapLength);
    System.Text.Encoding.ASCII.GetBytes("GRXP").CopyTo(data, 0);
    data[4] = data[5] = 1;
    data[28] = 0;
    data[29] = 0;
    data[30] = (byte)(width >> 8);
    data[31] = (byte)width;
    data[32] = (byte)(height >> 8);
    data[33] = (byte)height;
    data[34] = (byte)(colors >> 8);
    data[35] = (byte)colors;

    // Intensities are per thousand, so the stored words have to stay in range to mean anything.
    for (var i = 0; i < colors * 6; i += 2) {
      var thousandths = (i * 97) % 1001;
      data[36 + i] = (byte)(thousandths >> 8);
      data[37 + i] = (byte)thousandths;
    }

    data[1574] = (byte)(bitmapLength >> 24);
    data[1575] = (byte)(bitmapLength >> 16);
    data[1576] = (byte)(bitmapLength >> 8);
    data[1577] = (byte)bitmapLength;

    return data;
  }

  /// <summary>
  /// A packed Grafix picture, encoded here rather than taken from a fixture.
  /// </summary>
  /// <remarks>
  /// The stream is literals with the two control codes where the dictionary demands them, plus one
  /// back-reference so the copy loop is exercised too. Nine-bit codes can only name entries below
  /// 512 and the dictionary grows by one per code, so a widening code has to be emitted before it
  /// reaches that — which is the part of the format a probe of literals alone would never reach.
  /// </remarks>
  private static byte[] _PackedGrafix() {
    var width = 320;
    var height = 200;
    var colors = 16;
    var planes = 4;
    var stride = ((width + 15) >> 4) * planes * 2;
    var bitmapLength = stride * height;
    var half = bitmapLength >> 1;

    var first = _EncodeGrafixHalf(half, 0);
    var second = _EncodeGrafixHalf(bitmapLength - half, 1);

    var data = new byte[1586 + first.Length + second.Length];
    System.Text.Encoding.ASCII.GetBytes("GRXP").CopyTo(data, 0);
    data[4] = data[5] = 1;
    data[29] = 1;
    data[30] = (byte)(width >> 8);
    data[31] = (byte)width;
    data[32] = (byte)(height >> 8);
    data[33] = (byte)height;
    data[34] = (byte)(colors >> 8);
    data[35] = (byte)colors;

    for (var i = 0; i < colors * 6; i += 2) {
      var thousandths = (i * 97) % 1001;
      data[36 + i] = (byte)(thousandths >> 8);
      data[37 + i] = (byte)thousandths;
    }

    foreach (var (offset, value) in ((int, int)[])[(1574, bitmapLength), (1578, first.Length), (1582, second.Length)]) {
      data[offset] = (byte)(value >> 24);
      data[offset + 1] = (byte)(value >> 16);
      data[offset + 2] = (byte)(value >> 8);
      data[offset + 3] = (byte)value;
    }

    first.CopyTo(data, 1586);
    second.CopyTo(data, 1586 + first.Length);

    return data;
  }

  /// <summary>Encodes one half of a packed Grafix picture into exactly the codes it needs.</summary>
  private static byte[] _EncodeGrafixHalf(int length, int seed) {
    var bytes = new System.Collections.Generic.List<byte>();
    int bits = 0, bitsCount = 0, codes = 258, codeBits = 9;

    void Emit(int code) {
      bits |= code << bitsCount;
      bitsCount += codeBits;
      while (bitsCount >= 8) {
        bytes.Add((byte)bits);
        bits >>= 8;
        bitsCount -= 8;
      }
    }

    for (var written = 0; written < length;) {
      // Every emitted code adds a dictionary entry, and a nine-bit code cannot name entry 512.
      if (codes == (1 << codeBits)) {
        if (codeBits == 9) {
          Emit(256);
          codeBits = 10;
        } else {
          Emit(257);
          codes = 258;
          codeBits = 9;
        }

        continue;
      }

      // Entry 258 spans the first two bytes written, so naming it emits exactly two.
      if (written == 64 && length - written >= 2) {
        Emit(258);
        written += 2;
      } else {
        Emit((byte)(written * 47 + seed * 13 + (written >> 7)));
        ++written;
      }

      ++codes;
    }

    if (bitsCount > 0)
      bytes.Add((byte)bits);

    return bytes.ToArray();
  }

  /// <summary>An InShape picture in one of its four forms.</summary>
  private static byte[] _InShape(int mode, int width, int height) {
    var count = width * height;
    var pixels = mode switch {
      0 => ((width + 7) >> 3) * height,
      1 => count,
      4 => count * 3,
      _ => count * 4,
    };

    var data = _Monochrome(16 + pixels);
    System.Text.Encoding.ASCII.GetBytes("IS_IMAGE").CopyTo(data, 0);
    data[8] = 0;
    data[9] = (byte)mode;
    data[12] = (byte)(width >> 8);
    data[13] = (byte)width;
    data[14] = (byte)(height >> 8);
    data[15] = (byte)height;

    return data;
  }

  /// <summary>
  /// A Picworks picture. Its runs work in eight-byte groups, and whatever they leave unaccounted
  /// for is stored plainly at the end — so the counts have to add up to less than the screen.
  /// </summary>
  private static byte[] _Picworks() {
    const int groups = 32000 / 8;
    const int pairs = 120;
    var literalGroups = 10;
    var repeatedGroups = 6;

    var counts = new byte[(1 + pairs) * 4];
    counts[0] = (byte)(pairs >> 8);
    counts[1] = (byte)pairs;

    var values = new System.Collections.Generic.List<byte>();
    var covered = 0;
    for (var pair = 0; pair < pairs; ++pair) {
      var at = (pair + 1) * 4;
      counts[at] = (byte)(literalGroups >> 8);
      counts[at + 1] = (byte)literalGroups;
      counts[at + 2] = (byte)(repeatedGroups >> 8);
      counts[at + 3] = (byte)repeatedGroups;

      for (var i = 0; i < literalGroups * 8; ++i)
        values.Add((byte)(pair * 29 + i * 11));

      for (var i = 0; i < 8; ++i)
        values.Add((byte)(pair * 53 + i));

      covered += (literalGroups + repeatedGroups) * 8;
    }

    Assert.That(covered, Is.LessThan(groups * 8), "the runs must leave a tail to store plainly");

    var tail = groups * 8 - covered;
    var data = new byte[counts.Length + values.Count + tail];
    counts.CopyTo(data, 0);
    values.CopyTo(data, counts.Length);
    for (var i = 0; i < tail; ++i)
      data[counts.Length + values.Count + i] = (byte)(i * 47 + (i >> 7));

    return data;
  }

  /// <summary>
  /// A Best Paint picture. Only the lower half of the VIC-20's palette can be a foreground, so the
  /// cell colours are masked to it — a higher one makes the file malformed rather than merely odd.
  /// </summary>
  private static byte[] _BestPaint() {
    var data = _Monochrome(4083);
    data[0] = 0;
    data[1] = 17;
    for (var cell = 0; cell < 12 * 20; ++cell)
      data[3842 + cell] = (byte)(cell % 8);

    return data;
  }

  /// <summary>A TmS Cranach Paint picture at one of its three depths.</summary>
  private static byte[] _Cranach(int depth, int width, int height) {
    var count = width * height;
    var pixels = depth switch { 1 => ((width + 7) >> 3) * height, 8 => count, _ => count * 3 };

    var data = _Monochrome(812 + pixels);
    System.Text.Encoding.ASCII.GetBytes("TMS").CopyTo(data, 0);
    data[3] = 0;
    data[4] = 3;
    data[5] = 44;
    data[6] = (byte)(width >> 8);
    data[7] = (byte)width;
    data[8] = (byte)(height >> 8);
    data[9] = (byte)height;
    data[10] = 0;
    data[11] = (byte)depth;

    return data;
  }

  /// <summary>
  /// A SymbOS graphic of two rows of two chunks each. Rows are separated by a marker and must come
  /// to the same width, so a decoder that ignored the marker would run them together.
  /// </summary>
  private static byte[] _SymbOs(bool wide) {
    var body = new System.Collections.Generic.List<byte>();
    var fill = 0;

    void Chunk(int chunkWidth, int chunkHeight) {
      if (wide) {
        var stride = (chunkWidth + 1) >> 1;
        body.AddRange([64, 5, (byte)stride, (byte)(stride >> 8), (byte)chunkWidth, (byte)(chunkWidth >> 8),
          (byte)chunkHeight, (byte)(chunkHeight >> 8)]);
        for (var i = 0; i < stride * chunkHeight; ++i)
          body.Add((byte)(fill++ * 47 + (fill >> 5)));
      } else {
        var stride = (chunkWidth + 3) >> 2;
        body.AddRange([(byte)stride, (byte)chunkWidth, (byte)chunkHeight]);
        for (var i = 0; i < stride * chunkHeight; ++i)
          body.Add((byte)(fill++ * 47 + (fill >> 5)));
      }
    }

    Chunk(40, 24);
    Chunk(24, 24);
    body.AddRange([255, 0, 0]);
    Chunk(32, 16);
    Chunk(32, 16);
    // No marker after the last row: its width is what the file's own width is checked against.
    body.AddRange([0, 0, 0, 0]);

    return body.ToArray();
  }

  /// <summary>A SEUCK sprite set, identified by two bytes and its length.</summary>
  private static byte[] _Seuck() {
    var data = _Monochrome(8130);
    data[0] = 66;
    data[1] = 0;

    return data;
  }

  /// <summary>
  /// A MINIPAINT picture. Its cell colours deliberately span both halves of the nibble range, so
  /// both the two-colour and the four-colour reading of the bitmap are exercised.
  /// </summary>
  private static byte[] _MiniPaint() {
    var data = _Monochrome(4097);
    ReadOnlySpan<byte> signature = [241, 16, 12, 18, 216, 7, 158, 32, (byte)'8', (byte)'5', (byte)'8', (byte)'4', 0, 0, 0];
    signature.CopyTo(data);

    for (var cell = 0; cell < 12 * 10; ++cell)
      data[3857 + cell] = (byte)(cell * 37 + (cell >> 3));

    return data;
  }

  /// <summary>
  /// A PaintShop picture, either stored outright or with every one of its line commands used at
  /// least once — including both lengths of the repeat, which is the only part that reads back
  /// what it has already written.
  /// </summary>
  private static byte[] _PaintShop(bool stored) {
    var width = 320;
    var height = 400;
    var stride = (width + 7) >> 3;

    var head = new byte[14];
    System.Text.Encoding.ASCII.GetBytes("tm89").CopyTo(head, 0);
    head[8] = 2;
    head[9] = 1;
    head[10] = (byte)((width - 1) >> 8);
    head[11] = (byte)(width - 1);
    head[12] = (byte)((height - 1) >> 8);
    head[13] = (byte)(height - 1);

    var body = new System.Collections.Generic.List<byte>(head);

    if (stored) {
      body.Add(99);
      for (var i = 0; i < stride * height; ++i)
        body.Add((byte)(i * 47 + (i >> 7)));

      body.Add(255);

      return body.ToArray();
    }

    for (var line = 0; line < height;) {
      // A literal line first, so the repeats below have something to read back.
      body.Add(110);
      for (var i = 0; i < stride; ++i)
        body.Add((byte)(line * 31 + i * 11));

      ++line;

      foreach (var command in (byte[])[0, 200, 100, 102]) {
        if (line >= height)
          break;

        body.Add(command);
        if (command == 100)
          body.Add((byte)(line * 17));
        else if (command == 102) {
          body.Add((byte)(line * 13));
          body.Add((byte)(line * 29));
        }

        ++line;
      }

      if (line < height) {
        var repeat = Math.Min(4, height - line);
        body.Add(10);
        body.Add((byte)(repeat - 1));
        line += repeat;
      }
    }

    body.Add(255);

    return body.ToArray();
  }

  /// <summary>
  /// A Kompresor do Animatora animation. The map names tiles that must all exist, so the counts and
  /// the tile block have to agree — and being an Atari executable is the only signature there is,
  /// which means the declared block has to account for the file exactly.
  /// </summary>
  private static byte[] _Animator(int frames, int columns, int rows, int tileCount) {
    var map = frames * columns * rows;
    var length = 11 + map + tileCount * 8;

    var data = _Monochrome(length);
    data[0] = data[1] = 0xFF;

    var start = 0x4000;
    var end = start + length - 6 - 1;
    data[2] = (byte)start;
    data[3] = (byte)(start >> 8);
    data[4] = (byte)end;
    data[5] = (byte)(end >> 8);

    data[8] = (byte)frames;
    data[9] = (byte)columns;
    data[10] = (byte)rows;

    for (var i = 0; i < map; ++i)
      data[11 + i] = (byte)(i % tileCount);

    return data;
  }

  /// <summary>
  /// A Trzmiel picture in one of its three forms. The packed ones use every kind of command the
  /// encoding has: repeated runs, literal runs, and the escape that takes a two-byte count.
  /// </summary>
  private static byte[] _Trzmiel(int type) {
    if (type == 0) {
      var stored = _Monochrome(1 + 7680);
      stored[0] = 0;

      return stored;
    }

    var body = new System.Collections.Generic.List<byte> { (byte)type };
    var fill = 0;

    for (var written = 0; written < 7680;) {
      var left = 7680 - written;

      // A long repeated run, through the two-byte escape.
      if (left >= 400) {
        body.AddRange([0, 1, 0x90, (byte)(fill++ * 37)]);
        written += 400;
        continue;
      }

      // A short repeated run.
      if (left >= 9) {
        body.AddRange([5, (byte)(fill++ * 53)]);
        written += 5;

        // A run of literals.
        body.Add(128 + 4);
        for (var i = 0; i < 4; ++i)
          body.Add((byte)(fill++ * 29 + i));

        written += 4;
        continue;
      }

      body.Add((byte)(128 + left));
      for (var i = 0; i < left; ++i)
        body.Add((byte)(fill++ * 11 + i));

      written += left;
    }

    return body.ToArray();
  }

  /// <summary>
  /// A Grass' Slideshow picture. The byte after the packed screen names one of the program's own
  /// register sets, so one probe names a set and one names nothing and falls back to a grey ramp.
  /// </summary>
  private static byte[] _GrassSlideshow(int palette) {
    var body = new System.Collections.Generic.List<byte>();
    var fill = 0;

    for (var written = 0; written < 7680;) {
      var left = 7680 - written;

      if (left >= 200) {
        body.AddRange([0, (byte)(fill++ * 37), 200]);
        written += 200;
        continue;
      }

      var literals = Math.Min(left, 60);
      body.Add((byte)literals);
      for (var i = 0; i < literals; ++i)
        body.Add((byte)(fill++ * 29 + i));

      written += literals;
    }

    body.Add((byte)palette);

    return body.ToArray();
  }

  /// <summary>
  /// An XL-Paint picture. The unmarked form says nothing about its height, so its stream has to
  /// fill exactly one of the two lengths — which is what a probe of each is for.
  /// </summary>
  private static byte[] _XlPaint(bool marked, int height) {
    var body = new System.Collections.Generic.List<byte>();
    if (marked) {
      body.AddRange(System.Text.Encoding.ASCII.GetBytes("XLPC"));
      body.AddRange([2, 6, 10, 0]);
    } else
      body.AddRange([2, 6, 10, 0]);

    var fill = 0;
    for (var written = 0; written < height * 80;) {
      var left = height * 80 - written;

      // A long repeated run, through the escape that spends fourteen bits on the count.
      if (left >= 300) {
        body.AddRange([128 + 64 + 1, 44, (byte)(fill++ * 37)]);
        written += 300;
        continue;
      }

      // A short repeated run and then a run of literals.
      if (left >= 12) {
        body.AddRange([128 + 6, (byte)(fill++ * 53)]);
        written += 6;

        body.Add(6);
        for (var i = 0; i < 6; ++i)
          body.Add((byte)(fill++ * 29 + i));

        written += 6;
        continue;
      }

      body.Add((byte)left);
      for (var i = 0; i < left; ++i)
        body.Add((byte)(fill++ * 11 + i));

      written += left;
    }

    return body.ToArray();
  }

  /// <summary>
  /// A DelmPaint picture. Every block names its own escape byte, default value and stride, and a
  /// stride of zero means the block is that default all the way through — so one block of each
  /// kind appears, including one the stream contributes nothing to at all.
  /// </summary>
  private static byte[] _DelmPaint(int blocks) {
    var lengths = new System.Collections.Generic.List<byte>();
    var bodies = new System.Collections.Generic.List<byte>();

    // The last block of the small form is the file's remainder and so is not counted.
    var total = blocks == 2 ? blocks + 1 : blocks;

    for (var block = 0; block < total; ++block) {
      // One block says only "the default value, all of it" and carries no stream at all.
      var body = block == 1 ? (byte[])[0xFE, (byte)(block * 7 + 3), 0, 0] : _CaBlock(block);

      if (block < blocks)
        lengths.AddRange([
          (byte)(body.Length >> 24), (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length,
        ]);

      bodies.AddRange(body);
    }

    var data = new byte[lengths.Count + bodies.Count];
    lengths.CopyTo(data, 0);
    bodies.CopyTo(data, lengths.Count);

    return data;
  }

  /// <summary>
  /// A compressed D-GRAPH picture. Each block's length is written as decimal text before it, and
  /// the second length sits where the first block's stream stopped — so the two lengths have to be
  /// measured rather than guessed, and the file cannot be assembled back to front.
  /// </summary>
  private static byte[] _DGraph() {
    var first = _CaBlock(0);
    var second = _CaBlock(1);

    var head = System.Text.Encoding.ASCII.GetBytes($"{first.Length}\r\n");
    var middle = System.Text.Encoding.ASCII.GetBytes($"{second.Length}\r\n");

    var data = new byte[head.Length + 32 + first.Length + middle.Length + second.Length];
    var at = 0;
    head.CopyTo(data, at);
    at += head.Length;

    // The palette, sixteen ST colour words.
    for (var i = 0; i < 16; ++i) {
      data[at + i * 2] = (byte)((i * 3) & 7);
      data[at + i * 2 + 1] = (byte)(((i * 5) & 7) << 4 | ((i * 7) & 7));
    }

    at += 32;
    first.CopyTo(data, at);
    at += first.Length;
    middle.CopyTo(data, at);
    at += middle.Length;
    second.CopyTo(data, at);

    return data;
  }

  /// <summary>One block of the ST packers' shared encoding, using every command it has.</summary>
  private static byte[] _CaBlock(int seed) {
    const byte escape = 0xFE;
    var body = new System.Collections.Generic.List<byte> { escape, (byte)(seed * 11), 0, 1 };
    var fill = seed * 31;

    for (var written = 0; written < 32000;) {
      var left = 32000 - written;

      if (left >= 4096) {
        body.AddRange([escape, 1, 0x0F, 0xFF, (byte)(fill++ * 13)]);
        written += 4096;
        continue;
      }

      // A counted run of one value, then a run of the block's own default. The latter's count is
      // two bytes and a high byte of zero means the whole block, so 257 is the shortest it says.
      if (left >= 32 + 257) {
        body.AddRange([escape, 0, 31, (byte)(fill++ * 17)]);
        written += 32;
        body.AddRange([escape, 2, 1, 0]);
        written += 257;
        continue;
      }

      var value = (byte)(fill++ * 23 + written);
      body.Add(value);
      if (value == escape)
        body.Add(escape);

      ++written;
    }

    return body.ToArray();
  }

  /// <summary>
  /// A compressed Champions' Interlace picture: four streams, each preceded by four bytes the
  /// decoder steps over, and each unpacking to a length of its own.
  /// </summary>
  private static byte[] _Cci() {
    var body = new System.Collections.Generic.List<byte>(System.Text.Encoding.ASCII.GetBytes("CIN 1.2 "));
    var fill = 0;

    void Stream(int length) {
      body.AddRange([0, 0, 0, 0]);

      for (var written = 0; written < length;) {
        var left = length - written;

        if (left >= 128) {
          // A repeated run, counted from one rather than zero.
          body.AddRange([(byte)(127 + 100), (byte)(fill++ * 37)]);
          written += 100;
          continue;
        }

        var literals = Math.Min(left, 28);
        body.Add((byte)(literals - 1));
        for (var i = 0; i < literals; ++i)
          body.Add((byte)(fill++ * 29 + i));

        written += literals;
      }
    }

    Stream(3840);
    Stream(3840);
    Stream(7680);
    Stream(1024);

    return body.ToArray();
  }

  /// <summary>
  /// A CharPad project. The three places a foreground colour can come from and the two ways a tile
  /// names its characters change where everything after the character set sits, so each
  /// combination is a different file layout rather than a different reading of one.
  /// </summary>
  private static byte[] _CharPad(int colorMethod, bool tiles, bool implied, bool multi) {
    var characters = 48;
    var tileCount = tiles ? 12 : 0;
    var tileWidth = tiles ? 2 : 1;
    var tileHeight = tiles ? 2 : 1;
    var mapWidth = 5;
    var mapHeight = 4;

    var tilesOffset = 20 + characters * 9;
    var tileColorsOffset = implied ? tilesOffset : tilesOffset + tileCount * (tileWidth * tileHeight * 2);
    var mapOffset = colorMethod == 1 ? tileColorsOffset + tileCount : tileColorsOffset;
    var length = mapOffset + mapWidth * mapHeight * 2;

    var data = _Monochrome(length);
    System.Text.Encoding.ASCII.GetBytes("CTM").CopyTo(data, 0);
    data[3] = 5;
    data[8] = (byte)colorMethod;
    data[9] = (byte)((tiles ? 1 : 0) | (implied ? 2 : 0) | (multi ? 4 : 0));
    data[10] = (byte)(characters - 1);
    data[11] = 0;
    data[12] = (byte)(tiles ? tileCount - 1 : 0);
    data[13] = 0;
    data[14] = (byte)tileWidth;
    data[15] = (byte)tileHeight;
    data[16] = (byte)mapWidth;
    data[17] = 0;
    data[18] = (byte)mapHeight;
    data[19] = 0;

    // Every tile slot must name a character the set holds, and every map entry a tile that exists.
    if (tiles && !implied)
      for (var slot = 0; slot < tileCount * tileWidth * tileHeight; ++slot) {
        data[tilesOffset + slot * 2] = (byte)(slot % characters);
        data[tilesOffset + slot * 2 + 1] = 0;
      }

    for (var entry = 0; entry < mapWidth * mapHeight; ++entry) {
      data[mapOffset + entry * 2] = (byte)(entry % (tiles ? tileCount : characters));
      data[mapOffset + entry * 2 + 1] = 0;
    }

    return data;
  }

  /// <summary>
  /// An AMOS sprite bank. Sprites sit side by side and the palette closes the file, so where the
  /// palette lands is what confirms the sprites were walked correctly.
  /// </summary>
  private static byte[] _AmosSprites(string kind) {
    var body = new System.Collections.Generic.List<byte>(System.Text.Encoding.ASCII.GetBytes(kind));
    var sprites = 3;
    body.AddRange([(byte)(sprites >> 8), (byte)sprites]);

    var fill = 0;
    for (var sprite = 0; sprite < sprites; ++sprite) {
      var words = sprite + 1;
      var spriteHeight = 24;
      var planes = 4;
      body.AddRange([
        (byte)(words >> 8), (byte)words,
        (byte)(spriteHeight >> 8), (byte)spriteHeight,
        0, (byte)planes, 0, 0, 0, 0,
      ]);

      for (var i = 0; i < words * 2 * spriteHeight * planes; ++i)
        body.Add((byte)(fill++ * 47 + (fill >> 6)));
    }

    for (var i = 0; i < 64; ++i)
      body.Add((byte)(i * 37));

    return body.ToArray();
  }

  /// <summary>
  /// A packed AMOS screen, encoded here. Its control stream is itself compressed, so the probe has
  /// to build all three streams in step rather than emit a picture and a flag per byte.
  /// </summary>
  private static byte[] _AmosScreen() {
    var width = 4;
    var lumps = 3;
    var lumpLines = 8;
    var planes = 2;
    var height = lumps * lumpLines;

    // The three streams, built by running the decoder's own loop forwards.
    var pic = new System.Collections.Generic.List<byte>();
    var rle = new System.Collections.Generic.List<byte>();
    var points = new System.Collections.Generic.List<byte>();

    var rleByte = 0;
    var rleCount = 0;
    var pointsByte = 0;
    var pointsCount = 0;
    var fill = 0;

    void PushPoint(int bit) {
      pointsByte = (pointsByte << 1) | bit;
      if (++pointsCount != 8)
        return;

      points.Add((byte)pointsByte);
      pointsByte = 0;
      pointsCount = 0;
    }

    void PushRle(int bit) {
      rleByte = (rleByte << 1) | bit;
      if (++rleCount != 8)
        return;

      rle.Add((byte)rleByte);
      // Every control byte here is a fresh one rather than a continuation.
      PushPoint(1);
      rleByte = 0;
      rleCount = 0;
    }

    var total = planes * lumps * width * lumpLines;
    for (var i = 0; i < total; ++i) {
      // Take a new picture byte every fifth output byte, repeating the last otherwise.
      var fresh = i % 5 == 0;
      PushRle(fresh ? 1 : 0);
      if (fresh)
        pic.Add((byte)(fill++ * 29 + (fill >> 4)));
    }

    while (rleCount != 0)
      PushRle(0);

    while (pointsCount != 0)
      PushPoint(1);

    // The first picture byte sits in the header, and the first control byte is consumed at once.
    var head = new byte[135];
    System.Text.Encoding.ASCII.GetBytes("AmBk").CopyTo(head, 0);
    System.Text.Encoding.ASCII.GetBytes("Pac.Pic").CopyTo(head, 12);
    head[110] = 6;
    head[111] = 7;
    head[112] = 25;
    head[113] = 99;
    head[118] = (byte)(width >> 8);
    head[119] = (byte)width;
    head[120] = (byte)(lumps >> 8);
    head[121] = (byte)lumps;
    head[122] = (byte)(lumpLines >> 8);
    head[123] = (byte)lumpLines;
    head[124] = 0;
    head[125] = (byte)planes;

    for (var i = 0; i < 64; ++i)
      head[46 + i] = (byte)(i * 41);

    var rleOffset = 135 + pic.Count;
    var pointsOffset = rleOffset + rle.Count;
    foreach (var (offset, value) in ((int, int)[])[(126, rleOffset - 110), (130, pointsOffset - 110)]) {
      head[offset] = (byte)(value >> 24);
      head[offset + 1] = (byte)(value >> 16);
      head[offset + 2] = (byte)(value >> 8);
      head[offset + 3] = (byte)value;
    }

    head[134] = 0;

    var data = new byte[pointsOffset + points.Count];
    head.CopyTo(data, 0);
    pic.CopyTo(data, 135);
    rle.CopyTo(data, rleOffset);
    points.CopyTo(data, pointsOffset);

    return data;
  }

  /// <summary>
  /// A packed Super Hires FLI picture. Its escape byte is named by the file, so the probe picks one
  /// and then has to write that byte as a run of one wherever it occurs as a literal.
  /// </summary>
  private static byte[] _PackedShf() {
    const byte escape = 0xC7;
    var body = new System.Collections.Generic.List<byte> { 0, 0, escape };
    var fill = 0;

    for (var written = 0; written < 8170;) {
      var left = 8170 - written;

      // A run of the longest kind, which the count of zero stands for.
      if (left >= 256) {
        body.AddRange([escape, 0, (byte)(fill++ * 37)]);
        written += 256;
        continue;
      }

      if (left >= 40) {
        body.AddRange([escape, 40, (byte)(fill++ * 53)]);
        written += 40;
        continue;
      }

      var value = (byte)(fill++ * 29 + written);
      if (value == escape)
        body.AddRange([escape, 1, value]);
      else
        body.Add(value);

      ++written;
    }

    return body.ToArray();
  }

  /// <summary>An Extend Super Hires picture stored outright, marked by a zero third byte.</summary>
  private static byte[] _Esh() {
    var data = _Monochrome(20454);
    data[2] = 0;

    return data;
  }

  /// <summary>
  /// A packed Extend Super Hires picture. Its counts are the seven low bits exactly, so a command
  /// of 0 or 128 does nothing — the probe avoids both rather than relying on that.
  /// </summary>
  private static byte[] _PackedEsh() {
    var body = new System.Collections.Generic.List<byte> { 0, 0, 1 };
    var fill = 0;

    for (var written = 3; written < 20452;) {
      var left = 20452 - written;

      if (left >= 127) {
        body.AddRange([128 + 127, (byte)(fill++ * 37)]);
        written += 127;
        continue;
      }

      var literals = Math.Min(left, 40);
      body.Add((byte)literals);
      for (var i = 0; i < literals; ++i)
        body.Add((byte)(fill++ * 29 + i));

      written += literals;
    }

    return body.ToArray();
  }

  /// <summary>
  /// A UIFLI picture, whose packing runs backwards — the last byte of the file is the first one
  /// read — so the probe builds its commands and then reverses the whole stream.
  /// </summary>
  private static byte[] _Uifli() {
    const byte escape = 0xB3;
    var backwards = new System.Collections.Generic.List<byte>();
    var fill = 0;

    for (var written = 0; written < 32576;) {
      var left = 32576 - written;

      // In reading order a run is escape, count, value; the count of zero stands for 256.
      if (left >= 256) {
        backwards.AddRange([escape, 0, (byte)(fill++ * 37)]);
        written += 256;
        continue;
      }

      if (left >= 50) {
        backwards.AddRange([escape, 50, (byte)(fill++ * 53)]);
        written += 50;
        continue;
      }

      var value = (byte)(fill++ * 29 + written);
      if (value == escape)
        backwards.AddRange([escape, 1, value]);
      else
        backwards.Add(value);

      ++written;
    }

    // The reader starts at the end, so the commands go into the file in reverse.
    backwards.Reverse();

    var data = new byte[3 + backwards.Count];
    data[2] = escape;
    backwards.CopyTo(data, 3);

    return data;
  }

  /// <summary>
  /// A packed SHF-XL picture. Its escape byte is the last byte of the file, which follows from the
  /// packing running backwards — the first thing a backwards reader meets is the last thing written.
  /// </summary>
  private static byte[] _PackedShx() {
    const byte escape = 0x5D;
    // Reading order, which the file then stores reversed: the escape is read before any command.
    var backwards = new System.Collections.Generic.List<byte> { escape };
    var fill = 0;

    for (var written = 0; written < 9168;) {
      var left = 9168 - written;

      if (left >= 256) {
        backwards.AddRange([escape, 0, (byte)(fill++ * 37)]);
        written += 256;
        continue;
      }

      if (left >= 30) {
        backwards.AddRange([escape, 30, (byte)(fill++ * 53)]);
        written += 30;
        continue;
      }

      var value = (byte)(fill++ * 29 + written);
      if (value == escape)
        backwards.AddRange([escape, 1, value]);
      else
        backwards.Add(value);

      ++written;
    }

    backwards.Reverse();

    var data = new byte[2 + backwards.Count];
    backwards.CopyTo(data, 2);

    return data;
  }

  /// <summary>
  /// A Commodore Grafix file: a RIFF wrapper with a format chunk, a metadata chunk a decoder must
  /// step over, and the frames.
  /// </summary>
  private static byte[] _Grafix(int matrixColumns, int matrixRows, int frameColumns, int frameRows) {
    var characters = frameColumns * frameRows;
    var frameLength = characters * 10 + 2;
    var frames = matrixColumns * matrixRows;

    var body = new System.Collections.Generic.List<byte>(System.Text.Encoding.ASCII.GetBytes("CGFX"));

    var format = new byte[12];
    format[0] = (byte)matrixRows;
    format[1] = (byte)matrixColumns;
    format[4] = (byte)frames;
    format[8] = (byte)frameRows;
    format[9] = (byte)frameColumns;
    format[10] = 4;
    format[11] = 0;

    void Chunk(string kind, byte[] payload) {
      body.AddRange(System.Text.Encoding.ASCII.GetBytes(kind));
      body.AddRange([
        (byte)payload.Length, (byte)(payload.Length >> 8), (byte)(payload.Length >> 16), (byte)(payload.Length >> 24),
      ]);
      body.AddRange(payload);
    }

    Chunk("FRMT", format);
    Chunk("META", _Monochrome(17));
    Chunk("DATA", _Monochrome(frames * frameLength));

    var data = new byte[8 + body.Count];
    System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(data, 0);
    var length = body.Count;
    data[4] = (byte)length;
    data[5] = (byte)(length >> 8);
    data[6] = (byte)(length >> 16);
    data[7] = (byte)(length >> 24);
    body.CopyTo(data, 8);

    return data;
  }

  /// <summary>
  /// A 3201 picture. Its packing has three kinds of command and the probe uses all of them: a run
  /// of literals, a byte repeated, and a four-byte pattern repeated.
  /// </summary>
  private static byte[] _Apple3201() {
    var body = new System.Collections.Generic.List<byte>();
    var fill = 0;

    for (var written = 0; written < 32000;) {
      var left = 32000 - written;

      // A four-byte pattern, whose count is multiplied by four.
      if (left >= 64) {
        body.Add(128 + 15);
        for (var i = 0; i < 4; ++i)
          body.Add((byte)(fill++ * 37 + i));

        written += 64;
        continue;
      }

      if (left >= 16) {
        // One byte repeated, then a run of literals.
        body.AddRange([64 + 7, (byte)(fill++ * 53)]);
        written += 8;

        body.Add(7);
        for (var i = 0; i < 8; ++i)
          body.Add((byte)(fill++ * 29 + i));

        written += 8;
        continue;
      }

      body.Add((byte)(left - 1));
      for (var i = 0; i < left; ++i)
        body.Add((byte)(fill++ * 11 + i));

      written += left;
    }

    var data = new byte[6404 + body.Count];
    ReadOnlySpan<byte> signature = [193, 208, 208, 0];
    signature.CopyTo(data);

    for (var i = 4; i < 6404; ++i)
      data[i] = (byte)(i * 47 + (i >> 6));

    body.CopyTo(data, 6404);

    return data;
  }

  /// <summary>
  /// An Anime 4ever picture, encoded here. Its flags are packed two levels deep — eight command
  /// flags to a byte, and eight of those bytes governed by one byte of their own — so the probe has
  /// to interleave three streams in the order the decoder consumes them.
  /// </summary>
  private static byte[] _Anime4Ever() {
    // Each command is a flag and the bytes that follow it.
    var commands = new System.Collections.Generic.List<(int Flag, byte[] Bytes)>();

    var start = 19984 - 128 + 512;
    commands.Add((1, [0, (byte)start, (byte)(start >> 8), 0]));

    for (var written = 1; written < 10240;) {
      var left = 10240 - written;

      if (left >= 64 && (written & 15) == 0) {
        // A reference to the byte just written, repeated: distance one, count sixty-four.
        commands.Add((1, [1, 62]));
        written += 64;
        continue;
      }

      commands.Add((0, [(byte)(written * 29 + (written >> 6))]));
      ++written;
    }

    commands.Add((1, [1, 0]));

    var body = new System.Collections.Generic.List<byte>();

    for (var group = 0; group < commands.Count; group += 64) {
      // One outer byte governs eight inner ones; a set bit means the inner byte is present.
      var groups = Math.Min(8, (commands.Count - group + 7) / 8);
      var outer = 0;
      for (var i = 0; i < 8; ++i)
        outer = (outer << 1) | (i < groups ? 1 : 0);

      body.Add((byte)outer);

      for (var i = 0; i < groups; ++i) {
        var first = group + i * 8;
        var count = Math.Min(8, commands.Count - first);

        var inner = 0;
        for (var bit = 0; bit < 8; ++bit)
          inner = (inner << 1) | (bit < count ? commands[first + bit].Flag : 0);

        body.Add((byte)inner);

        for (var bit = 0; bit < count; ++bit)
          body.AddRange(commands[first + bit].Bytes);
      }
    }

    return body.ToArray();
  }

  /// <summary>
  /// A Boogie Down Paint picture in one of its three encodings. The oldest has no header at all —
  /// every byte is a command — so it is recognised by the other two failing to match.
  /// </summary>
  private static byte[] _Bdp(int form) {
    var body = new System.Collections.Generic.List<byte> { 0, 0 };
    var fill = 0;

    switch (form) {
      case 0:
        for (var written = 0; written < 10001;) {
          var left = 10001 - written;

          if (left >= 300) {
            body.AddRange([255, 0, (byte)(fill++ * 37)]);
            written += 256;
            continue;
          }

          var literals = Math.Min(left, 40);
          body.AddRange([254, (byte)literals, (byte)(literals >> 8)]);
          for (var i = 0; i < literals; ++i)
            body.Add((byte)(fill++ * 29 + i));

          written += literals;
        }

        return body.ToArray();

      case 1: {
        const byte escape = 0x9B;
        body.AddRange([2, 4, 16, 54, 48, 48, escape, 0]);
        _BdpEscaped(body, escape, -1);

        return body.ToArray();
      }

      default: {
        const byte shortEscape = 0x9B, longEscape = 0x9C;
        body.AddRange(System.Text.Encoding.ASCII.GetBytes("BDP 5.00"));
        body.Add(shortEscape);
        body.Add(longEscape);
        _BdpEscaped(body, shortEscape, longEscape);

        return body.ToArray();
      }
    }
  }

  /// <summary>
  /// Fills a Boogie Down Paint stream whose escapes the file names, writing any literal that
  /// happens to be an escape as a run of one.
  /// </summary>
  private static void _BdpEscaped(System.Collections.Generic.List<byte> body, byte shortEscape, int longEscape) {
    var fill = 0;

    for (var written = 0; written < 10001;) {
      var left = 10001 - written;

      if (longEscape >= 0 && left >= 400) {
        body.AddRange([(byte)longEscape, 0x90, 1, (byte)(fill++ * 37)]);
        written += 400;
        continue;
      }

      if (left >= 60) {
        body.AddRange([shortEscape, 50, (byte)(fill++ * 53)]);
        written += 50;
        continue;
      }

      var value = (byte)(fill++ * 29 + written);
      if (value == shortEscape || (longEscape >= 0 && value == longEscape))
        body.AddRange([shortEscape, 1, value]);
      else
        body.Add(value);

      ++written;
    }
  }

  /// <summary>
  /// A Hard Color Map picture. Its two arrangements differ in both the priority ranking and which
  /// sprite lands on the left, so each is a different picture from the same bytes.
  /// </summary>
  private static byte[] _Hcm(int arrangement) {
    var data = _Monochrome(8208);
    System.Text.Encoding.ASCII.GetBytes("HCMA8").CopyTo(data, 0);
    data[5] = 1;
    data[6] = (byte)arrangement;

    return data;
  }

  /// <summary>
  /// A GED picture. The timing decides where its six colour changes land across the scanline, and
  /// one priority bit decides whether the missiles follow their players or line up as a fifth
  /// playfield colour — so both are worth a probe.
  /// </summary>
  private static byte[] _Ged(int cycle, int priority) {
    var data = _Monochrome(11302);
    ReadOnlySpan<byte> signature = [255, 255, 48, 83, 79, 127];
    signature.CopyTo(data);

    data[3292] = (byte)priority;
    data[3300] = (byte)cycle;

    // The free register write per scanline must address something the chip has.
    for (var y = 0; y < 200; ++y)
      data[206 + y] = (byte)(y % 28);

    return data;
  }

  /// <summary>
  /// A PowerGraphics picture: a display list saying what ANTIC fetches for each of 240 scanlines,
  /// and a raster program per line saying which register to write and after how many cycles.
  /// </summary>
  private static byte[] _PowerGraphics(int dmaControl) {
    var length = 8192;
    var data = _Monochrome(length);

    data[0] = data[1] = 0xFF;
    data[2] = 6;
    data[3] = 130;
    var start = 6 | (130 << 8);
    var end = start + length - 6 - 1;
    data[4] = (byte)end;
    data[5] = (byte)(end >> 8);

    System.Text.Encoding.ASCII.GetBytes("PowerGFX").CopyTo(data, 8);
    data[774] = (byte)dmaControl;

    // The raster program sits after everything the header and tables occupy.
    var raster = 1600;
    data[6] = (byte)((33280 + raster) & 255);
    data[7] = (byte)((33280 + raster) >> 8);

    // A display list of 240 lines: each names a mode and, at the start of a block, an address.
    var at = 16;
    var screen = 33280 + 3000;
    for (var y = 0; y < 240; ++y) {
      if (y % 8 == 0) {
        data[at++] = 78;
        data[at++] = (byte)(screen & 255);
        data[at++] = (byte)(screen >> 8);
      } else
        data[at++] = 14;
    }

    // One raster program per scanline: a few register writes and then a terminator.
    var program = raster;
    for (var y = 0; y < 240; ++y) {
      for (var write = 0; write < 3; ++write) {
        data[program++] = (byte)(32 | (18 + write));
        data[program++] = (byte)(y * 7 + write * 40);
      }

      // The high bit ends the line's program.
      data[program++] = (byte)(128 | 32 | 26);
      data[program++] = (byte)(y * 3);
    }

    return data;
  }

  /// <summary>
  /// A Graph2Font MCH picture. Its length alone says how wide it is and whether it carries sprites,
  /// and one byte says which display mode — so the shapes it can take are combinations of the two.
  /// </summary>
  /// <param name="character">
  /// Which character mode. Two of the three also carry a raster program, and a file with sprites
  /// and a raster program is an animation rather than a picture — so the probes that carry sprites
  /// use the one mode that cannot have one.
  /// </param>
  private static byte[] _Mch(int length, int mode, int character = 2) {
    var data = _Monochrome(length);
    data[0] = (byte)(mode | character);

    return data;
  }

  /// <summary>
  /// A Graph2Font project. It stores every register the display uses for every scanline as plain
  /// tables, so most of the file is those tables and the picture is nowhere in it.
  /// </summary>
  private static byte[] _G2f(int columns, bool compressed) {
    var fonts = 1;
    var fontsOffset = 3 + 30 * columns;
    var fontNumberOffset = fontsOffset + fonts * 1024;
    var length = fontNumberOffset + 153724;

    var data = _Monochrome(length);
    data[0] = (byte)columns;
    data[1] = 0;
    data[2] = (byte)(fonts - 1);

    // Character arrangement 2 is the one that carries no raster program to check.
    data[fontNumberOffset + 147679] = 2;

    for (var row = 0; row < 30; ++row) {
      data[fontNumberOffset + row] = 0;
      // Alternate the display mode down the screen so more than one path is exercised.
      // The parentheses are not decoration: a switch expression binds tighter than the remainder,
      // so "row % 3 switch" would take the remainder against the switch's own result.
      data[fontNumberOffset + 153694 + row] = (byte)((row % 3) switch { 0 => 1, 1 => 2, _ => 4 });
    }

    // Every scanline names one of five priority arrangements and two sprite widths.
    for (var y = 0; y < 240; ++y) {
      var sprite = fontNumberOffset + 2334 + (y << 1);
      for (var i = 0; i < 4; ++i) {
        data[sprite + (i << 10) + 1] = (byte)((y % 5) << 4 | (i % 2 == 0 ? 1 : 2));
        data[sprite + 512 + (i << 10) + 1] = (byte)((y % 5) << 4 | 1);
      }

      data[sprite + 1025] = (byte)((y % 4) << 4);
    }

    if (!compressed)
      return data;

    using var packed = new MemoryStream();
    packed.Write(System.Text.Encoding.ASCII.GetBytes("G2FZLIB"));
    using (var deflate = new System.IO.Compression.ZLibStream(
             packed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
      deflate.Write(data);

    return packed.ToArray();
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
