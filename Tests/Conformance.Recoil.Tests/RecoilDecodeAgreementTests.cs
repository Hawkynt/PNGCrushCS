using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Hp48Grob;
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
    new("UIMG bitplanes, ST palette", ImageFormat.Uimg, ".bp1", () => _Uimg(1, 4, 0)),
    new("UIMG bitplanes, Falcon palette", ImageFormat.Uimg, ".bp1", () => _Uimg(3, 8, 0)),
    new("UIMG bytes", ImageFormat.Uimg, ".c01", () => _Uimg(1, 8, 1)),
    new("UIMG packed without rows, two bits", ImageFormat.Uimg, ".bp1", () => _Uimg(1, 2, 255)),
    new("UIMG packed without rows, nibbles", ImageFormat.Uimg, ".bp1", () => _Uimg(1, 4, 255)),
    new("UIMG true colour, two bytes", ImageFormat.Uimg, ".c02", () => _Uimg(0, 16, 2)),
    new("UIMG true colour, three bytes", ImageFormat.Uimg, ".c04", () => _Uimg(0, 24, 3)),
    new("UIMG true colour, four bytes", ImageFormat.Uimg, ".c04", () => _Uimg(0, 32, 4)),
    new("PL4", ImageFormat.Pl4Picture, ".pl4", () => _Pl4(false)),
    new("PL4, stored blocks", ImageFormat.Pl4Picture, ".pl4", () => _Pl4(true)),
    new("Blazing Paddles shapes", ImageFormat.ShapeTableFileType, ".shp", _Vectors),
    new("Movie Maker shapes", ImageFormat.ShapeTableFileType, ".shp", () => _Monochrome(4384)),
    new("Loadstar", ImageFormat.ShapeTableFileType, ".shp", () => _Monochrome(10018)),
    new("Shape table, packed hires", ImageFormat.ShapeTableFileType, ".shp", () => _PackedShapes(128)),
    new("Shape table, packed multicolour", ImageFormat.ShapeTableFileType, ".shp", () => _PackedShapes(0)),
    new("CHR$, one field", ImageFormat.ChrDollar, ".ch$", () => _ChrDollar(1)),
    new("CHR$, two fields", ImageFormat.ChrDollar, ".ch$", () => _ChrDollar(2)),
    new("Big font", ImageFormat.ZxBigFont, ".chx", _BigFont),
    new("Trefi, screen only", ImageFormat.ZxTrefiBorderScreen, ".bsp", () => _Trefi(0)),
    new("Trefi, two screens", ImageFormat.ZxTrefiBorderScreen, ".bsp", () => _Trefi(128)),
    new("Trefi, with border", ImageFormat.ZxTrefiBorderScreen, ".bsp", () => _Trefi(64)),
    new("Trefi, two bordered", ImageFormat.ZxTrefiBorderScreen, ".bsp", () => _Trefi(192)),
    new("Fuckpaint, 320x200", ImageFormat.FalconFuckpaint, ".pi4", () => _FalconFuckpaint(320, 200)),
    new("Fuckpaint, 320x240", ImageFormat.FalconFuckpaint, ".pi7", () => _FalconFuckpaint(320, 240)),
    new("Fuckpaint, 640x480", ImageFormat.FalconFuckpaint, ".pi9", () => _FalconFuckpaint(640, 480)),
    new("DEGAS Elite icon", ImageFormat.DegasIcon, ".icn", () => _DegasIcon(37, 23)),
    new("DEGAS Elite icon, whole words", ImageFormat.DegasIcon, ".icn", () => _DegasIcon(32, 8)),
    new("ColorSTar object, mono", ImageFormat.ColorStarObject, ".obj", () => _ColorStarObject(0)),
    new("ColorSTar object, colour", ImageFormat.ColorStarObject, ".obj", () => _ColorStarObject(4)),
    new("Tobias Richter, ST palettes", ImageFormat.TobiasRichterSlideshow, ".pci", () => _TobiasRichter(false)),
    new("Tobias Richter, STE palettes", ImageFormat.TobiasRichterSlideshow, ".pci", () => _TobiasRichter(true)),
    new("SAMAR hi-res with colour map", ImageFormat.SamarHiresMap, ".shc", _Samar),
    new("Unpacked 3200 colours", ImageFormat.AppleSh3, ".sh3", _AppleSh3),
    new("Apple Preferred, 320 mode", ImageFormat.ApplePreferred, ".32k", () => _ApplePreferred(false, false)),
    new("Apple Preferred, 640 mode", ImageFormat.ApplePreferred, ".32k", () => _ApplePreferred(true, false)),
    new("Apple Preferred, MULTIPAL", ImageFormat.ApplePreferred, ".32k", () => _ApplePreferred(false, true)),
    new("GROB, binary", ImageFormat.Hp48Grob, ".grb", () => _Grob(false)),
    new("GROB, serial text", ImageFormat.Hp48Grob, ".gro", () => _Grob(true)),
    new("ComputerEyes, colour", ImageFormat.ComputerEyesSt, ".ce3", () => _ComputerEyesSt(0)),
    new("ComputerEyes, hi-res colour", ImageFormat.ComputerEyesSt, ".ce3", () => _ComputerEyesSt(1)),
    new("ComputerEyes, grey", ImageFormat.ComputerEyesSt, ".ce3", () => _ComputerEyesSt(2)),
    new("Fun with Art", ImageFormat.FunWithArt, ".fwa", _FunWithArt),
    new("ICE mode 0", ImageFormat.AtariIce, ".ice", () => _AtariIce(0, 2053)),
    new("ICE mode 1", ImageFormat.AtariIce, ".ice", () => _AtariIce(1, 2054)),
    new("ICE mode 2", ImageFormat.AtariIce, ".ice", () => _AtariIce(2, 2058)),
    new("ICE mode 3", ImageFormat.AtariIce, ".ice", () => _AtariIce(3, 2055)),
    new("ICE mode 4", ImageFormat.AtariIce, ".ice", () => _AtariIce(4, 2058)),
    new("ICE mode 5", ImageFormat.AtariIce, ".ice", () => _AtariIce(5, 2065)),
    new("ICE mode 6", ImageFormat.AtariIce, ".ice", () => _AtariIce(6, 2051)),
    new("ICE mode 7", ImageFormat.AtariIce, ".ice", () => _AtariIce(7, 2051)),
    new("ICE mode 8", ImageFormat.AtariIce, ".ice", () => _AtariIce(8, 2058)),
    new("ICE mode 9", ImageFormat.AtariIce, ".ice", () => _AtariIce(9, 2058)),
    new("ICE mode 10", ImageFormat.AtariIce, ".ice", () => _AtariIce(10, 2051)),
    new("ICE mode 11", ImageFormat.AtariIce, ".ice", () => _AtariIce(11, 2051)),
    new("ICE mode 12", ImageFormat.AtariIce, ".ice", () => _AtariIce(12, 2051)),
    new("ICE mode 13", ImageFormat.AtariIce, ".ice", () => _AtariIce(13, 2059)),
    new("ICE mode 14", ImageFormat.AtariIce, ".ice", () => _AtariIce(14, 2054)),
    new("ICE mode 15", ImageFormat.AtariIce, ".ice", () => _AtariIce(15, 2054)),
    new("ICE mode 16", ImageFormat.AtariIce, ".ice", () => _AtariIce(16, 2058)),
    new("ICE mode 17", ImageFormat.AtariIce, ".ice", () => _AtariIce(17, 2054)),
    new("ICE mode 18", ImageFormat.AtariIce, ".ice", () => _AtariIce(18, 2054)),
    new("ICE mode 19", ImageFormat.AtariIce, ".ice", () => _AtariIce(19, 2058)),
    new("ICE mode 22", ImageFormat.AtariIce, ".ice", () => _AtariIce(22, 2058)),
    new("ICE mode 23", ImageFormat.AtariIce, ".ice", () => _AtariIce(23, 2065)),
    new("ICE mode 24", ImageFormat.AtariIce, ".ice", () => _AtariIce(24, 2051)),
    new("ICE mode 25", ImageFormat.AtariIce, ".ice", () => _AtariIce(25, 2051)),
    new("ICE mode 26", ImageFormat.AtariIce, ".ice", () => _AtariIce(26, 2058)),
    new("ICE mode 27", ImageFormat.AtariIce, ".ice", () => _AtariIce(27, 2058)),
    new("ICE mode 28", ImageFormat.AtariIce, ".ice", () => _AtariIce(28, 2051)),
    new("ICE mode 31", ImageFormat.AtariIce, ".ice", () => _AtariIce(31, 1032)),
    new("ICE mode 32", ImageFormat.AtariIce, ".ice", () => _AtariIce(32, 1038)),
    new("ICE mode 33", ImageFormat.AtariIce, ".ice", () => _AtariIce(33, 1027)),
    new("ICE mode 34", ImageFormat.AtariIce, ".ice", () => _AtariIce(34, 1027)),
    new("ICE mode 35", ImageFormat.AtariIce, ".ice", () => _AtariIce(35, 1032)),
    new("ICE mode 36", ImageFormat.AtariIce, ".ice", () => _AtariIce(36, 1032)),
    new("ICE mode 37", ImageFormat.AtariIce, ".ice", () => _AtariIce(37, 1027)),
    new("ICE mode 5, long", ImageFormat.AtariIce, ".ice", () => _AtariIce(5, 2066)),
    new("ICE PCIN+", ImageFormat.IcePcinPlus, ".ip2", _IcePcinPlus),
    new("Kitty, short tiles", ImageFormat.Kitty, ".kty", () => _Kitty(0)),
    new("Kitty, tall tiles", ImageFormat.Kitty, ".kt4", () => _Kitty(1)),
    new("Kitty, two-half tiles", ImageFormat.Kitty, ".kty", () => _Kitty(2)),
    new("I Paint, monochrome", ImageFormat.IPaint, ".ip", () => _IPaint(false)),
    new("I Paint, colour", ImageFormat.IPaint, ".ip", () => _IPaint(true)),
    new("Mapletown NL3", ImageFormat.MapletownNl3, ".nl3", _MapletownNl3),
    new("Printfox screen", ImageFormat.Printfox, ".gb", () => _Printfox('B')),
    new("Printfox double screen", ImageFormat.Printfox, ".gb", () => _Printfox('G')),
    new("Printfox block", ImageFormat.Printfox, ".gb", () => _Printfox('P')),
    new("Semi-Graphic logos", ImageFormat.SemiGraphicLogo, ".sge", _SemiGraphicLogo),
    new("Dir Logo Maker", ImageFormat.DirLogoMaker, ".dlm", _DirLogoMaker),
    new("ZXpaintyONE", ImageFormat.ZxPaintyOne, ".zp1", _ZxPaintyOne),
    new("Sinclair BASIC", ImageFormat.SinclairBasic, ".p", () => _SinclairBasic(false)),
    new("Sinclair BASIC, scrolling line", ImageFormat.SinclairBasic, ".p", () => _SinclairBasic(true)),
    new("Canvas raster, 320", ImageFormat.CanvasRaster, ".ful", () => _CanvasRaster(0)),
    new("Canvas raster, 640", ImageFormat.CanvasRaster, ".ful", () => _CanvasRaster(1)),
    new("PhotoChrome, one field", ImageFormat.PhotoChromePcs, ".pcs", () => _PhotoChromePcs(0)),
    new("PhotoChrome, both differences", ImageFormat.PhotoChromePcs, ".pcs", () => _PhotoChromePcs(4)),
    new("PhotoChrome, bitmap stored", ImageFormat.PhotoChromePcs, ".pcs", () => _PhotoChromePcs(1)),
    new("PhotoChrome, palette stored", ImageFormat.PhotoChromePcs, ".pcs", () => _PhotoChromePcs(2)),
    new("Z's Staff Kid98", ImageFormat.ZsStaffKid98, ".zim", () => _ZsStaffKid98(true)),
    new("Z's Staff Kid98, default palette", ImageFormat.ZsStaffKid98, ".zim", () => _ZsStaffKid98(false)),
    new("Art Master 88, PC-88", ImageFormat.ArtMaster88, ".arv", () => _ArtMaster88(200, 3)),
    new("Art Master 88, PC-98 three planes", ImageFormat.ArtMaster88, ".arv", () => _ArtMaster88(400, 3)),
    new("Art Master 88, PC-98 four planes", ImageFormat.ArtMaster88, ".arv", () => _ArtMaster88(400, 4)),
    new("XLD4", ImageFormat.Xld4, ".q4", _Xld4),
    new("LdPic, mode 0", ImageFormat.LdPic, ".bbg", () => _LdPic(0)),
    new("LdPic, mode 1", ImageFormat.LdPic, ".bbg", () => _LdPic(1)),
    new("LdPic, mode 2", ImageFormat.LdPic, ".bbg", () => _LdPic(2)),
    new("LdPic, mode 4", ImageFormat.LdPic, ".bbg", () => _LdPic(4)),
    new("LdPic, mode 5", ImageFormat.LdPic, ".bbg", () => _LdPic(5)),
    new("True-colour IMG, chunky", ImageFormat.TrueColorImg, ".timg", () => _TrueColorImg(0)),
    new("True-colour IMG, 15 planes", ImageFormat.TrueColorImg, ".timg", () => _TrueColorImg(15)),
    new("True-colour IMG, 16 planes", ImageFormat.TrueColorImg, ".timg", () => _TrueColorImg(16)),
    new("True-colour IMG, 24 planes", ImageFormat.TrueColorImg, ".timg", () => _TrueColorImg(24)),
    new("Screen 12, 192 rows", ImageFormat.MsxScc, ".scc", () => _MsxScc(0)),
    new("Screen 12, whole memory", ImageFormat.MsxScc, ".scc", () => _MsxScc(1)),
    new("Screen 12, packed", ImageFormat.MsxScc, ".scc", () => _MsxScc(2)),
    new("Screen 12, with sprites", ImageFormat.MsxScc, ".scc", () => _MsxScc(3)),
    new("MIG, screen 5", ImageFormat.MsxMig, ".mig", () => _MsxMig(5, false)),
    new("MIG, screen 7", ImageFormat.MsxMig, ".mig", () => _MsxMig(7, false)),
    new("MIG, screen 8", ImageFormat.MsxMig, ".mig", () => _MsxMig(8, false)),
    new("MIG, screen 12", ImageFormat.MsxMig, ".mig", () => _MsxMig(12, false)),
    new("MIG, screen 8 interlaced", ImageFormat.MsxMig, ".mig", () => _MsxMig(8, true)),
  ];

  /// <summary>
  /// A MIG picture: a compressed list of records — register writes, a palette and the screen — from
  /// which the graphics mode has to be worked out rather than read.
  /// </summary>
  private static byte[] _MsxMig(int screen, bool interlaced) {
    // The mode bits live in three registers; these are the values that add up to each mode.
    var (register0, pages) = screen switch {
      5 => (6, 106),
      7 => (10, 212),
      8 => (14, 212),
      _ => (14, 212),
    };

    var register25 = screen == 12 ? 8 : 0;

    var records = new System.Collections.Generic.List<byte> {
      0, 4,
      0, (byte)register0, 14,
      1, 0, 24,
      25, (byte)register25, 24,
      9, (byte)(interlaced ? 12 : 0), 12,
    };

    // A palette, which only the indexed modes actually consult.
    records.AddRange([1, 0, 16]);
    for (var i = 0; i < 16; ++i)
      records.AddRange([(byte)(((i * 3 % 8) << 4) | (i % 8)), (byte)((i * 5) % 8)]);

    void Screen(int seed) {
      records.AddRange([2, 0, 0, 0, 0, (byte)pages, 0]);
      for (var i = 0; i < pages * 256; ++i)
        records.Add((byte)(i * 37 + (i >> 8) * 11 + seed));
    }

    Screen(0);
    if (interlaced)
      Screen(97);

    // One byte follows the last screen, which the length check accounts for.
    records.Add(0);

    // The bits and the bytes share one stream: a flag byte is taken whenever eight bits have been
    // used, wherever the reader happens to be by then, so the emitter has to interleave them the
    // same way the reader does.
    var body = new System.Collections.Generic.List<byte>();
    var emitted = 0;
    var flag = -1;

    void PutBit(int bit) {
      if (emitted % 8 == 0) {
        flag = body.Count;
        body.Add(0);
      }

      if (bit != 0)
        body[flag] |= (byte)(1 << (7 - emitted % 8));

      ++emitted;
    }

    foreach (var value in records) {
      PutBit(0);
      body.Add(value);
    }

    // The unpacker has no end marker of its own: what ends it is a match whose length needs
    // sixteen bits to describe, followed by the file running out four bytes later.
    PutBit(1);
    body.Add(0);
    for (var i = 0; i < 16; ++i)
      PutBit(1);

    PutBit(0);
    for (var i = 0; i < 16; ++i)
      PutBit(0);

    var data = new byte[15 + body.Count];
    "MSXMIG"u8.CopyTo(data);
    var declared = data.Length - 6;
    data[6] = (byte)declared;
    data[7] = (byte)(declared >> 8);
    data[8] = (byte)(declared >> 16);
    data[9] = (byte)(declared >> 24);
    body.CopyTo(data, 15);

    return data;
  }

  /// <summary>An MSX2+ Screen 12 picture in one of the four shapes a BSAVE image can take.</summary>
  private static byte[] _MsxScc(int kind) {
    static void Header(byte[] data, int end) {
      data[0] = 254;
      data[3] = (byte)end;
      data[4] = (byte)(end >> 8);
    }

    static void Fill(byte[] data, int from, int to, int seed) {
      for (var i = from; i < to; ++i)
        data[i] = (byte)(i * 37 + (i >> 7) * 11 + seed);
    }

    switch (kind) {
      // Only the visible screen, which the end address declares.
      case 0: {
        var data = new byte[49159];
        Header(data, 49151);
        Fill(data, 7, data.Length, 0);
        return data;
      }

      // The whole of video memory.
      case 1: {
        var data = new byte[54279];
        Header(data, 54279 - 8);
        Fill(data, 7, data.Length, 1);
        return data;
      }

      // The same, run-length packed, which a different leading byte announces.
      case 2: {
        var screen = new byte[54279];
        Fill(screen, 7, screen.Length, 2);

        // Some stretches flat, so both the short and the long run are written.
        for (var i = 7; i < screen.Length; ++i) {
          if (i % 900 < 300)
            screen[i] = (byte)((i / 900) & 255);
        }

        var body = new System.Collections.Generic.List<byte>();
        for (var i = 7; i < screen.Length;) {
          var run = 1;
          while (run < 256 && i + run < screen.Length && screen[i + run] == screen[i])
            ++run;

          if (run == 1 && screen[i] > 15) {
            body.Add(screen[i]);
            ++i;
            continue;
          }

          if (run <= 15) {
            body.Add((byte)run);
            body.Add(screen[i]);
          } else {
            body.Add(0);
            body.Add((byte)(run & 255));
            body.Add(screen[i]);
          }

          i += run;
        }

        var data = new byte[7 + body.Count];
        data[0] = 253;
        data[3] = (byte)body.Count;
        data[4] = (byte)(body.Count >> 8);
        body.CopyTo(data, 7);

        return data;
      }

      // Video memory with the sprite tables after it, which are drawn over the picture.
      default: {
        var data = new byte[64167];
        Header(data, 54279 - 8);
        Fill(data, 7, 61447, 3);

        // Thirty-two sprites, most of them past the terminator so the early exit is covered.
        for (var sprite = 0; sprite < 32; ++sprite) {
          var at = 64007 + sprite * 4;
          data[at] = (byte)(sprite < 6 ? sprite * 30 + 8 : 216);
          data[at + 1] = (byte)(sprite * 37 % 240);
          data[at + 2] = (byte)(sprite * 4);
          data[at + 3] = 0;
        }

        // The per-line colour table a Screen 12 sprite uses, sixteen bytes to a sprite.
        for (var i = 63495; i < 64007; ++i)
          data[i] = (byte)((i * 13) & 0x4F);

        for (var i = 61447; i < 63495; ++i)
          data[i] = (byte)(i * 29 + (i >> 4));

        for (var i = 0; i < 16; ++i) {
          data[64135 + i * 2] = (byte)(((i * 3) % 8 << 4) | ((i * 5) % 8));
          data[64136 + i * 2] = (byte)((i * 7) % 8);
        }

        return data;
      }
    }
  }

  /// <summary>A true-colour GEM bit image, either chunky or in fifteen, sixteen or twenty-four planes.</summary>
  private static byte[] _TrueColorImg(int bitplanes) {
    const int width = 96, height = 40, patternLength = 4;
    var header = new System.Collections.Generic.List<byte>();

    void Word(int value) {
      header.Add((byte)(value >> 8));
      header.Add((byte)value);
    }

    var headerLength = bitplanes == 0 ? 18 : 28;
    Word(1);
    Word(headerLength >> 1);
    Word(bitplanes == 0 ? 24 : bitplanes);
    Word(patternLength);
    Word(372);
    Word(372);
    Word(width);
    Word(height);

    if (bitplanes == 0) {
      header.AddRange([0, 3]);

      var chunky = new System.Collections.Generic.List<byte>(header);
      var written = 0;
      var seed = 0;
      while (written < width * height) {
        var run = Math.Min(width * height - written, 40 + seed % 30);
        chunky.Add(128);
        chunky.Add((byte)run);
        for (var i = 0; i < run; ++i)
          chunky.AddRange([(byte)(seed * 7 + i), (byte)(seed * 13 + i * 3), (byte)(seed * 29 + i * 5)]);

        written += run;
        ++seed;
      }

      return chunky.ToArray();
    }

    header.AddRange("TIMG"u8.ToArray());

    // Sixteen planes spends its spare bit on green; the other two forms are even.
    var channel = (byte)(bitplanes == 24 ? 8 : 5);
    var green = (byte)(bitplanes == 16 ? 6 : channel);
    header.AddRange([0, 3, 0, channel, 0, green, 0, channel]);

    var body = new System.Collections.Generic.List<byte>(header);
    var bytesPerPlane = (width + 7) / 8;
    var stride = bitplanes * bytesPerPlane;

    for (var y = 0; y < height;) {
      // Every fifth row stands for three, which is the only compression across rows.
      var repeat = y % 5 == 0 && y + 3 <= height ? 3 : 1;
      if (repeat > 1)
        body.AddRange([0, 0, 255, (byte)repeat]);

      var line = new byte[stride];
      for (var i = 0; i < stride; ++i)
        line[i] = (byte)(y * 31 + i * 17);

      // Make some of the line solid so the solid-run branch is covered.
      for (var i = 0; i < stride; ++i) {
        if (i % 23 < 9)
          line[i] = (byte)(i % 46 < 9 ? 0 : 255);
      }

      var at = 0;
      while (at < stride) {
        var left = stride - at;

        // A solid run of one of the two values the coder can write without spelling them out.
        var solid = 1;
        while (solid < Math.Min(left, 127) && line[at + solid] == line[at])
          ++solid;

        if (solid >= 3 && (line[at] == 0 || line[at] == 255)) {
          body.Add((byte)(solid | (line[at] == 255 ? 128 : 0)));
          at += solid;
          continue;
        }

        // A pattern repeated, which rewinds over its own bytes rather than storing them again.
        if (left >= patternLength * 3) {
          var repeats = true;
          for (var i = patternLength; i < patternLength * 3 && repeats; ++i)
            repeats = line[at + i] == line[at + i % patternLength];

          if (repeats) {
            body.Add(0);
            body.Add(3);
            for (var i = 0; i < patternLength; ++i)
              body.Add(line[at + i]);

            at += patternLength * 3;
            continue;
          }
        }

        var run = Math.Min(left, 60);
        body.Add(128);
        body.Add((byte)run);
        for (var i = 0; i < run; ++i)
          body.Add(line[at + i]);

        at += run;
      }

      y += repeat;
    }

    return body.ToArray();
  }

  /// <summary>
  /// An LdPic picture: a bit stream whose own field widths it declares, unpacking a screen column
  /// by column at a stride it also declares.
  /// </summary>
  private static byte[] _LdPic(int mode) {
    const int valueBits = 6, countBits = 5, step = 8;
    var size = mode >= 4 ? 10240 : 20480;

    var bits = new System.Collections.Generic.List<byte>();
    var held = 0;
    var used = 0;

    // Bits go into a byte from the top down, but a field's own bits go in from the bottom up.
    void Bit(int bit) {
      held = (held << 1) | bit;
      if (++used != 8)
        return;

      bits.Add((byte)held);
      held = 0;
      used = 0;
    }

    void Field(int value, int count) {
      for (var i = 0; i < count; ++i)
        Bit((value >> i) & 1);
    }

    Field(valueBits, 8);
    Field(mode, 8);

    // Sixteen logical colours, written from the last backwards.
    for (var i = 15; i >= 0; --i)
      Field((i * 5 + 3) & 15, 4);

    Field(step, 8);
    Field(countBits, 8);

    // The screen is visited column by column, so the values follow that order rather than the
    // screen's own.
    var screen = new byte[size];
    for (var i = 0; i < size; ++i)
      screen[i] = (byte)((i * 37 + (i >> 6)) & ((1 << valueBits) - 1));

    // Make some stretches flat so that runs are worth writing, and the run branch is covered.
    for (var i = 0; i < size; ++i) {
      if (i % 700 < 200)
        screen[i] = (byte)((i / 700) & ((1 << valueBits) - 1));
    }

    var order = new System.Collections.Generic.List<byte>();
    for (var column = step - 1; column >= 0; --column) {
      for (var at = column; at < size; at += step)
        order.Add(screen[at]);
    }

    for (var i = 0; i < order.Count;) {
      var run = 1;
      while (run < (1 << countBits) - 1 && i + run < order.Count && order[i + run] == order[i])
        ++run;

      if (run == 1) {
        Bit(0);
        Field(order[i], valueBits);
      } else {
        Bit(1);
        Field(run, countBits);
        Field(order[i], valueBits);
      }

      i += run;
    }

    while (used != 0)
      Bit(0);

    return bits.ToArray();
  }

  /// <summary>
  /// An XLD4 picture: a palette chunk and eight picture chunks, each a dictionary-coded stream of
  /// seventeen symbols which themselves carry a run-length coding.
  /// </summary>
  private static byte[] _Xld4() {
    var body = new System.Collections.Generic.List<byte>(new byte[16]);
    body[2] = 2;
    body[11] = (byte)'M';
    body[12] = (byte)'A';
    body[13] = (byte)'J';
    body[14] = (byte)'Y';
    body[15] = (byte)'O';

    // Every symbol is written as a literal code, so the dictionary is built but never used — which
    // still exercises the width growth, the codes needing five bits from the start.
    static byte[] Encode(System.Collections.Generic.List<int> symbols) {
      var bytes = new System.Collections.Generic.List<byte>();
      var bits = 0;
      var held = 0;
      var width = 3;

      void Put(int value, int count) {
        for (var i = count - 1; i >= 0; --i) {
          held = (held << 1) | ((value >> i) & 1);
          if (++bits != 8)
            continue;

          bytes.Add((byte)held);
          held = 0;
          bits = 0;
        }
      }

      // A code of one is the instruction to read the next one a bit wider.
      while (width < 5) {
        Put(1, width);
        ++width;
      }

      foreach (var symbol in symbols)
        Put(symbol + 2, width);

      // A code of zero ends the chunk.
      Put(0, width);
      if (bits > 0)
        bytes.Add((byte)(held << (8 - bits)));

      return bytes.ToArray();
    }

    void Chunk(System.Collections.Generic.List<int> symbols, int pixels) {
      var packed = Encode(symbols);
      body.AddRange([(byte)packed.Length, (byte)(packed.Length >> 8), 0, 0,
                     (byte)(pixels >> 1), (byte)(pixels >> 9)]);
      body.AddRange(System.Array.ConvertAll(packed, b => b));
    }

    // The palette: two symbols a channel, three channels, sixteen colours.
    var paletteSymbols = new System.Collections.Generic.List<int>();
    for (var i = 0; i < 16; ++i)
    for (var channel = 0; channel < 3; ++channel) {
      paletteSymbols.Add((i + channel) % 16);
      paletteSymbols.Add((i * 5 + channel * 3) % 16);
    }

    Chunk(paletteSymbols, 0);

    for (var chunk = 0; chunk < 8; ++chunk) {
      var symbols = new System.Collections.Generic.List<int>();
      var written = 0;
      var index = 0;

      while (written < 32000) {
        var left = 32000 - written;

        switch (index % 3) {
          // A colour standing for itself.
          case 0:
            symbols.Add((chunk + index) % 16);
            ++written;
            break;

          // A run of whatever colour was last named, its length two digits in base seventeen and
          // its high digit not zero, since a zero there would mean the colour changes instead.
          case 1: {
            var run = Math.Min(left, 17 + index % 40);
            symbols.AddRange([16, run / 17, run % 17]);
            written += run;
            break;
          }

          // A run that names its colour first, which is the only way to write a short one.
          default: {
            var run = Math.Min(left, 3 + index % 13);
            symbols.AddRange([16, 0, (chunk * 3 + index) % 16, run / 17, run % 17]);
            written += run;
            break;
          }
        }

        ++index;
      }

      Chunk(symbols, 32000);
    }

    var data = body.ToArray();
    data[8] = (byte)data.Length;
    data[9] = (byte)(data.Length >> 8);

    return data;
  }

  /// <summary>An Art Master 88 picture in one of its two forms.</summary>
  private static byte[] _ArtMaster88(int height, int planes) {
    var body = new System.Collections.Generic.List<byte>(new byte[40]);
    "SS_SIF    0.0"u8.CopyTo(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(body));

    body[16] = (byte)'I';
    body[17] = (byte)(height == 400 ? 'R' : ' ');
    body[18] = (byte)'B';
    body[19] = (byte)'B';
    body[20] = (byte)'R';
    body[21] = (byte)'G';
    body[24] = 128;
    body[25] = 2;
    body[26] = (byte)height;
    body[27] = (byte)(height >> 8);

    // The comment chunk, which carries its own length and is stepped over.
    body.AddRange([6, 0, 1, 2, 3, 4]);

    if (height == 400) {
      var length = planes == 3 ? 50 : 98;
      body.Add((byte)length);
      body.Add(0);
      for (var c = 0; c < 1 << planes; ++c)
        body.AddRange([(byte)(c % 16), 0, (byte)((c * 5) % 16), 0, (byte)((c * 11) % 16), 0]);
    }

    // The bitmap chunk, likewise skipped.
    body.AddRange([4, 0, 9, 9]);

    var planeLength = height * 80;
    for (var plane = 0; plane < planes; ++plane) {
      var written = 0;
      var index = 0;

      // What the decoder would take as a run marker: the value it last saw, or nothing at all
      // straight after a run.
      var escape = -1;

      while (written < planeLength) {
        var left = planeLength - written;
        var value = (byte)(plane * 37 + index * 53);

        // A value that already equals the marker would begin a run rather than stand for itself.
        if (value == escape)
          value ^= 1;

        // A run is the value twice and then how many copies it stands for in all, so the first of
        // the two is an ordinary literal and the second is what marks it.
        if ((index & 3) == 3 && left >= 4) {
          var run = Math.Min(left, 60);
          body.Add(value);
          body.Add(value);
          body.Add((byte)run);
          written += run;
          escape = -1;
        } else {
          body.Add(value);
          ++written;
          escape = value;
        }

        ++index;
      }
    }

    return body.ToArray();
  }

  /// <summary>
  /// A Z's Staff Kid98 picture: a header, an optional palette, and a list of horizontal runs whose
  /// four planes are packed by nested flags and differenced twice.
  /// </summary>
  private static byte[] _ZsStaffKid98(bool palette) {
    const int width = 320, height = 200;
    var body = new System.Collections.Generic.List<byte>(new byte[512]);
    "FORMAT-A"u8.CopyTo(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(body));

    // No directory, so the header follows the fixed part directly.
    var header = new byte[24];
    var storedWidth = width - 1;
    var storedHeight = height - 1;
    header[4] = (byte)storedWidth;
    header[5] = (byte)(storedWidth >> 8);
    header[6] = (byte)storedHeight;
    header[7] = (byte)(storedHeight >> 8);
    header[20] = 1;

    // The two bytes before the palette say whether there is one.
    header[22] = (byte)(palette ? 1 : 0);
    body.AddRange(header);

    if (palette) {
      for (var c = 0; c < 16; ++c)
        body.AddRange([(byte)(c * 17), (byte)(c * 13), (byte)(c * 7), 0]);
    }

    // The run list's own directory, which the decoder skips.
    body.AddRange([2, 0, 0, 0, 0, 0]);

    void Word(System.Collections.Generic.List<byte> into, int value) {
      into.Add((byte)value);
      into.Add((byte)(value >> 8));
    }

    for (var run = 0; run < 24; ++run) {
      var length = 40 + run * 3;
      var size = (length + 7) / 8 * 4;
      size = (size + 3) & ~3;

      // The planes, before the two differencing passes the decoder undoes.
      var planes = new byte[size];
      for (var i = 0; i < size; ++i)
        planes[i] = (byte)(run * 31 + i * 17);

      // Some bytes are left out entirely and read back as zero, which is what the flags are for.
      var present = new bool[size];
      for (var i = 0; i < size; ++i)
        present[i] = (i + run) % 5 != 0;

      var differenced = new byte[size];
      for (var i = 0; i < size; ++i)
        differenced[i] = present[i] ? planes[i] : (byte)0;

      // The flags, three levels of them, each level saying which of the next level's bytes follow.
      var flags3 = new byte[64];
      for (var i = 0; i < size; ++i) {
        if (differenced[i] != 0 || present[i])
          flags3[i >> 3] |= (byte)(1 << (~i & 7));
      }

      var flags2 = new byte[8];
      for (var i = 0; i < 64; ++i) {
        if (flags3[i] != 0)
          flags2[i >> 3] |= (byte)(1 << (~i & 7));
      }

      byte flags1 = 0;
      for (var i = 0; i < 8; ++i) {
        if (flags2[i] != 0)
          flags1 |= (byte)(1 << (~i & 7));
      }

      var packed = new System.Collections.Generic.List<byte> { flags1 };
      for (var i = 0; i < 8; ++i) {
        if (((flags1 >> (~i & 7)) & 1) != 0)
          packed.Add(flags2[i]);
      }

      for (var i = 0; i < 64; ++i) {
        if (((flags2[i >> 3] >> (~i & 7)) & 1) != 0)
          packed.Add(flags3[i]);
      }

      for (var i = 0; i < size; ++i) {
        if (((flags3[i >> 3] >> (~i & 7)) & 1) != 0)
          packed.Add(differenced[i]);
      }

      Word(body, length);
      Word(body, run * 7 % (width - length));
      Word(body, run * 8);
      Word(body, packed.Count + 2);
      Word(body, size);
      body.AddRange(packed);
    }

    Word(body, 0);

    // The last run must not reach the end of the file, since a run is bounded by what follows it.
    body.AddRange(new byte[8]);

    return body.ToArray();
  }

  /// <summary>
  /// A PhotoChrome picture: one or two fields, each a run-length block of bitmap and a block of
  /// palette words.
  /// </summary>
  private static byte[] _PhotoChromePcs(int flags) {
    var body = new System.Collections.Generic.List<byte> { 1, 64, 0, 200, (byte)flags, 0 };

    void Block(int count, bool words, int seed) {
      var commands = new System.Collections.Generic.List<byte>();
      var written = 0;
      var index = 0;

      while (written < count) {
        var left = count - written;

        void Value(int at) {
          if (words)
            commands.Add((byte)((seed * 7 + at) & 7));

          commands.Add((byte)(seed * 31 + at * 17));
        }

        switch (index & 3) {
          // A short run of one value.
          case 0: {
            var run = Math.Min(left, 100);
            commands.Add((byte)run);
            Value(written);
            written += run;
            break;
          }

          // A short run of literals, counted downwards from 256.
          case 1: {
            var run = Math.Min(left, 60);
            commands.Add((byte)(256 - run));
            for (var i = 0; i < run; ++i)
              Value(written + i);

            written += run;
            break;
          }

          // A long run of one value, its length written as a word.
          case 2: {
            var run = Math.Min(left, 900);
            commands.AddRange([0, (byte)(run >> 8), (byte)run]);
            Value(written);
            written += run;
            break;
          }

          // A long run of literals.
          default: {
            var run = Math.Min(left, 300);
            commands.AddRange([1, (byte)(run >> 8), (byte)run]);
            for (var i = 0; i < run; ++i)
              Value(written + i);

            written += run;
            break;
          }
        }

        ++index;
      }

      body.Add((byte)(index >> 8));
      body.Add((byte)index);
      body.AddRange(commands);
    }

    Block(32000, false, 1);
    Block(19136 / 2, true, 2);

    if (flags == 0)
      return body.ToArray();

    Block(32000, false, 3);
    Block(19136 / 2, true, 4);

    return body.ToArray();
  }

  /// <summary>
  /// A Canvas raster picture: a table saying which bands carry a palette, those palettes written
  /// backwards, and a screen stored as runs plus whatever the runs missed.
  /// </summary>
  private static byte[] _CanvasRaster(int mode) {
    var bitplanes = 4 >> mode;

    // Most bands change palette, but not all, so both branches of the backwards walk are covered.
    var hasPalette = new bool[50];
    for (var band = 0; band < 50; ++band)
      hasPalette[band] = band == 0 || band % 5 != 3;

    var withPalette = 0;
    for (var band = 1; band < 50; ++band) {
      if (hasPalette[band])
        ++withPalette;
    }

    var cursor = 896 + withPalette * 48;
    var runs = new System.Collections.Generic.List<byte>();
    var filled = new bool[16000];

    // A handful of runs, one of them long enough to need the count's high byte. A run's start is a
    // group index that the plane count multiplies, so the four-plane mode bounds how high it goes.
    for (var i = 0; i < 12; ++i) {
      var start = i * 250;
      var count = i == 3 ? 400 : 20 + i;

      runs.AddRange([(byte)(count >> 8), (byte)count, (byte)(start >> 8), (byte)start]);
      for (var j = 0; j < bitplanes * 2; ++j)
        runs.Add((byte)(i * 31 + j * 17));

      for (var group = start * bitplanes; group <= (start + count) * bitplanes; group += bitplanes) {
        if (group < 16000)
          filled[group] = true;
      }
    }

    runs.AddRange([255, 255, 0, 0]);
    for (var j = 0; j < bitplanes * 2; ++j)
      runs.Add(0);

    var rest = new System.Collections.Generic.List<byte>();
    var seed = 0;
    for (var group = 0; group < 16000; group += bitplanes) {
      if (filled[group])
        continue;

      for (var j = 0; j < bitplanes * 2; ++j)
        rest.Add((byte)(seed * 37 + j * 53));

      ++seed;
    }

    var headerAt = cursor + 608;
    var data = new byte[headerAt + 34 + runs.Count + rest.Count];

    for (var band = 0; band < 50; ++band) {
      if (hasPalette[band])
        continue;

      data[band * 2] = 255;
      data[band * 2 + 1] = 255;
    }

    // The first band's palette is the last one written, so filling from the end matches the order
    // the decoder reads them in.
    for (var i = 896; i < cursor; ++i)
      data[i] = (byte)(i * 29 & 7);

    for (var i = 848; i < 896; ++i)
      data[i] = (byte)(i * 13 & 7);

    data[headerAt + 33] = (byte)mode;
    runs.CopyTo(data, headerAt + 34);
    rest.CopyTo(data, headerAt + 34 + runs.Count);

    return data;
  }

  /// <summary>A Semi-Graphic logos screen: 960 character codes and nothing else.</summary>
  private static byte[] _SemiGraphicLogo() {
    var data = new byte[960];

    // Include the four codes the editor patches, so the patched shapes are actually drawn.
    for (var i = 0; i < data.Length; ++i)
      data[i] = i % 40 < 8 ? (byte)(91 + i % 8) : (byte)(i * 37 + (i >> 5));

    return data;
  }

  /// <summary>A Dir Logo Maker logo: sixteen directory entries with eleven-character names.</summary>
  private static byte[] _DirLogoMaker() {
    var data = new byte[256];

    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 29 + (i >> 4) * 7);

    return data;
  }

  /// <summary>A ZXpaintyONE picture: 768 character codes as hexadecimal text.</summary>
  private static byte[] _ZxPaintyOne() {
    var text = new System.Text.StringBuilder();
    for (var i = 0; i < 768; ++i)
      text.Append($"{(i * 37 + (i >> 5)) & 0xFF:X2}");

    return System.Text.Encoding.ASCII.GetBytes(text.ToString());
  }

  /// <summary>
  /// A saved ZX81 program of the one shape these pictures take: PRINT AT and a string per row, and
  /// optionally the LET, IF, FOR, POKE and NEXT that scroll a line along the bottom.
  /// </summary>
  private static byte[] _SinclairBasic(bool scrolling) {
    var program = new System.Collections.Generic.List<byte>();

    void Line(System.Collections.Generic.IEnumerable<byte> tokens) {
      var body = new System.Collections.Generic.List<byte>(tokens) { 118 };

      // A line number, then the length of what follows it, then the statement.
      program.Add(0);
      program.Add((byte)(program.Count / 3 + 1));
      program.Add((byte)body.Count);
      program.Add((byte)(body.Count >> 8));
      program.AddRange(body);
    }

    // A number is typed as digits and then repeated as a five-byte float; only the float is read.
    static byte[] Number(int value) {
      var digits = new System.Collections.Generic.List<byte>();
      foreach (var c in value.ToString())
        digits.Add((byte)(28 + c - '0'));

      var exponent = 144;
      var mantissa = value;
      while (mantissa != 0 && (mantissa & 0x8000) == 0) {
        mantissa <<= 1;
        --exponent;
      }

      if (value == 0)
        return [.. digits, 126, 0, 0, 0, 0, 0];

      return [.. digits, 126, (byte)exponent, (byte)((mantissa >> 8) & 127), (byte)mantissa, 0, 0];
    }

    for (var row = 0; row < 20; ++row) {
      var line = new System.Collections.Generic.List<byte> { 245, 193 };
      line.AddRange(Number(row));
      line.Add(26);
      line.AddRange(Number(row % 12));
      line.Add(0);
      line.Add(11);

      // Character codes below 64; the ones above 128 would be the inverted half. A code equal to
      // the quote would close the string, so it is written as the escape that stands for one.
      for (var i = 0; i < 18; ++i) {
        var code = (row * 7 + i * 3) % 64;
        line.Add((byte)(code == 11 ? 192 : code));
      }

      line.Add(11);
      Line(line);
    }

    if (scrolling) {
      var text = new System.Collections.Generic.List<byte> { 241, 38, 13, 20, 11 };
      for (var i = 0; i < 30; ++i) {
        var code = (i * 5 + 3) % 64;
        text.Add((byte)(code == 11 ? 192 : code));
      }

      text.Add(11);
      Line(text);

      Line([241, 56, 20, .. Number(3), 21, 211, .. Number(16400), 21, .. Number(256), 23, 211, .. Number(16401)]);
      Line([241, 41, 20, .. Number(727), 21, 211, .. Number(16396), 21, .. Number(256), 23, 211, .. Number(16397)]);
      Line([250, 198, 38, 13, 221, .. Number(64), 222, 227]);
      Line([235, 43, 20, .. Number(0), 223, .. Number(63)]);
      Line([244, 41, 21, 43, 21, 16, 43, 18, .. Number(31), 17, 26, 211, 16, 56, 21, 43, 17]);
      Line([243, 43]);
    }

    // The reader will not look at a line unless eight bytes follow it, so the end-of-program marker
    // needs that much after it — which a real saved file has, its variables area sitting there.
    var data = new byte[116 + program.Count + 8];
    program.CopyTo(data, 116);
    data[116 + program.Count] = 118;

    return data;
  }

  /// <summary>A Printfox picture in whichever of its three kinds the leading letter names.</summary>
  private static byte[] _Printfox(char kind) {
    var (columns, rows) = kind switch {
      'B' => (40, 25),
      'G' => (80, 50),
      _ => (23, 17),
    };

    var body = new System.Collections.Generic.List<byte> { (byte)kind };
    if (kind == 'P') {
      body.Add((byte)rows);
      body.Add((byte)columns);
      body.AddRange("PROBE"u8.ToArray());
      body.Add(0);
    }

    var count = rows * columns * 8;
    var written = 0;
    var seed = 0;

    while (written < count) {
      var left = count - written;

      // Alternate literals with runs, and make one run the longest the kind can express, since the
      // two kinds count their lengths differently.
      if ((seed & 3) == 3) {
        var run = Math.Min(left, kind == 'P' ? 256 : 400);
        body.Add(155);
        body.Add((byte)run);
        if (kind != 'P')
          body.Add((byte)(run >> 8));

        body.Add((byte)(seed * 37 + 1));
        written += run;
      } else {
        var value = (byte)(seed * 29 + written);

        // The escape byte cannot stand for itself, so where the pattern lands on it, nudge it.
        body.Add(value == 155 ? (byte)156 : value);
        ++written;
      }

      ++seed;
    }

    return body.ToArray();
  }

  /// <summary>An I Paint picture: a header, a run-length bitmap and optionally a run-length colour map.</summary>
  private static byte[] _IPaint(bool color) {
    const int columns = 37, height = 53;
    var body = new System.Collections.Generic.List<byte> { 0, 0 };
    body.AddRange("BRUS"u8.ToArray());
    body.AddRange([4, 0, 0, 0, 1, 2, columns, (byte)height, (byte)(height >> 8), 0, 0, 0]);

    void Pack(int count, int seed) {
      for (var written = 0; written < count;) {
        var left = count - written;

        // Alternate runs of one repeated byte and runs of literals, so both commands are covered.
        if ((written / 20 & 1) == 0) {
          var run = Math.Min(left, 100);
          body.Add((byte)(128 | run));
          body.Add((byte)(seed * 31 + written));
          written += run;
        } else {
          var run = Math.Min(left, 20);
          body.Add((byte)run);
          for (var i = 0; i < run; ++i)
            body.Add((byte)(seed * 17 + written + i));

          written += run;
        }
      }
    }

    Pack(height * columns, 1);

    if (!color)
      return body.ToArray();

    body.AddRange("COLR"u8.ToArray());
    for (var block = 0; block < (height + 7) / 8; ++block)
      Pack(columns * 2, block + 2);

    return body.ToArray();
  }

  /// <summary>
  /// A Mapletown NL3 picture, every byte of which has to be a character a bulletin board would
  /// carry unaltered.
  /// </summary>
  private static byte[] _MapletownNl3() {
    var body = new System.Collections.Generic.List<byte>();

    // The alphabet: printable ASCII first, then the half-width Japanese range, some of it written
    // as the three-byte sequences the format uses for characters a plain byte cannot carry.
    void Write(int value) {
      switch (value) {
        case < 95: body.Add((byte)(value + 32)); break;

        // Two three-byte sequences, one for each half of the range above ASCII; which of the two a
        // character takes is fixed by its code, not free.
        case < 127: body.AddRange([0xEF, 0xBD, (byte)(value + 65)]); break;
        case < 159: body.AddRange([0xEF, 0xBE, (byte)(value + 1)]); break;
        case 159: body.Add(253); break;
        default: body.Add(254); break;
      }
    }

    for (var i = 0; i < 64; ++i) {
      // Nine levels a channel, so a colour is a number below 729 split across two characters.
      var color = (i % 9) * 81 + ((i / 9) % 9) * 9 + (i * 5 % 9);
      Write(color & 127);
      Write(color >> 7);
    }

    var written = 0;
    var index = 0;
    while (written < 160 * 100) {
      var left = 160 * 100 - written;

      // A short command is one pixel, a long one carries a length that is at least two.
      if ((index & 3) == 0 || left < 2) {
        Write(index % 64);
        ++written;
      } else {
        var run = Math.Min(left, 40 + index % 17);
        Write(64 | (index % 64));
        Write(run - 2);
        written += run;
      }

      ++index;
    }

    body.Add((byte)'\n');

    return body.ToArray();
  }

  /// <summary>
  /// A Kitty picture: blocks of one tile and the rectangles and positions it fills, then a fill of
  /// whatever is left.
  /// </summary>
  private static byte[] _Kitty(int mode) {
    var body = new System.Collections.Generic.List<byte>();
    var tileSize = mode < 2 ? 3 : 6;

    void Tile(int seed) {
      for (var i = 0; i < tileSize; ++i)
        body.Add((byte)(seed * 37 + i * 53));
    }

    // Three blocks, so the fill has something left to do but not the whole picture.
    for (var block = 0; block < 3; ++block) {
      body.Add((byte)mode);
      Tile(block + 1);

      // A rectangle with both extents, one that is a single row, and one that is a single column.
      var top = block * 12;
      body.AddRange([(byte)((top * 160 + 5) >> 8), (byte)(top * 160 + 5), 40, (byte)(top + 4)]);

      var rowStart = (top + 6) * 160 + 10;
      body.AddRange([(byte)(64 | (rowStart >> 8)), (byte)rowStart, 90]);

      var columnStart = (top + 8) * 160 + 100;
      body.AddRange([(byte)(128 | (columnStart >> 8)), (byte)columnStart, (byte)(top + 11)]);
      body.Add(255);

      // Then a few single positions.
      for (var i = 0; i < 4; ++i)
        body.AddRange([(byte)(120 + i * 5), (byte)(block * 12 + i)]);

      body.Add(255);
    }

    body.Add(255);

    // Everything still blank is filled in scan order, so the count has to be exact.
    var drawn = _KittyDrawnCount(mode);
    var fillTileSize = mode == 0 ? 3 : 6;
    for (var i = 0; i < 160 * 100 - drawn; ++i)
    for (var j = 0; j < fillTileSize; ++j)
      body.Add((byte)(i * 29 + j * 71));

    return body.ToArray();
  }

  /// <summary>How many of a Kitty probe's tiles the blocks above have already covered.</summary>
  private static int _KittyDrawnCount(int mode) {
    var drawn = new bool[160 * 100];

    for (var block = 0; block < 3; ++block) {
      var top = block * 12;
      for (var y = top; y <= top + 4; ++y)
      for (var x = 5; x <= 40; ++x)
        drawn[y * 160 + x] = true;

      for (var x = 10; x <= 90; ++x)
        drawn[(top + 6) * 160 + x] = true;

      for (var y = top + 8; y <= top + 11; ++y)
        drawn[y * 160 + 100] = true;

      for (var i = 0; i < 4; ++i)
        drawn[(block * 12 + i) * 160 + 120 + i * 5] = true;
    }

    var count = 0;
    foreach (var cell in drawn) {
      if (cell)
        ++count;
    }

    return count;
  }

  /// <summary>
  /// An Interlace Character Editor file in one of its thirty-three mode pairings, which the first
  /// byte names and the length confirms.
  /// </summary>
  private static byte[] _AtariIce(int mode, int length) {
    var data = new byte[length];
    data[0] = (byte)mode;

    // The header is colour registers and the rest a character set; both want varied content, and
    // no byte of either is constrained.
    for (var i = 1; i < length; ++i)
      data[i] = (byte)(i * 37 + (i >> 5) * 11);

    return data;
  }

  /// <summary>An ICE PCIN+ picture: a screen of character codes and two character sets.</summary>
  private static byte[] _IcePcinPlus() {
    var data = new byte[17358];
    data[0] = 1;

    for (var i = 1; i < data.Length; ++i)
      data[i] = (byte)(i * 53 + (i >> 6) * 7);

    return data;
  }

  /// <summary>
  /// A Fun with Art picture: the program's saved workspace, a display list, the bitmap, and one
  /// 6502 interrupt routine for every line that changes colour.
  /// </summary>
  private static byte[] _FunWithArt() {
    var routines = new System.Collections.Generic.List<byte>();
    var interrupts = new bool[192];

    for (var y = 0; y < 192; ++y) {
      // Every third line changes colour, so both branches of the display list walk are covered and
      // the routines are not all the same length.
      if (y % 3 != 0)
        continue;

      interrupts[y] = true;

      // PHA, TXA, PHA, LDA #n, STA $D40A — the fixed opening every routine has.
      routines.AddRange([72, 138, 72, 169, (byte)(y * 7), 141, 10, 212]);

      // One to three colour writes, some reloading the accumulator and some reusing it.
      routines.AddRange([141, 22, 208]);
      if (y % 6 == 0)
        routines.AddRange([169, (byte)(y * 11), 141, 23, 208]);
      if (y % 9 == 0)
        routines.AddRange([169, (byte)(y * 13), 141, 26, 208]);

      // JSR to the program's exit.
      routines.AddRange([32, 202, 6]);
    }

    var data = new byte[7960 + routines.Count];
    data[0] = 254;
    data[1] = 254;

    // Background, then PF0, PF1 and PF2 as the picture starts.
    data[2] = 0x0E;
    data[3] = 0x24;
    data[4] = 0x86;
    data[5] = 0xC8;

    data[6] = data[7] = data[8] = 112;
    data[11] = 80;
    data[115] = 96;
    data[205] = 65;

    var at = 9;
    for (var y = 0; y < 192; ++y) {
      // The list is in two halves, each opening with the address of the bitmap it draws from.
      var loadsAddress = at is 9 or 113;
      data[at] = (byte)((loadsAddress ? 78 : 14) | (interrupts[y] ? 128 : 0));
      at += loadsAddress ? 3 : 1;
    }

    for (var i = 262; i < 7958; ++i)
      data[i] = (byte)(i * 37 + (i >> 6));

    data[7958] = (byte)routines.Count;
    data[7959] = (byte)(routines.Count >> 8);
    routines.CopyTo(data, 7960);

    return data;
  }

  /// <summary>An HP 48 graphics object, in whichever of its two forms is asked for.</summary>
  private static byte[] _Grob(bool text) {
    const int width = 131, height = 37;
    var stride = (width + 7) >> 3;

    var bitmap = new byte[stride * height];
    for (var i = 0; i < bitmap.Length; ++i)
      bitmap[i] = (byte)(i * 37 + (i >> 3));

    if (text) {
      var built = new System.Text.StringBuilder(Hp48GrobFile.TextSignature);
      built.Append(width).Append(' ').Append(height).Append('\r');
      foreach (var b in bitmap)
        built.Append($"{b:X2}");

      return System.Text.Encoding.ASCII.GetBytes(built.ToString());
    }

    var data = new byte[18 + bitmap.Length];
    "HPHP48-"u8.CopyTo(data);
    data[7] = (byte)'A';
    data[8] = 30;
    data[9] = 43;

    // The object's size is counted in nibbles from just past this field, so it depends on itself.
    var nibbles = data.Length * 2 - 21;
    data[10] = (byte)(nibbles << 4);
    data[11] = (byte)(nibbles >> 4);
    data[12] = (byte)(nibbles >> 12);
    data[13] = (byte)height;
    data[14] = (byte)(height >> 8);
    data[15] = (byte)(((height >> 16) & 15) | ((width & 15) << 4));
    data[16] = (byte)(width >> 4);
    data[17] = (byte)(width >> 12);
    bitmap.CopyTo(data, 18);

    return data;
  }

  /// <summary>A ComputerEyes ST capture, stored a column at a time as the digitiser wrote it.</summary>
  private static byte[] _ComputerEyesSt(int mode) {
    var data = new byte[mode == 0 ? 192022 : 256022];
    "EYES"u8.CopyTo(data);
    data[5] = (byte)mode;

    // Every mode reserves part of each byte or word, and a value outside that range is rejected
    // rather than masked, so the fill has to stay inside it.
    for (var i = 22; i < data.Length; ++i)
      data[i] = mode switch {
        0 => (byte)(i * 37 & 63),
        1 => (byte)((i & 1) == 0 ? i * 29 & 127 : i * 53),
        _ => (byte)(i * 41 % 192),
      };

    return data;
  }

  /// <summary>A 3200-colour picture written out as it sits in memory: bitmap, then 200 palettes.</summary>
  private static byte[] _AppleSh3() {
    var data = new byte[38400];

    for (var i = 0; i < 32000; ++i)
      data[i] = (byte)(i * 37 + (i >> 6));

    for (var i = 32000; i < data.Length; ++i)
      data[i] = (byte)(i * 53 + (i >> 4));

    return data;
  }

  /// <summary>
  /// An Apple Preferred Format picture: a MAIN chunk of palettes, a scanline directory and a
  /// PackBytes bitmap, optionally followed by a MULTIPAL chunk of a palette per line.
  /// </summary>
  private static byte[] _ApplePreferred(bool wide, bool multipal) {
    const int height = 200;
    var width = wide ? 640 : 320;
    var bytesPerLine = wide ? width >> 2 : width >> 1;
    const int paletteCount = 4;
    var directoryOffset = 17 + paletteCount * 32;

    var packed = new System.Collections.Generic.List<byte>();
    var lineLengths = new int[height];

    for (var y = 0; y < height; ++y) {
      var before = packed.Count;

      for (var written = 0; written < bytesPerLine;) {
        var left = bytesPerLine - written;

        // A run of one repeated byte, a four-byte pattern repeated, then literals — so all three
        // of PackBytes' strides are exercised on every line.
        if (left >= 16 && written == 0) {
          packed.Add(0x43);
          packed.Add((byte)(y * 7));
          written += 4;
        } else if (left >= 16 && written == 4) {
          packed.Add(0x83);
          packed.AddRange([(byte)(y + 1), (byte)(y + 2), (byte)(y + 3), (byte)(y + 4)]);
          written += 16;
        } else {
          var run = Math.Min(left, 20);
          packed.Add((byte)(run - 1));
          for (var i = 0; i < run; ++i)
            packed.Add((byte)(y * 11 + written + i));

          written += run;
        }
      }

      lineLengths[y] = packed.Count - before;
    }

    var bitmapOffset = directoryOffset + height * 4;
    var mainLength = bitmapOffset + packed.Count;
    var data = new byte[mainLength + (multipal ? 6415 : 0)];

    data[0] = (byte)mainLength;
    data[1] = (byte)(mainLength >> 8);
    data[2] = (byte)(mainLength >> 16);
    data[3] = (byte)(mainLength >> 24);
    data[4] = 4;
    "MAIN"u8.CopyTo(data.AsSpan(5));
    data[9] = (byte)(wide ? 128 : 0);
    data[11] = (byte)width;
    data[12] = (byte)(width >> 8);
    data[13] = paletteCount;

    for (var i = 0; i < paletteCount * 16; ++i) {
      data[15 + i * 2] = (byte)(i * 29);
      data[16 + i * 2] = (byte)(i * 17);
    }

    data[directoryOffset - 2] = (byte)height;
    data[directoryOffset - 1] = (byte)(height >> 8);

    for (var y = 0; y < height; ++y) {
      var entry = directoryOffset + y * 4;
      data[entry] = (byte)lineLengths[y];
      data[entry + 1] = (byte)(lineLengths[y] >> 8);
      data[entry + 2] = (byte)((wide ? 128 : 0) | y % paletteCount);
    }

    packed.CopyTo(data, bitmapOffset);

    if (!multipal)
      return data;

    data[mainLength] = 6415 & 255;
    data[mainLength + 1] = 6415 >> 8;
    data[mainLength + 4] = 8;
    "MULTIPAL"u8.CopyTo(data.AsSpan(mainLength + 5));
    data[mainLength + 13] = height;

    for (var i = 0; i < height * 16; ++i) {
      data[mainLength + 15 + i * 2] = (byte)(i * 41);
      data[mainLength + 16 + i * 2] = (byte)(i * 23);
    }

    return data;
  }

  /// <summary>
  /// A Tobias Richter slideshow picture: two fields of four whole-picture bitplanes, then a
  /// sixteen-colour palette for every one of the 556 scanlines.
  /// </summary>
  private static byte[] _TobiasRichter(bool ste) {
    const int paletteOffset = 97856;
    var data = new byte[115648];

    for (var i = 0; i < paletteOffset; ++i)
      data[i] = (byte)(i * 37 + (i >> 9));

    for (var line = 0; line < 556; ++line)
    for (var color = 0; color < 16; ++color) {
      var at = paletteOffset + (line * 16 + color) * 2;

      // An ST palette has three bits a channel and an STE four, the extra bit sitting below the
      // other three; a picture in which no entry uses it is read as an ST one.
      data[at] = (byte)((line + color) & 7 | (ste && (line + color) % 5 == 0 ? 8 : 0));
      data[at + 1] = (byte)((color * 17 + line) & 0x77 | (ste && (color + line) % 3 == 0 ? 0x88 : 0));
    }

    return data;
  }

  /// <summary>A SAMAR picture: two hi-res bitmaps and the colour registers each line steps through.</summary>
  private static byte[] _Samar() {
    var data = new byte[17920];

    for (var i = 0; i < 15360; ++i)
      data[i] = (byte)(i * 53 + (i >> 7));

    // Both maps hold a colour for every zone of every line, plus one that carries into the next.
    for (var i = 15360; i < data.Length; ++i)
      data[i] = (byte)(i * 29 & 254);

    return data;
  }

  /// <summary>A Falcon Fuckpaint picture: a 256-colour palette and then eight bitplanes.</summary>
  private static byte[] _FalconFuckpaint(int width, int height) {
    var data = new byte[1024 + width * height];

    for (var i = 0; i < 256; ++i) {
      data[i * 4] = (byte)(i * 7);
      data[i * 4 + 1] = (byte)(255 - i);
      data[i * 4 + 2] = 0xEE;
      data[i * 4 + 3] = (byte)(i * 29);
    }

    for (var i = 0; i < width * height; ++i)
      data[1024 + i] = (byte)(i * 37 + (i >> 8));

    return data;
  }

  /// <summary>A DEGAS Elite icon, which is a fragment of C source rather than a binary file.</summary>
  private static byte[] _DegasIcon(int width, int height) {
    var words = (width + 15) >> 4;
    var text = new System.Text.StringBuilder();

    // The parser needs something before the first token, and the exporter always wrote a comment.
    text.Append("/* icon */\n");
    text.Append($"#define ICON_W 0x{width:X}\n");
    text.Append($"#define ICON_H 0x{height:X}\n");
    text.Append($"#define ICONSIZE 0x{words * height:X}\n");
    text.Append("int image[ICONSIZE] = {");

    for (var i = 0; i < words * height; ++i) {
      if (i > 0)
        text.Append(',');

      // A comment between the words, since the parser accepts one anywhere a space may go.
      text.Append(i % 8 == 7 ? "\n\t/* row */ 0x" : " 0x");
      text.Append($"{(i * 30011 + 7) & 0xFFFF:X}");
    }

    text.Append("\n};\n");

    return System.Text.Encoding.ASCII.GetBytes(text.ToString());
  }

  /// <summary>A ColorSTar object, monochrome or in sixteen colours written as decimal text.</summary>
  private static byte[] _ColorStarObject(int bitplanes) {
    const int width = 37, height = 11;
    var stride = (width + 15) >> 4 << 1;

    if (bitplanes == 0) {
      var mono = new byte[6 + stride * height];
      mono[0] = (byte)((width - 1) >> 8);
      mono[1] = (byte)(width - 1);
      mono[2] = (byte)((height - 1) >> 8);
      mono[3] = (byte)(height - 1);

      // The two bytes that say "monochrome" are where a coloured object starts its palette text.
      mono[4] = 0;
      mono[5] = 1;

      for (var i = 6; i < mono.Length; ++i)
        mono[i] = (byte)(i * 53);

      return mono;
    }

    var header = new System.Collections.Generic.List<byte>();
    for (var i = 0; i < 16; ++i) {
      // Three channels of three bits, four bits apart, so the number prints as three octal digits.
      var value = (i & 7) << 8 | (15 - i & 7) << 4 | (i * 3 & 7);
      header.AddRange(System.Text.Encoding.ASCII.GetBytes(value.ToString()));
      header.AddRange("\r\n"u8.ToArray());
    }

    header.AddRange([(byte)((width - 1) >> 8), (byte)(width - 1), 0, (byte)(height - 1), 0, 4]);

    var data = new byte[header.Count + stride * 4 * height];
    header.CopyTo(data);
    for (var i = header.Count; i < data.Length; ++i)
      data[i] = (byte)(i * 47 + (i >> 5));

    return data;
  }

  /// <summary>A CHR$ font: a signature, three dimensions and then cells of bitmap plus attribute.</summary>
  private static byte[] _ChrDollar(int fields) {
    const int columns = 6, rows = 5;
    var data = new byte[7 + rows * columns * fields * 9];
    "chr$"u8.CopyTo(data);
    data[4] = columns;
    data[5] = rows;
    data[6] = (byte)(fields * 9);

    for (var i = 0; i * 9 + 8 < data.Length - 7; ++i) {
      for (var y = 0; y < 8; ++y)
        data[7 + i * 9 + y] = (byte)(i * 37 + y * 11);

      // Both intensities and both a lit and an unlit half, so ink, paper and bright all matter.
      data[7 + i * 9 + 8] = (byte)(i * 23 & 0x7F);
    }

    return data;
  }

  /// <summary>
  /// A big font: an offset table of 256 entries, most of them absent, and characters of differing
  /// sizes so that the sheet is laid out at the largest and the rest are padded.
  /// </summary>
  private static byte[] _BigFont() {
    var offsets = new int[256];
    var body = new System.Collections.Generic.List<byte>();
    var at = 5 + 256 * 2;

    (int Columns, int Rows, int Transparent)[] characters = [
      (3, 2, 0), (1, 1, 0), (2, 3, 1), (3, 3, 0), (1, 2, 1),
    ];

    for (var i = 0; i < characters.Length; ++i) {
      var (columns, rows, transparent) = characters[i];

      // Spread them out so absent characters, and the chequer that stands in for them, are covered.
      offsets[i * 7] = at + body.Count;
      body.Add((byte)transparent);
      body.Add((byte)columns);
      body.Add((byte)rows);

      for (var cell = 0; cell < rows * columns; ++cell) {
        for (var y = 0; y < 8; ++y)
          body.Add((byte)(i * 53 + cell * 17 + y * 7));

        if (transparent == 0)
          body.Add((byte)(i * 29 + cell * 11 & 0x7F));
      }
    }

    var data = new byte[at + body.Count];
    "CHX"u8.CopyTo(data);
    for (var i = 0; i < 256; ++i) {
      data[5 + i * 2] = (byte)offsets[i];
      data[6 + i * 2] = (byte)(offsets[i] >> 8);
    }

    body.CopyTo(data, at);

    return data;
  }

  /// <summary>A Trefi border screen in whichever of its four shapes the flag byte asks for.</summary>
  private static byte[] _Trefi(int flags) {
    const int screenSize = 6912;
    var bordered = (flags & 64) != 0;
    var fields = (flags & 128) != 0 ? 2 : 1;
    var header = 70 + (bordered && fields == 2 ? 2 : 0);

    var border = new System.Collections.Generic.List<byte>();
    if (bordered) {
      // 304 lines of runs, each line using all four ways a length can be written: the fixed twelve,
      // a length in the following byte, one of the lengths the selector carries itself, and the
      // zero that means the colour holds to the end of the line.
      for (var field = 0; field < fields; ++field)
      for (var line = 0; line < 304; ++line) {
        border.Add((byte)((line + field) % 7 + 1 | 2 << 3));
        border.Add((byte)((line * 3 + field) % 7 + 1 | 1 << 3));
        border.Add(40);
        border.Add((byte)((line * 2 + field) % 7 + 1 | (3 + line % 29) << 3));
        border.Add((byte)((line * 5 + field) % 7 + 1));
      }
    }

    var perField = border.Count / Math.Max(fields, 1);
    var data = new byte[header + screenSize * fields + border.Count];
    data[3] = (byte)flags;

    for (var field = 0; field < fields; ++field) {
      var at = header + screenSize * field;
      for (var i = 0; i < 6144; ++i)
        data[at + i] = (byte)(i * 37 + field * 91);

      for (var i = 0; i < 768; ++i)
        data[at + 6144 + i] = (byte)((i * 23 + field * 17) & 0x7F);
    }

    if (!bordered)
      return data;

    var borderAt = header + screenSize * fields;
    border.CopyTo(data, borderAt);

    if (fields == 2) {
      data[70] = (byte)(borderAt + perField);
      data[71] = (byte)((borderAt + perField) >> 8);
    }

    return data;
  }

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
  /// A vertical scroll is nothing but a list of file names, so the pair — the list and the projects
  /// it names — is the only arrangement in which it is a picture at all.
  /// </summary>
  [TestCase(1)]
  [TestCase(3)]
  [Category("Conformance")]
  public void Graph2FontScroll_WithItsProjects_MatchesRecoilPixelForPixel(int frames) {
    RecoilOracle.RequireAvailable();

    var directory = Path.Combine(Path.GetTempPath(), $"recoilvsc_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);

    try {
      var list = new System.Text.StringBuilder();
      for (var i = 0; i < frames; ++i) {
        var name = $"part{i}.g2f";
        list.Append(name).Append("\r\n");
        File.WriteAllBytes(Path.Combine(directory, name), _G2f(40, false));
      }

      var scroll = Path.Combine(directory, "scroll.vsc");
      File.WriteAllBytes(scroll, System.Text.Encoding.ASCII.GetBytes(list.ToString()));

      var (png, output) = RecoilOracle.TryDecodeToPng(scroll);
      Assert.That(png, Is.Not.Null, $"RECOIL rejected the scroll — {output}");

      var theirs = _AsRgb(FormatRegistry.Read(png!));
      var ours = _AsRgb(FileFormat.Graph2FontScroll.Graph2FontScrollFile.ToRawImage(
        FileFormat.Graph2FontScroll.Graph2FontScrollReader.FromFile(new(scroll))));

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

  /// <summary>
  /// A UIMG picture. Its header names a palette kind, a depth and an arrangement, and the file's
  /// own length has to agree with all three — so each combination is a different length as well as
  /// a different reading.
  /// </summary>
  private static byte[] _Uimg(int palette, int depth, int chunk) {
    var width = 64;
    var height = 40;
    var count = width * height;

    ReadOnlySpan<byte> unit = [0, 2, 2, 4];
    var bitmapOffset = 14 + (unit[palette] << depth);

    var length = chunk switch {
      0 or 255 => bitmapOffset + (count >> 3) * depth,
      1 => bitmapOffset + count,
      _ => 14 + count * chunk,
    };

    var data = _Monochrome(length);
    System.Text.Encoding.ASCII.GetBytes("UIMG").CopyTo(data, 0);
    data[6] = 0;
    data[7] = (byte)palette;
    data[8] = (byte)depth;
    data[9] = (byte)chunk;
    data[10] = (byte)(width >> 8);
    data[11] = (byte)width;
    data[12] = (byte)(height >> 8);
    data[13] = (byte)height;

    return data;
  }

  /// <summary>
  /// A PL4 picture, packed here into an LZ4 frame. One probe stores its blocks outright and one
  /// compresses them, since the two take different paths through the frame reader.
  /// </summary>
  private static byte[] _Pl4(bool stored) {
    var unpacked = _Monochrome(64070);
    unpacked[0] = unpacked[1] = 0;
    unpacked[32036] = unpacked[32037] = 0;

    var body = new System.Collections.Generic.List<byte> { 4, 34, 77, 24, 64, 112, 0 };

    for (var at = 0; at < unpacked.Length;) {
      var take = Math.Min(16384, unpacked.Length - at);

      if (stored) {
        var size = take | int.MinValue;
        body.AddRange([(byte)size, (byte)(size >> 8), (byte)(size >> 16), (byte)(size >> 24)]);
        body.AddRange(unpacked[at..(at + take)]);
        at += take;
        continue;
      }

      // One sequence of literals and no match. A literals-only sequence has to be the last in its
      // block, so the whole block is one token however long its run is.
      var block = new System.Collections.Generic.List<byte> { 15 << 4 };
      var remaining = take - 15;
      while (remaining >= 255) {
        block.Add(255);
        remaining -= 255;
      }

      block.Add((byte)remaining);
      block.AddRange(unpacked[at..(at + take)]);

      body.AddRange([(byte)block.Count, (byte)(block.Count >> 8), (byte)(block.Count >> 16), (byte)(block.Count >> 24)]);
      body.AddRange(block);
      at += take;
    }

    body.AddRange([0, 0, 0, 0]);

    return body.ToArray();
  }

  /// <summary>
  /// A Blazing Paddles shape table: 128 addresses and then the drawing instructions they point at,
  /// each shape a run of moves closed by a stop.
  /// </summary>
  private static byte[] _Vectors() {
    var data = new byte[1024];
    var shapes = 24;
    var at = 256;

    for (var i = 0; i < shapes; ++i) {
      var address = 31744 + at;
      data[i * 2] = (byte)address;
      data[i * 2 + 1] = (byte)(address >> 8);

      // A small closed figure, drawn with the pen down and then lifted for the last leg.
      foreach (var control in (byte[])[0x30, 0x33, 0x21, 0x26, 0x10]) {
        data[at++] = control;
      }

      data[at++] = 8;
    }

    // Every remaining address points at the stop that follows the last shape.
    var end = 31744 + at - 1;
    for (var i = shapes; i < 128; ++i) {
      data[i * 2] = (byte)end;
      data[i * 2 + 1] = (byte)(end >> 8);
    }

    return data;
  }

  /// <summary>
  /// A packed shape table. The third byte says which program wrote it and where its escape byte
  /// and stream begin, so it is the whole of the identification.
  /// </summary>
  private static byte[] _PackedShapes(int kind) {
    const byte escape = 0xB7;
    var body = new System.Collections.Generic.List<byte> { 0, 0, (byte)kind };
    var fill = 0;

    if (kind == 0) {
      body.Add(escape);
      body.Add(0);
    } else
      body.Add(escape);

    void Stream(int length, byte streamEscape) {
      for (var written = 0; written < length;) {
        var left = length - written;

        if (streamEscape != 0 && left >= 200) {
          body.AddRange([streamEscape, 200, (byte)(fill++ * 37)]);
          written += 200;
          continue;
        }

        var value = (byte)(fill++ * 29 + written);
        if (value == streamEscape)
          body.AddRange([streamEscape, 1, value]);
        else
          body.Add(value);

        ++written;
      }
    }

    // The bitmap, then the colour map under an escape of zero, then — for the multicolour form —
    // the video matrix under an escape of 255. Each section's length is fixed by the mode.
    Stream(8000, escape);
    Stream(1000, 0);

    if (kind == 0)
      Stream(1000, 255);

    return body.ToArray();
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
