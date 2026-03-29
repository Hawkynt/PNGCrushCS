using System;
using System.IO;
using Optimizer.Image;

namespace Optimizer.Image.Tests;

[TestFixture]
public sealed class ImageFormatDetectorTests {

  [Test]
  public void DetectFromSignature_PngMagic_ReturnsPng() {
    ReadOnlySpan<byte> header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Png));
  }

  [Test]
  public void DetectFromSignature_GifMagic_ReturnsGif() {
    ReadOnlySpan<byte> header = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Gif));
  }

  [Test]
  public void DetectFromSignature_TiffLittleEndian_ReturnsTiff() {
    ReadOnlySpan<byte> header = [0x49, 0x49, 0x2A, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Tiff));
  }

  [Test]
  public void DetectFromSignature_TiffBigEndian_ReturnsTiff() {
    ReadOnlySpan<byte> header = [0x4D, 0x4D, 0x00, 0x2A, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Tiff));
  }

  [Test]
  public void DetectFromSignature_BmpMagic_ReturnsBmp() {
    ReadOnlySpan<byte> header = [0x42, 0x4D, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Bmp));
  }

  [Test]
  public void DetectFromSignature_JpegMagic_ReturnsJpeg() {
    ReadOnlySpan<byte> header = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Jpeg));
  }

  [Test]
  public void DetectFromSignature_IcoMagic_ReturnsIco() {
    ReadOnlySpan<byte> header = [0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Ico));
  }

  [Test]
  public void DetectFromSignature_CurMagic_ReturnsCur() {
    ReadOnlySpan<byte> header = [0x00, 0x00, 0x02, 0x00, 0x01, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Cur));
  }

  [Test]
  public void DetectFromSignature_WebPMagic_ReturnsWebP() {
    ReadOnlySpan<byte> header = [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.WebP));
  }

  [Test]
  public void DetectFromSignature_AniMagic_ReturnsAni() {
    ReadOnlySpan<byte> header = [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x41, 0x43, 0x4F, 0x4E, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Ani));
  }

  [Test]
  public void DetectFromSignature_QoiMagic_ReturnsQoi() {
    ReadOnlySpan<byte> header = [0x71, 0x6F, 0x69, 0x66, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Qoi));
  }

  [Test]
  public void DetectFromSignature_FarbfeldMagic_ReturnsFarbfeld() {
    ReadOnlySpan<byte> header = [0x66, 0x61, 0x72, 0x62, 0x66, 0x65, 0x6C, 0x64, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Farbfeld));
  }

  [Test]
  public void DetectFromSignature_PcxMagic_ReturnsPcx() {
    ReadOnlySpan<byte> header = [0x0A, 0x05, 0x01, 0x08, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Pcx));
  }

  [Test]
  public void DetectFromSignature_Empty_ReturnsUnknown() {
    Assert.That(ImageFormatDetector.DetectFromSignature(ReadOnlySpan<byte>.Empty), Is.EqualTo(ImageFormat.Unknown));
  }

  [Test]
  public void DetectFromSignature_Random_ReturnsUnknown() {
    ReadOnlySpan<byte> header = [0xDE, 0xAD, 0xBE, 0xEF, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Unknown));
  }

  [Test]
  public void DetectFromExtension_PngExtension_ReturnsPng() {
    var file = new FileInfo("test.png");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Png));
  }

  [Test]
  public void DetectFromExtension_JpgExtension_ReturnsJpeg() {
    var file = new FileInfo("test.jpg");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Jpeg));
  }

  [Test]
  public void DetectFromExtension_TgaExtension_ReturnsTga() {
    var file = new FileInfo("test.tga");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Tga));
  }

  [Test]
  public void DetectFromExtension_UnknownExtension_ReturnsUnknown() {
    var file = new FileInfo("test.zzz");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Unknown));
  }

  [Test]
  public void Detect_NullFile_Throws() {
    Assert.Throws<ArgumentNullException>(() => ImageFormatDetector.Detect(null!));
  }

  [Test]
  public void Detect_MissingFile_Throws() {
    Assert.Throws<FileNotFoundException>(() => ImageFormatDetector.Detect(new FileInfo("nonexistent.png")));
  }

  [Test]
  public void DetectFromSignature_SgiMagic_ReturnsSgi() {
    var header = new byte[16];
    header[0] = 0x01;
    header[1] = 0xDA;
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Sgi));
  }

  [Test]
  public void DetectFromSignature_SunRasterMagic_ReturnsSunRaster() {
    var header = new byte[16];
    header[0] = 0x59;
    header[1] = 0xA6;
    header[2] = 0x6A;
    header[3] = 0x95;
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.SunRaster));
  }

  [Test]
  public void DetectFromSignature_DdsMagic_ReturnsDds() {
    var header = new byte[16];
    header[0] = 0x44;
    header[1] = 0x44;
    header[2] = 0x53;
    header[3] = 0x20;
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Dds));
  }

  [Test]
  public void DetectFromSignature_PsdMagic_ReturnsPsd() {
    var header = new byte[16];
    header[0] = 0x38;
    header[1] = 0x42;
    header[2] = 0x50;
    header[3] = 0x53;
    header[4] = 0x00;
    header[5] = 0x01;
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Psd));
  }

  [Test]
  public void DetectFromSignature_VtfMagic_ReturnsVtf() {
    var header = new byte[16];
    header[0] = 0x56;
    header[1] = 0x54;
    header[2] = 0x46;
    header[3] = 0x00;
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Vtf));
  }

  [Test]
  public void DetectFromSignature_ExrMagic_ReturnsExr() {
    var header = new byte[16];
    header[0] = 0x76;
    header[1] = 0x2F;
    header[2] = 0x31;
    header[3] = 0x01;
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Exr));
  }

  [Test]
  public void DetectFromSignature_AstcMagic_ReturnsAstc() {
    var header = new byte[16];
    header[0] = 0x13;
    header[1] = 0xAB;
    header[2] = 0xA1;
    header[3] = 0x5C;
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Astc));
  }

  [Test]
  public void DetectFromSignature_PkmMagic_ReturnsPkm() {
    var header = new byte[16];
    header[0] = 0x50;
    header[1] = 0x4B;
    header[2] = 0x4D;
    header[3] = 0x20;
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Pkm));
  }

  [Test]
  public void DetectFromSignature_Wad3Magic_ReturnsWad3() {
    var header = new byte[16];
    header[0] = 0x57;
    header[1] = 0x41;
    header[2] = 0x44;
    header[3] = 0x33;
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Wad3));
  }

  [Test]
  public void DetectFromSignature_DcxMagic_ReturnsDcx() {
    var header = new byte[16];
    header[0] = 0xB1;
    header[1] = 0x68;
    header[2] = 0xDE;
    header[3] = 0x3A;
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Dcx));
  }

  [Test]
  public void DetectFromSignature_XcfMagic_ReturnsXcf() {
    var header = new byte[16];
    header[0] = 0x67; // g
    header[1] = 0x69; // i
    header[2] = 0x6D; // m
    header[3] = 0x70; // p
    header[4] = 0x20; // (space)
    header[5] = 0x78; // x
    header[6] = 0x63; // c
    header[7] = 0x66; // f
    header[8] = 0x20; // (space)
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Xcf));
  }

  [Test]
  public void DetectFromSignature_FitsMagic_ReturnsFits() {
    var header = new byte[16];
    header[0] = 0x53; // S
    header[1] = 0x49; // I
    header[2] = 0x4D; // M
    header[3] = 0x50; // P
    header[4] = 0x4C; // L
    header[5] = 0x45; // E
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Fits));
  }

  [Test]
  public void DetectFromExtension_SgiExtension_ReturnsSgi() {
    var file = new FileInfo("test.sgi");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Sgi));
  }

  [Test]
  public void DetectFromExtension_PsdExtension_ReturnsPsd() {
    var file = new FileInfo("test.psd");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Psd));
  }

  [Test]
  public void DetectFromExtension_ExrExtension_ReturnsExr() {
    var file = new FileInfo("test.exr");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Exr));
  }

  [Test]
  public void DetectFromSignature_AwdMagic_ReturnsAwd() {
    ReadOnlySpan<byte> header = [0x41, 0x57, 0x44, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Awd));
  }

  [Test]
  public void DetectFromSignature_PspMagic_ReturnsPsp() {
    var header = new byte[32];
    header[0] = 0x50; // P
    header[1] = 0x61; // a
    header[2] = 0x69; // i
    header[3] = 0x6E; // n
    header[4] = 0x74; // t
    header[5] = 0x20; // (space)
    header[6] = 0x53; // S
    header[7] = 0x68; // h
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Psp));
  }

  [Test]
  public void DetectFromSignature_NitfMagic_ReturnsNitf() {
    ReadOnlySpan<byte> header = [0x4E, 0x49, 0x54, 0x46, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Nitf));
  }

  [Test]
  public void DetectFromSignature_UhdrMagic_ReturnsUhdr() {
    ReadOnlySpan<byte> header = [0x55, 0x48, 0x44, 0x52, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Uhdr));
  }

  [Test]
  public void DetectFromSignature_PhotoPaintMagic_ReturnsPhotoPaint() {
    ReadOnlySpan<byte> header = [0x43, 0x50, 0x54, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.PhotoPaint));
  }

  [Test]
  public void DetectFromSignature_PdnMagic_ReturnsPdn() {
    ReadOnlySpan<byte> header = [0x50, 0x44, 0x4E, 0x33, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Pdn));
  }

  [Test]
  public void DetectFromSignature_FpxMagic_ReturnsFpx() {
    ReadOnlySpan<byte> header = [0x46, 0x50, 0x58, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Fpx));
  }

  [Test]
  public void DetectFromExtension_BsbExtension_ReturnsBsb() {
    var file = new FileInfo("test.kap");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Bsb));
  }

  [Test]
  public void DetectFromExtension_QtifExtension_ReturnsQtif() {
    var file = new FileInfo("test.qtif");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Qtif));
  }

  [Test]
  public void DetectFromExtension_IngrExtension_ReturnsIngr() {
    var file = new FileInfo("test.cit");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Ingr));
  }

  [Test]
  public void DetectFromSignature_PalmPdbMagic_ReturnsPalmPdb() {
    var header = new byte[64];
    header[60] = 0x49; // I
    header[61] = 0x6D; // m
    header[62] = 0x67; // g
    header[63] = 0x20; // (space)
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.PalmPdb));
  }

  [Test]
  public void DetectFromExtension_PcdExtension_ReturnsPcd() {
    var file = new FileInfo("test.pcd");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Pcd));
  }

  [Test]
  public void DetectFromSignature_JpegLsMagic_ReturnsJpegLs() {
    ReadOnlySpan<byte> header = [0xFF, 0xD8, 0xFF, 0xF7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.JpegLs));
  }

  [Test]
  public void DetectFromExtension_JbigExtension_ReturnsJbig() {
    var file = new FileInfo("test.jbg");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Jbig));
  }

  [Test]
  public void DetectFromSignature_WsqMagic_ReturnsWsq() {
    ReadOnlySpan<byte> header = [0xFF, 0xA0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Wsq));
  }

  [Test]
  public void DetectFromSignature_DjVuMagic_ReturnsDjVu() {
    ReadOnlySpan<byte> header = [0x41, 0x54, 0x26, 0x54, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.DjVu));
  }

  [Test]
  public void DetectFromSignature_Jbig2Magic_ReturnsJbig2() {
    ReadOnlySpan<byte> header = [0x97, 0x4A, 0x42, 0x32, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Jbig2));
  }

  [Test]
  public void DetectFromSignature_FlifMagic_ReturnsFlif() {
    ReadOnlySpan<byte> header = [0x46, 0x4C, 0x49, 0x46, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Flif));
  }

  [Test]
  public void DetectFromSignature_Jpeg2000Magic_ReturnsJpeg2000() {
    ReadOnlySpan<byte> header = [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Jpeg2000));
  }

  [Test]
  public void DetectFromSignature_JpegXrMagic_ReturnsJpegXr() {
    ReadOnlySpan<byte> header = [0x49, 0x49, 0x01, 0xBC, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.JpegXr));
  }

  [Test]
  public void DetectFromSignature_HeifMagic_ReturnsHeif() {
    // ftyp box with "heic" brand: [size(4)] [ftyp(4)] [heic(4)]
    ReadOnlySpan<byte> header = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Heif));
  }

  [Test]
  public void DetectFromSignature_HeifMif1Brand_ReturnsHeif() {
    ReadOnlySpan<byte> header = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x69, 0x66, 0x31, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Heif));
  }

  [Test]
  public void DetectFromSignature_AvifMagic_ReturnsAvif() {
    ReadOnlySpan<byte> header = [0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69, 0x66, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Avif));
  }

  [Test]
  public void DetectFromSignature_AvisBrand_ReturnsAvif() {
    ReadOnlySpan<byte> header = [0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69, 0x73, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Avif));
  }

  [Test]
  public void DetectFromSignature_JpegXlBareCodestream_ReturnsJpegXl() {
    ReadOnlySpan<byte> header = [0xFF, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.JpegXl));
  }

  [Test]
  public void DetectFromSignature_JpegXlContainer_ReturnsJpegXl() {
    ReadOnlySpan<byte> header = [0x00, 0x00, 0x00, 0x0C, 0x66, 0x74, 0x79, 0x70, 0x6A, 0x78, 0x6C, 0x20, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.JpegXl));
  }

  [Test]
  public void DetectFromSignature_BpgMagic_ReturnsBpg() {
    ReadOnlySpan<byte> header = [0x42, 0x50, 0x47, 0xFB, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Bpg));
  }

  [Test]
  public void DetectFromExtension_DngExtension_ReturnsDng() {
    var file = new FileInfo("test.dng");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Dng));
  }

  [Test]
  public void DetectFromExtension_Cr2Extension_ReturnsCameraRaw() {
    var file = new FileInfo("test.cr2");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.CameraRaw));
  }

  [Test]
  public void DetectFromExtension_NefExtension_ReturnsCameraRaw() {
    var file = new FileInfo("test.nef");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.CameraRaw));
  }

  [Test]
  public void DetectFromExtension_RafExtension_ReturnsCameraRaw() {
    var file = new FileInfo("test.raf");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.CameraRaw));
  }

  // Wave 9 signature tests

  [Test]
  public void DetectFromSignature_EpsMagic_ReturnsEps() {
    ReadOnlySpan<byte> header = [0xC5, 0xD0, 0xD3, 0xC6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Eps));
  }

  [Test]
  public void DetectFromSignature_WmfMagic_ReturnsWmf() {
    ReadOnlySpan<byte> header = [0xD7, 0xCD, 0xC6, 0x9A, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Wmf));
  }

  [Test]
  public void DetectFromSignature_EmfMagic_ReturnsEmf() {
    var header = new byte[48];
    header[0] = 0x01; // EMR_HEADER type (LE uint32 = 1)
    header[40] = 0x20; // ' '
    header[41] = 0x45; // 'E'
    header[42] = 0x4D; // 'M'
    header[43] = 0x46; // 'F'
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Emf));
  }

  [Test]
  public void DetectFromSignature_QuakeSprMagic_ReturnsQuakeSpr() {
    ReadOnlySpan<byte> header = [0x49, 0x44, 0x53, 0x50, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.QuakeSpr));
  }

  [Test]
  public void DetectFromSignature_AnalyzeMagic_ReturnsAnalyze() {
    var header = new byte[48];
    header[0] = 0x5C; // sizeof_hdr = 348 (LE)
    header[1] = 0x01;
    header[40] = 0x03; // dim[0] = 3 (3D volume)
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Analyze));
  }

  [Test]
  public void DetectFromSignature_IffAnimMagic_ReturnsIffAnim() {
    var header = new byte[16];
    header[0] = 0x46; // F
    header[1] = 0x4F; // O
    header[2] = 0x52; // R
    header[3] = 0x4D; // M
    header[8] = 0x41; // A
    header[9] = 0x4E; // N
    header[10] = 0x49; // I
    header[11] = 0x4D; // M
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.IffAnim));
  }

  // Wave 9 extension tests

  [Test]
  public void DetectFromExtension_KraExtension_ReturnsKrita() {
    var file = new FileInfo("test.kra");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Krita));
  }

  [Test]
  public void DetectFromExtension_MhaExtension_ReturnsMetaImage() {
    var file = new FileInfo("test.mha");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.MetaImage));
  }

  [Test]
  public void DetectFromExtension_VipsExtension_ReturnsVips() {
    var file = new FileInfo("test.vips");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Vips));
  }

  [Test]
  public void DetectFromExtension_ChrExtension_ReturnsNesChr() {
    var file = new FileInfo("test.chr");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.NesChr));
  }

  [Test]
  public void DetectFromExtension_2bppExtension_ReturnsGameBoyTile() {
    var file = new FileInfo("test.2bpp");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.GameBoyTile));
  }

  [Test]
  public void DetectFromExtension_Gr8Extension_ReturnsAtari8Bit() {
    var file = new FileInfo("test.gr8");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Atari8Bit));
  }

  [Test]
  public void DetectFromExtension_AnimExtension_ReturnsIffAnim() {
    var file = new FileInfo("test.anim");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.IffAnim));
  }

  // Wave 10 signature tests

  [Test]
  public void DetectFromSignature_SoftImageMagic_ReturnsSoftImage() {
    ReadOnlySpan<byte> header = [0x53, 0x80, 0xF6, 0x34, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.SoftImage));
  }

  [Test]
  public void DetectFromSignature_MayaIffMagic_ReturnsMayaIff() {
    var header = new byte[16];
    header[0] = 0x46; // F
    header[1] = 0x4F; // O
    header[2] = 0x52; // R
    header[3] = 0x34; // 4
    header[8] = 0x43; // C
    header[9] = 0x49; // I
    header[10] = 0x4D; // M
    header[11] = 0x47; // G
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.MayaIff));
  }

  [Test]
  public void DetectFromSignature_EnviMagic_ReturnsEnvi() {
    ReadOnlySpan<byte> header = [0x45, 0x4E, 0x56, 0x49, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Envi));
  }

  [Test]
  public void DetectFromSignature_EnviWithCrLf_ReturnsEnvi() {
    ReadOnlySpan<byte> header = [0x45, 0x4E, 0x56, 0x49, 0x0D, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Envi));
  }

  [Test]
  public void DetectFromSignature_XcursorMagic_ReturnsXcursor() {
    ReadOnlySpan<byte> header = [0x58, 0x63, 0x75, 0x72, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Xcursor));
  }

  [Test]
  public void DetectFromSignature_IffPbmMagic_ReturnsIffPbm() {
    var header = new byte[16];
    header[0] = 0x46; // F
    header[1] = 0x4F; // O
    header[2] = 0x52; // R
    header[3] = 0x4D; // M
    header[8] = 0x50; // P
    header[9] = 0x42; // B
    header[10] = 0x4D; // M
    header[11] = 0x20; // (space)
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.IffPbm));
  }

  [Test]
  public void DetectFromSignature_PcPaintMagic_ReturnsPcPaint() {
    var header = new byte[16];
    header[0] = 0x34; // magic LE 0x1234
    header[1] = 0x12;
    header[2] = 0x40; // width = 320 (0x0140 LE)
    header[3] = 0x01;
    header[4] = 0xC8; // height = 200 (0x00C8 LE)
    header[5] = 0x00;
    header[10] = 0x01; // planes = 1
    header[11] = 0x08; // bpp = 8
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.PcPaint));
  }

  [Test]
  public void DetectFromSignature_IffAcbmMagic_ReturnsIffAcbm() {
    var header = new byte[16];
    header[0] = 0x46; // F
    header[1] = 0x4F; // O
    header[2] = 0x52; // R
    header[3] = 0x4D; // M
    header[8] = 0x41; // A
    header[9] = 0x43; // C
    header[10] = 0x42; // B
    header[11] = 0x4D; // M
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.IffAcbm));
  }

  [Test]
  public void DetectFromSignature_IffDeepMagic_ReturnsIffDeep() {
    var header = new byte[16];
    header[0] = 0x46; // F
    header[1] = 0x4F; // O
    header[2] = 0x52; // R
    header[3] = 0x4D; // M
    header[8] = 0x44; // D
    header[9] = 0x45; // E
    header[10] = 0x45; // E
    header[11] = 0x50; // P
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.IffDeep));
  }

  [Test]
  public void DetectFromSignature_IffRgb8Magic_ReturnsIffRgb8() {
    var header = new byte[16];
    header[0] = 0x46; // F
    header[1] = 0x4F; // O
    header[2] = 0x52; // R
    header[3] = 0x4D; // M
    header[8] = 0x52; // R
    header[9] = 0x47; // G
    header[10] = 0x42; // B
    header[11] = 0x38; // 8
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.IffRgb8));
  }

  [Test]
  public void DetectFromSignature_InterfileMagic_ReturnsInterfile() {
    var header = new byte[16];
    header[0] = 0x21; // !
    header[1] = 0x49; // I
    header[2] = 0x4E; // N
    header[3] = 0x54; // T
    header[4] = 0x45; // E
    header[5] = 0x52; // R
    header[6] = 0x46; // F
    header[7] = 0x49; // I
    header[8] = 0x4C; // L
    header[9] = 0x45; // E
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Interfile));
  }

  // Wave 10 extension tests

  [Test]
  public void DetectFromExtension_MayaExtension_ReturnsMayaIff() {
    var file = new FileInfo("test.maya");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.MayaIff));
  }

  [Test]
  public void DetectFromExtension_XcurExtension_ReturnsXcursor() {
    var file = new FileInfo("test.xcur");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Xcursor));
  }

  [Test]
  public void DetectFromExtension_HvExtension_ReturnsInterfile() {
    var file = new FileInfo("test.hv");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Interfile));
  }

  [Test]
  public void DetectFromExtension_FtcExtension_ReturnsAtariFalcon() {
    var file = new FileInfo("test.ftc");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.AtariFalcon));
  }

  [Test]
  public void DetectFromExtension_HrExtension_ReturnsTrs80() {
    var file = new FileInfo("test.hr");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Trs80));
  }

  // Wave 11 signature tests

  [Test]
  public void DetectFromSignature_BigTiffLeMagic_ReturnsBigTiff() {
    ReadOnlySpan<byte> header = [0x49, 0x49, 0x2B, 0x00, 0x08, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.BigTiff));
  }

  [Test]
  public void DetectFromSignature_BigTiffBeMagic_ReturnsBigTiff() {
    ReadOnlySpan<byte> header = [0x4D, 0x4D, 0x00, 0x2B, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.BigTiff));
  }

  [Test]
  public void DetectFromSignature_XvThumbnailMagic_ReturnsXvThumbnail() {
    ReadOnlySpan<byte> header = [0x50, 0x37, 0x20, 0x33, 0x33, 0x32, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.XvThumbnail));
  }

  [Test]
  public void DetectFromSignature_IffRgbnMagic_ReturnsIffRgbn() {
    var header = new byte[16];
    header[0] = 0x46; // F
    header[1] = 0x4F; // O
    header[2] = 0x52; // R
    header[3] = 0x4D; // M
    header[8] = 0x52; // R
    header[9] = 0x47; // G
    header[10] = 0x42; // B
    header[11] = 0x4E; // N
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.IffRgbn));
  }

  [Test]
  public void DetectFromSignature_SymbianMbmMagic_ReturnsSymbianMbm() {
    ReadOnlySpan<byte> header = [0x37, 0x00, 0x00, 0x10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.SymbianMbm));
  }

  [Test]
  public void DetectFromSignature_Gd2Magic_ReturnsGd2() {
    ReadOnlySpan<byte> header = [0x67, 0x64, 0x32, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Gd2));
  }

  [Test]
  public void DetectFromSignature_Wad2Magic_ReturnsWad2() {
    ReadOnlySpan<byte> header = [0x57, 0x41, 0x44, 0x32, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.Wad2));
  }

  [Test]
  public void DetectFromSignature_AutodeskCelMagic_ReturnsAutodeskCel() {
    var header = new byte[16];
    header[0] = 0x19; // magic LE 0x9119
    header[1] = 0x91;
    header[2] = 0x40; // width = 320 (0x0140 LE)
    header[3] = 0x01;
    header[4] = 0xC8; // height = 200 (0x00C8 LE)
    header[5] = 0x00;
    header[10] = 0x08; // bpp = 8 (LE)
    header[11] = 0x00;
    Assert.That(ImageFormatDetector.DetectFromSignature(header), Is.EqualTo(ImageFormat.AutodeskCel));
  }

  // Wave 11 extension tests

  [Test]
  public void DetectFromExtension_SfcExtension_ReturnsSnesTile() {
    var file = new FileInfo("test.sfc");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.SnesTile));
  }

  [Test]
  public void DetectFromExtension_GenExtension_ReturnsSegaGenTile() {
    var file = new FileInfo("test.gen");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.SegaGenTile));
  }

  [Test]
  public void DetectFromExtension_PceExtension_ReturnsPcEngineTile() {
    var file = new FileInfo("test.pce");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.PcEngineTile));
  }

  [Test]
  public void DetectFromExtension_SmsExtension_ReturnsMasterSystemTile() {
    var file = new FileInfo("test.sms");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.MasterSystemTile));
  }

  [Test]
  public void DetectFromExtension_BtfExtension_ReturnsBigTiff() {
    var file = new FileInfo("test.btf");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.BigTiff));
  }

  [Test]
  public void DetectFromExtension_MrcExtension_ReturnsMrc() {
    var file = new FileInfo("test.mrc");
    Assert.That(ImageFormatDetector.DetectFromExtension(file), Is.EqualTo(ImageFormat.Mrc));
  }
}
