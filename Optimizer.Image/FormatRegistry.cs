using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Acorn;
using FileFormat.AliasPix;
using FileFormat.AmstradCpc;
using FileFormat.Apng;
using FileFormat.AppleII;
using FileFormat.AppleIIgs;
using FileFormat.Art;
using FileFormat.Astc;
using FileFormat.Avs;
using FileFormat.BbcMicro;
using FileFormat.Bmp;
using FileFormat.Bsave;
using FileFormat.C64Multi;
using FileFormat.Cals;
using FileFormat.Ccitt;
using FileFormat.Cineon;
using FileFormat.Clp;
using FileFormat.Cmu;
using FileFormat.Core;
using FileFormat.CrackArt;
using FileFormat.Cur;
using FileFormat.Dcx;
using FileFormat.Dds;
using FileFormat.Degas;
using FileFormat.Dicom;
using FileFormat.Dpx;
using FileFormat.DrHalo;
using FileFormat.Exr;
using FileFormat.Farbfeld;
using FileFormat.Fits;
using FileFormat.Fli;
using FileFormat.GemImg;
using FileFormat.Hdr;
using FileFormat.Hrz;
using FileFormat.Ico;
using FileFormat.Ilbm;
using FileFormat.Jng;
using FileFormat.Jpeg;
using FileFormat.Koala;
using FileFormat.Ktx;
using FileFormat.MacPaint;
using FileFormat.Miff;
using FileFormat.Mng;
using FileFormat.Msp;
using FileFormat.Msx;
using FileFormat.Mtv;
using FileFormat.Neochrome;
using FileFormat.Netpbm;
using FileFormat.Nifti;
using FileFormat.Nrrd;
using FileFormat.OpenRaster;
using FileFormat.Oric;
using FileFormat.Otb;
using FileFormat.Palm;
using FileFormat.Pcx;
using FileFormat.Pfm;
using FileFormat.Pict;
using FileFormat.Pkm;
using FileFormat.Png;
using FileFormat.Psd;
using FileFormat.Pvr;
using FileFormat.Qoi;
using FileFormat.Qrt;
using FileFormat.Rla;
using FileFormat.SamCoupe;
using FileFormat.Sff;
using FileFormat.Sgi;
using FileFormat.Sixel;
using FileFormat.Spectrum512;
using FileFormat.SunRaster;
using FileFormat.Tga;
using FileFormat.Tiff;
using FileFormat.Tim;
using FileFormat.Tim2;
using FileFormat.Tiny;
using FileFormat.Vicar;
using FileFormat.Viff;
using FileFormat.Vtf;
using FileFormat.Wal;
using FileFormat.Wad3;
using FileFormat.Wbmp;
using FileFormat.Wpg;
using FileFormat.Xbm;
using FileFormat.Xcf;
using FileFormat.Xpm;
using FileFormat.Xwd;
using FileFormat.ZxSpectrum;
using FileFormat.Aai;
using FileFormat.Rgf;
using FileFormat.Fbm;
using FileFormat.Gbr;
using FileFormat.Pat;
using FileFormat.Xyz;
using FileFormat.Lss16;
using FileFormat.ColoRix;
using FileFormat.SunIcon;
using FileFormat.Cel;
using FileFormat.AmigaIcon;
using FileFormat.Gaf;
using FileFormat.GunPaint;
using FileFormat.GeoPaint;
using FileFormat.Psb;
using FileFormat.Icns;
using FileFormat.Blp;
using FileFormat.Fsh;
using FileFormat.Mpo;
using FileFormat.Pds;
using FileFormat.Ics;
using FileFormat.BioRadPic;
using FileFormat.Ptif;
using FileFormat.Bsb;
using FileFormat.Awd;
using FileFormat.Psp;
using FileFormat.Qtif;
using FileFormat.Ingr;
using FileFormat.Nitf;
using FileFormat.Uhdr;
using FileFormat.PalmPdb;
using FileFormat.Pcd;
using FileFormat.PhotoPaint;
using FileFormat.Pdn;
using FileFormat.Fpx;
using FileFormat.JpegLs;
using FileFormat.Jbig;
using FileFormat.Wsq;
using FileFormat.DjVu;
using FileFormat.Jbig2;
using FileFormat.Flif;
using FileFormat.Jpeg2000;
using FileFormat.JpegXr;
using FileFormat.Heif;
using FileFormat.Avif;
using FileFormat.JpegXl;
using FileFormat.Bpg;
using FileFormat.Dng;
using FileFormat.CameraRaw;
using FileFormat.Krita;
using FileFormat.Analyze;
using FileFormat.MetaImage;
using FileFormat.Eps;
using FileFormat.Wmf;
using FileFormat.Emf;
using FileFormat.Vips;
using FileFormat.QuakeSpr;
using FileFormat.NesChr;
using FileFormat.GameBoyTile;
using FileFormat.Atari8Bit;
using FileFormat.IffAnim;
using FileFormat.SoftImage;
using FileFormat.MayaIff;
using FileFormat.Envi;
using FileFormat.Xcursor;
using FileFormat.IffPbm;
using FileFormat.PcPaint;
using FileFormat.IffAcbm;
using FileFormat.IffDeep;
using FileFormat.IffRgb8;
using FileFormat.Interfile;
using FileFormat.AtariFalcon;
using FileFormat.Trs80;
using FileFormat.SnesTile;
using FileFormat.SegaGenTile;
using FileFormat.PcEngineTile;
using FileFormat.MasterSystemTile;
using FileFormat.SymbianMbm;
using FileFormat.XvThumbnail;
using FileFormat.IffRgbn;
using FileFormat.Mrc;
using FileFormat.Gd2;
using FileFormat.BigTiff;
using FileFormat.AutodeskCel;
using FileFormat.Wad2;

