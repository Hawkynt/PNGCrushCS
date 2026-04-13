namespace Optimizer.Image;

/// <summary>Supported image formats for optimization and conversion.</summary>
public enum ImageFormat {
  Unknown,
  // With dedicated optimizers
  Png, Gif, Tiff, Bmp, Tga, Pcx, Jpeg, Ico, Cur, Ani, WebP,
  // Lossless raster
  Qoi, Farbfeld, Sgi, SunRaster, Netpbm, Wbmp, Xbm, Xpm,
  Hrz, Cmu, Mtv, Qrt, Otb, Avs, Msp, Bam,
  // Complex raster
  Hdr, Pfm, Psd, Xcf, Dds, Vtf, Ktx, Exr, Dpx, Miff,
  AliasPix, Rla, ScitexCt, Viff, Xwd, Dicom, Cineon,
  // Indexed/planar/retro
  MacPaint, DrHalo, Palm, Sixel, Ccitt, Cals, Sff, Oric, Vicar,
  Koala, ZxSpectrum, BbcMicro, AmstradCpc, C64Multi, Bsave,
  Degas, Neochrome, CrackArt, Spectrum512, Tiny, GemImg,
  AppleII, AppleIIgs, Msx, SamCoupe, Acorn,
  // Planar/container
  Ilbm, Pict, Fli, OpenRaster, Jng, Nifti, Nrrd, Fits,
  // Container/multi-image
  Apng, Mng, Dcx, Wad, Wad3, Art, Tim, Tim2, Wal, Wpg, Pkm, Astc, Pvr,
  // Utah
  UtahRle,
  // Clipboard
  Clp,
  // Wave 4: Trivial formats
  Aai, Rgf, Fbm, Gbr, Pat, Xyz, Lss16, ColoRix,
  SunIcon, Cel, AmigaIcon, Gaf, GunPaint, GeoPaint,
  // Wave 5: Extensions & containers
  Psb, Icns, Blp, Fsh, Mpo, Pds, Ics, BioRadPic, Ptif,
  // Wave 6: Medium complexity
  Bsb, Awd, Psp, Qtif, Ingr, Nitf, Uhdr, PalmPdb, Pcd, PhotoPaint, Pdn, Fpx,
  // Wave 7: Complex codecs
  JpegLs, Jbig, Wsq, DjVu, Jbig2, Flif, Jpeg2000, JpegXr,
  // Wave 8: Advanced codecs
  Heif, Avif, JpegXl, Bpg, Dng, CameraRaw,
  // Wave 9: Additional formats
  Krita, Analyze, MetaImage, Eps, Wmf, Emf, Vips, QuakeSpr, NesChr, GameBoyTile, Atari8Bit, IffAnim,
  // Wave 10: IFF variants, professional 3D, scientific, retro
  SoftImage, MayaIff, Envi, Xcursor, IffPbm, PcPaint, IffAcbm, IffDeep, IffRgb8, Interfile, AtariFalcon, Trs80,
  // Wave 11: Console tiles, containers, scientific, retro
  SnesTile, SegaGenTile, PcEngineTile, MasterSystemTile, SymbianMbm, XvThumbnail, IffRgbn, Mrc, Gd2, BigTiff, AutodeskCel, Wad2,
  // Wave 12: Game console tile formats
  GbaTile, WonderSwanTile, NeoGeoSprite, NdsTexture, VirtualBoyTile,
  // Wave 13: Additional retro home computers
  Vic20, Dragon, JupiterAce, Zx81, C128, C16Plus4, Electronika, Vector06c,
  // Wave 14: Calculator & embedded devices
  TiBitmap, HpGrob, EpaBios, CiscoIp, PocketPc2bp,
  // Wave 15: Japanese image formats
  Mag, Pi, Q0,
  // Wave 16: Phone/mobile formats
  NokiaLogo, NokiaNlm, SiemensBmx, PsionPic,
  // Wave 17: Fax & document formats
  KofaxKfx, BrooktroutFax, WinFax, EdmicsC4,
  // Wave 18: Additional professional/3D formats
  PixarRib, Sdt, MatLab, Ipl,
  // Wave 19: Raytracer & 3D rendering formats
  Vivid, Bob, GfaRaytrace, Cloe,
  // Wave 20: Additional game/engine formats
  QuakeLmp, HalfLifeMdl, HereticM8, DoomFlat,
  // Wave 21: Miscellaneous gap formats
  FaxG3, StelaRaw, MonoMagic, Ecw,
  // Wave 22: Additional retro, game console, and niche formats
  Thomson, CommodorePet, FmTowns, Pc88, Enterprise128, Atari7800, SharpX68k, RiscOsSprite, NeoGeoPocket, Atari2600,
  // Wave 23: C64 art program formats
  Doodle, DoodleComp, MicroIllustrator, Vidcom64, Picasso64, InterPaintHi, InterPaintMc,
  AdvancedArtStudio, RunPaint, Bfli, FunPainter, DrazPaint, GigaPaint, PrintfoxPagefox, Pagefox,
  // Wave 24: Retro/scientific/professional formats
  Stad, PortfolioGraphics, PrintShop, IconLibrary, YuvRaw, ZeissLsm, Ioca, SpotImage, ImageSystem, ZeissBivas, PrintMaster,
  // Wave 25: C64 art programs round 2
  Artist64, FacePainter, FunGraphicsMachine, GoDot4Bit, HiresC64, EggPaint, CDUPaint, RainbowPainter, KoalaCompressed,
  // Wave 26: Atari ST round 2
  DoodleAtari, Spectrum512Comp, Spectrum512Smoosh, DaliRaw, Gigacad, MegaPaint, FontasyGrafik, ArtDirector,
  // Wave 27: Unix/raytracer/scientific
  AndrewToolkit, MgrBitmap, FaceServer, DbwRender, SbigCcd, Cp8Gray, ComputerEyes, PmBitmap,
  // Wave 28: Game/fax/misc
  DivGameMap, HomeworldLif, Ps2Txc, HayesJtfax, GammaFax, CompW, DolphinEd, NokiaGroupGraphics,
  // Wave 29: Additional misc
  Im5Visilog, Pco16Bit, HiEddi, PaintMagic, SaracenPaint,
  // Wave 30: Fax variants batch 1
  AttGroup4, AccessFax, AdTechFax, BfxBitware, BrotherFax, CanonNavFax, EverexFax, FaxMan, FremontFax, ImagingFax,
  // Wave 31: Fax variants batch 2
  MobileFax, OazFax, OlicomFax, RicohFax, SciFax, SmartFax, Tg4, TeliFax, VentaFax, WorldportFax,
  // Wave 32: Scientific/satellite/industrial
  NistIHead, AvhrrImage, ByuSir, Grs16, CsvImage, HfImage, LucasFilm, QuantelVpb,
  // Wave 33: Game/camera/retro
  RedStormRsb, SegaSj1, SonyMavica, Pic2, FunPhotor, AtariGrafik, Calamus, NewsRoom,
  // Wave 34: Miscellaneous remaining
  AdexImage, AimGrayScale, QdvImage, SifImage, WebShots, Rlc2, SeqImage,
  // Wave 35: C64 art programs round 3
  Afli, Fli64, FliGraph, Blazing, DoodlePacked, SuperHires, FliDesigner2,
  Centauri, Pixel64, HiResEditor, MultiPainter, BugBitmap, AnimPainter, Cheese, ImageSysC64, Sprite64, CharSet64,
  // Wave 36: Atari 8-bit
  MicroPainter8, GraphicsMaster, FuntasticPaint, Interlace8, AtariPlayer, AtariCompressed, AtariFont,
  ArtStudio8, AtariGraphics9, AtariGraphics10, AtariGraphics11, RamBrandt, AtariArtist, GreatPaint,
  AtariCAD, AtariDump, MicroIllustratorA8, PictureEditor, HighResAtari, AtariMaxi, SoftwareAutomation,
  AtariPicture, AtariAnimation, CinemasterAtari, AtariAnticMode,
  // Wave 37: Atari ST/STE round 3
  AladdinPaint, Canvas, Crack, Deluxe, DigiSpec, EscapePaint, FreeHand, GfaPaint, HighResST,
  ImagicPaint, MonoStar, OcsPics, PaintPro, PicWorks, ScreenBlaster, SpcPainter, StTrueColor, TurboView,
  // Wave 38: Atari Falcon
  FalconPaint, FalconRes, GodPaint, IndyPaint, PhotoChrome, PntrFalcon, RagePaint, SmartST,
  SpeederFalcon, TriPaint, VidiChrome,
  // Wave 39: MSX2/MSX2+
  GraphSaurus, MsxView, MsxVideo, YJKImage, MsxSprite, MsxFont,
  // Wave 40: ZX Spectrum extensions
  ZxMulticolor, ZxGigascreen, ZxTimex, ZxArtStudio, ZxFlash, ZxMlg, ZxChrd, ZxTricolor, ZxUlaPlus, ZxNext,
  // Wave 41: Amiga extensions
  IffSham, IffDctv, IffHame, IffAnim8, IffDpan, IffMultiPalette,
  // Wave 42: TRS-80 CoCo / C128 / CPC
  CoCo, CoCo3, CoCoMax, C128Multi, C128VDC, C128Hires, CpcAdvanced, CpcOverscan, CpcPlus, CpcSprite, CpcFont,
  // Wave 43: Miscellaneous remaining
  AppleShr, MobyDick, ScreenMaker, PlotMaker, DigiView,
  // Wave 44: HDR/float/lossless formats
  Phm, Fl32, Nie,
  // Wave 45: Text-based/legacy formats
  FaceSaver,
  // Wave 46: C64 additional art programs
  Drazlace, TruePaint, PixelPerfect, EciGraphicEditor, CreateWithGarfield, SpritePad, FliProfi,
  // Wave 47: ZX Spectrum additional
  ZxBorderScreen, ZxMultiArtist,
  // Wave 48: Professional/mobile
  SeattleFilmWorks, NokiaOperatorLogo,
  // Wave 49: Atari ST additional
  AtariPaintworks, ExtendedGemImg,
  // Wave 50: C64 hires/FLI formats
  HiresFliCrest, HiresManager, HiresInterlaceFeniks, HiPicCreator, InterlaceHiresEditor, MultiLaceEditor,
  // Wave 51: C64 additional art programs
  LogoPainter, Ffli, Flip64,
  // Wave 52: Atari ST/misc
  DaliST, MultiPalettePicture, DrawIt,
  // Wave 53: ZX Spectrum/misc
  ZxPaintbrush, SpeccyExtended, MagicPainter,
  // Wave 54: C64 FLI variants
  CfliDesigner, UfliEditor, NufliEditor,
  // Wave 55: C64 multicolor editors
  AmicaPaint, EmcEditor, Flimatic,
  // Wave 56: Atari ST additional
  EzArt, Spectrum512Ext, PublicPainter,
  // Wave 57: Atari Falcon additional
  DuneGraph, PrismPaint, Rembrandt,
  // Wave 58: MSX/ZX/C64 additional
  G9b, ZxBorderMulticolor, Hireslace,
  // Wave 59: C64 hires/interlace editors
  SuperHiresEditor, Zoomatic, XFliEditor,
  // Wave 60: C64 FLI editors
  MuifliEditor, FliEditor, FliDesigner,
  // Wave 61: Atari ST additional
  SyntheticArts, HighresMedium, FullscreenKit,
  // Wave 62: Atari Falcon additional
  CokeAtari, AtariFalconXga, SpookySpritesFalcon,
  // Wave 63: Atari ST art programs
  PabloPaint, QuantumPaint, SinbadSlideshow,
  // Wave 64: C64 hires/interlace
  GephardHires, HardInterlace, RockyInterlace,
  // Wave 65: C64 multicolor/hires
  Din, HinterGrundBild, WigmoreArtist,
  // Wave 66: MSX screen modes
  MsxScreen2, MsxScreen5, MsxScreen8,
  // Wave 67: Atari 8-bit graphics modes
  AtariGr8, AtariGr7, AtariAgp,
  // Wave 68: Cross-platform simple
  BennetYeeFace, Olpc565, NokiaPictureMessage,
  // Wave 69: C64 interlace/multicolor
  ChampionsInterlace, InterlaceStudio, Mlt, Mcs,
  // Wave 70: Atari 8-bit / Apple II / misc
  AtariDrg, AppleIIHgr, AppleIIDhr, LogoSys,
  // Wave 71: Gap-fill (existing implementations missing enum)
  AtariGfb, AtariHr, HiresBitmap,
}