namespace Optimizer.Image;

/// <summary>Data-driven registry mapping <see cref="ImageFormat"/> to format-specific operations via <see cref="IImageFileFormat{TSelf}"/>.</summary>
internal static class FormatRegistry {

  internal sealed record FormatEntry(
    ImageFormat Format,
    string PrimaryExtension,
    string[] AllExtensions,
    Func<FileInfo, RawImage?> LoadRawImage,
    Func<RawImage, byte[]> ConvertFromRawImage,
    FormatCapability Capabilities
  );

  private static readonly Dictionary<ImageFormat, FormatEntry> _byFormat = new();
  private static readonly Dictionary<string, ImageFormat> _byExtension = new(StringComparer.OrdinalIgnoreCase);

  static FormatRegistry() {
    // Formats with dedicated optimizers
    _Register<PngFile>(ImageFormat.Png, FormatCapability.HasDedicatedOptimizer);
    _Register<BmpFile>(ImageFormat.Bmp, FormatCapability.HasDedicatedOptimizer);
    _Register<TgaFile>(ImageFormat.Tga, FormatCapability.HasDedicatedOptimizer);
    _Register<PcxFile>(ImageFormat.Pcx, FormatCapability.HasDedicatedOptimizer);
    _Register<JpegFile>(ImageFormat.Jpeg, FormatCapability.HasDedicatedOptimizer);
    _Register<TiffFile>(ImageFormat.Tiff, FormatCapability.HasDedicatedOptimizer);
    _Register<IcoFile>(ImageFormat.Ico, FormatCapability.HasDedicatedOptimizer);
    _Register<CurFile>(ImageFormat.Cur, FormatCapability.HasDedicatedOptimizer);

    // Lossless raster — variable resolution
    _Register<QoiFile>(ImageFormat.Qoi);
    _Register<FarbfeldFile>(ImageFormat.Farbfeld);
    _Register<SgiFile>(ImageFormat.Sgi);
    _Register<SunRasterFile>(ImageFormat.SunRaster);
    _Register<NetpbmFile>(ImageFormat.Netpbm);
    _Register<HrzFile>(ImageFormat.Hrz);
    _Register<MtvFile>(ImageFormat.Mtv);
    _Register<QrtFile>(ImageFormat.Qrt);
    _Register<AvsFile>(ImageFormat.Avs);

    // Complex raster — variable resolution
    _Register<HdrFile>(ImageFormat.Hdr);
    _Register<PfmFile>(ImageFormat.Pfm);
    _Register<PsdFile>(ImageFormat.Psd);
    _Register<XcfFile>(ImageFormat.Xcf);
    _Register<DdsFile>(ImageFormat.Dds);
    _Register<VtfFile>(ImageFormat.Vtf);
    _Register<KtxFile>(ImageFormat.Ktx);
    _Register<ExrFile>(ImageFormat.Exr);
    _Register<DpxFile>(ImageFormat.Dpx);
    _Register<MiffFile>(ImageFormat.Miff);
    _Register<AliasPixFile>(ImageFormat.AliasPix);
    _Register<RlaFile>(ImageFormat.Rla);
    _Register<ViffFile>(ImageFormat.Viff);
    _Register<XwdFile>(ImageFormat.Xwd);
    _Register<DicomFile>(ImageFormat.Dicom);
    _Register<CineonFile>(ImageFormat.Cineon);

    // Indexed/planar/retro — variable resolution
    _Register<DrHaloFile>(ImageFormat.DrHalo);
    _Register<PalmFile>(ImageFormat.Palm);
    _Register<SixelFile>(ImageFormat.Sixel);
    _Register<CcittFile>(ImageFormat.Ccitt);
    _Register<CalsFile>(ImageFormat.Cals);
    _Register<SffFile>(ImageFormat.Sff);
    _Register<OricFile>(ImageFormat.Oric);
    _Register<VicarFile>(ImageFormat.Vicar);
    _Register<BbcMicroFile>(ImageFormat.BbcMicro);
    _Register<AmstradCpcFile>(ImageFormat.AmstradCpc);
    _Register<C64MultiFile>(ImageFormat.C64Multi);
    _Register<BsaveFile>(ImageFormat.Bsave);
    _Register<DegasFile>(ImageFormat.Degas);
    _Register<NeochromeFile>(ImageFormat.Neochrome);
    _Register<CrackArtFile>(ImageFormat.CrackArt);
    _Register<Spectrum512File>(ImageFormat.Spectrum512);
    _Register<TinyFile>(ImageFormat.Tiny);
    _Register<AppleIIFile>(ImageFormat.AppleII);
    _Register<AppleIIgsFile>(ImageFormat.AppleIIgs);
    _Register<MsxFile>(ImageFormat.Msx);
    _Register<SamCoupeFile>(ImageFormat.SamCoupe);
    _Register<KoalaFile>(ImageFormat.Koala);
    _Register<ZxSpectrumFile>(ImageFormat.ZxSpectrum);
    _Register<JngFile>(ImageFormat.Jng);
    _Register<NiftiFile>(ImageFormat.Nifti);
    _Register<NrrdFile>(ImageFormat.Nrrd);
    _Register<FitsFile>(ImageFormat.Fits);
    _Register<FliFile>(ImageFormat.Fli);

    // Planar/container
    _Register<AcornFile>(ImageFormat.Acorn);
    _Register<IlbmFile>(ImageFormat.Ilbm);
    _Register<PictFile>(ImageFormat.Pict);
    _Register<OpenRasterFile>(ImageFormat.OpenRaster);

    // Container/multi-image
    _Register<ApngFile>(ImageFormat.Apng);
    _Register<MngFile>(ImageFormat.Mng);
    _Register<DcxFile>(ImageFormat.Dcx);
    _Register<Wad3File>(ImageFormat.Wad3);
    _Register<ArtFile>(ImageFormat.Art);
    _Register<TimFile>(ImageFormat.Tim);
    _Register<Tim2File>(ImageFormat.Tim2);
    _Register<WalFile>(ImageFormat.Wal);
    _Register<WpgFile>(ImageFormat.Wpg);
    _Register<PkmFile>(ImageFormat.Pkm);
    _Register<AstcFile>(ImageFormat.Astc);
    _Register<PvrFile>(ImageFormat.Pvr);
    _Register<ClpFile>(ImageFormat.Clp);

    // Monochrome-only formats
    _Register<WbmpFile>(ImageFormat.Wbmp, FormatCapability.MonochromeOnly);
    _Register<XbmFile>(ImageFormat.Xbm, FormatCapability.MonochromeOnly);
    _Register<MspFile>(ImageFormat.Msp, FormatCapability.MonochromeOnly);
    _Register<CmuFile>(ImageFormat.Cmu, FormatCapability.MonochromeOnly);
    _Register<OtbFile>(ImageFormat.Otb, FormatCapability.MonochromeOnly);
    _Register<MacPaintFile>(ImageFormat.MacPaint, FormatCapability.MonochromeOnly);

    // Indexed-only formats
    _Register<XpmFile>(ImageFormat.Xpm, FormatCapability.IndexedOnly);
    _Register<GemImgFile>(ImageFormat.GemImg, FormatCapability.IndexedOnly);
    _Register<Lss16File>(ImageFormat.Lss16, FormatCapability.IndexedOnly);
    _Register<ColoRixFile>(ImageFormat.ColoRix, FormatCapability.IndexedOnly);
    _Register<XyzFile>(ImageFormat.Xyz, FormatCapability.IndexedOnly);
    _Register<GafFile>(ImageFormat.Gaf, FormatCapability.IndexedOnly);

    // Wave 4: Trivial formats — variable resolution
    _Register<AaiFile>(ImageFormat.Aai);
    _Register<FbmFile>(ImageFormat.Fbm);
    _Register<GbrFile>(ImageFormat.Gbr);
    _Register<PatFile>(ImageFormat.Pat);
    _Register<CelFile>(ImageFormat.Cel);
    _Register<AmigaIconFile>(ImageFormat.AmigaIcon);

    // Wave 4: Monochrome-only
    _Register<RgfFile>(ImageFormat.Rgf, FormatCapability.MonochromeOnly);
    _Register<SunIconFile>(ImageFormat.SunIcon, FormatCapability.MonochromeOnly);
    _Register<GeoPaintFile>(ImageFormat.GeoPaint, FormatCapability.MonochromeOnly);

    // GunPaint is read-only (FromRawImage not supported) — extension detection only, no registry entry

    // Wave 5: Extensions & containers
    _Register<PsbFile>(ImageFormat.Psb);
    _Register<IcnsFile>(ImageFormat.Icns);
    _Register<BlpFile>(ImageFormat.Blp);
    _Register<FshFile>(ImageFormat.Fsh);
    _Register<MpoFile>(ImageFormat.Mpo);
    _Register<PdsFile>(ImageFormat.Pds);
    _Register<IcsFile>(ImageFormat.Ics);
    _Register<BioRadPicFile>(ImageFormat.BioRadPic);
    _Register<PtifFile>(ImageFormat.Ptif);

    // Wave 6: Medium complexity
    _Register<PspFile>(ImageFormat.Psp);
    _Register<QtifFile>(ImageFormat.Qtif);
    _Register<IngrFile>(ImageFormat.Ingr);
    _Register<NitfFile>(ImageFormat.Nitf);
    _Register<UhdrFile>(ImageFormat.Uhdr);
    _Register<PalmPdbFile>(ImageFormat.PalmPdb);
    _Register<PcdFile>(ImageFormat.Pcd);
    _Register<PhotoPaintFile>(ImageFormat.PhotoPaint);
    _Register<PdnFile>(ImageFormat.Pdn);
    _Register<FpxFile>(ImageFormat.Fpx);

    // Wave 6: Indexed-only
    _Register<BsbFile>(ImageFormat.Bsb, FormatCapability.IndexedOnly);

    // Wave 6: Monochrome-only
    _Register<AwdFile>(ImageFormat.Awd, FormatCapability.MonochromeOnly);

    // Wave 7: Complex codecs
    _Register<JpegLsFile>(ImageFormat.JpegLs);
    _Register<WsqFile>(ImageFormat.Wsq);
    _Register<DjVuFile>(ImageFormat.DjVu);
    _Register<FlifFile>(ImageFormat.Flif);
    _Register<Jpeg2000File>(ImageFormat.Jpeg2000);
    _Register<JpegXrFile>(ImageFormat.JpegXr);

    // Wave 7: Monochrome-only
    _Register<JbigFile>(ImageFormat.Jbig, FormatCapability.MonochromeOnly);
    _Register<Jbig2File>(ImageFormat.Jbig2, FormatCapability.MonochromeOnly);

    // Wave 8: Advanced codecs
    _Register<HeifFile>(ImageFormat.Heif);
    _Register<AvifFile>(ImageFormat.Avif);
    _Register<JpegXlFile>(ImageFormat.JpegXl);
    _Register<BpgFile>(ImageFormat.Bpg);
    _Register<DngFile>(ImageFormat.Dng);
    _Register<CameraRawFile>(ImageFormat.CameraRaw);

    // Wave 9: Additional formats
    _Register<KritaFile>(ImageFormat.Krita);
    _Register<AnalyzeFile>(ImageFormat.Analyze);
    _Register<MetaImageFile>(ImageFormat.MetaImage);
    _Register<EpsFile>(ImageFormat.Eps);
    _Register<WmfFile>(ImageFormat.Wmf);
    _Register<EmfFile>(ImageFormat.Emf);
    _Register<VipsFile>(ImageFormat.Vips);
    _Register<QuakeSprFile>(ImageFormat.QuakeSpr);
    _Register<NesChrFile>(ImageFormat.NesChr, FormatCapability.IndexedOnly);
    _Register<GameBoyTileFile>(ImageFormat.GameBoyTile, FormatCapability.IndexedOnly);
    _Register<Atari8BitFile>(ImageFormat.Atari8Bit, FormatCapability.IndexedOnly);
    _Register<IffAnimFile>(ImageFormat.IffAnim);

    // Wave 10: IFF variants, professional 3D, scientific, retro
    _Register<SoftImageFile>(ImageFormat.SoftImage);
    _Register<MayaIffFile>(ImageFormat.MayaIff);
    _Register<EnviFile>(ImageFormat.Envi);
    _Register<XcursorFile>(ImageFormat.Xcursor);
    _Register<IffPbmFile>(ImageFormat.IffPbm, FormatCapability.IndexedOnly);
    _Register<PcPaintFile>(ImageFormat.PcPaint, FormatCapability.IndexedOnly);
    _Register<IffAcbmFile>(ImageFormat.IffAcbm, FormatCapability.IndexedOnly);
    _Register<IffDeepFile>(ImageFormat.IffDeep);
    _Register<IffRgb8File>(ImageFormat.IffRgb8);
    _Register<InterfileFile>(ImageFormat.Interfile);
    _Register<AtariFalconFile>(ImageFormat.AtariFalcon);
    _Register<Trs80File>(ImageFormat.Trs80, FormatCapability.MonochromeOnly);

    // Wave 11: Console tiles, containers, scientific, retro
    _Register<SnesTileFile>(ImageFormat.SnesTile, FormatCapability.IndexedOnly);
    _Register<SegaGenTileFile>(ImageFormat.SegaGenTile, FormatCapability.IndexedOnly);
    _Register<PcEngineTileFile>(ImageFormat.PcEngineTile, FormatCapability.IndexedOnly);
    _Register<MasterSystemTileFile>(ImageFormat.MasterSystemTile, FormatCapability.IndexedOnly);
    _Register<SymbianMbmFile>(ImageFormat.SymbianMbm);
    _Register<XvThumbnailFile>(ImageFormat.XvThumbnail);
    _Register<IffRgbnFile>(ImageFormat.IffRgbn);
    _Register<MrcFile>(ImageFormat.Mrc);
    _Register<Gd2File>(ImageFormat.Gd2);
    _Register<BigTiffFile>(ImageFormat.BigTiff);
    _Register<AutodeskCelFile>(ImageFormat.AutodeskCel, FormatCapability.IndexedOnly);
    _Register<Wad2File>(ImageFormat.Wad2, FormatCapability.IndexedOnly);
  }

  private static void _Register<T>(ImageFormat format, FormatCapability caps = FormatCapability.VariableResolution)
    where T : IImageFileFormat<T> {
    var entry = new FormatEntry(
      format,
      T.PrimaryExtension,
      T.FileExtensions,
      file => {
        try {
          return T.ToRawImage(T.FromFile(file));
        } catch {
          return null;
        }
      },
      raw => T.ToBytes(T.FromRawImage(raw)),
      caps
    );
    _byFormat[format] = entry;
    foreach (var ext in T.FileExtensions)
      _byExtension.TryAdd(ext, format);
  }

  internal static FormatEntry? GetEntry(ImageFormat format)
    => _byFormat.GetValueOrDefault(format);

  internal static string GetExtension(ImageFormat format)
    => GetEntry(format)?.PrimaryExtension ?? "";

  internal static ImageFormat DetectFromExtension(string extension)
    => _byExtension.GetValueOrDefault(extension);

  internal static IEnumerable<FormatEntry> ConversionTargets
    => _byFormat.Values.Where(e => (e.Capabilities & FormatCapability.HasDedicatedOptimizer) == 0);
}
