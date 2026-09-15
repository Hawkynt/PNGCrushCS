#include <errno.h>
#include <limits.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "hap.h"

#define BCDEC_IMPLEMENTATION
#include "bcdec.h"

static void serial_decode(HapDecodeWorkFunction function, void *context, unsigned int count, void *info) {
  (void)info;
  for (unsigned int index = 0; index < count; ++index)
    function(context, index);
}

static int parse_dimension(const char *text, int *value) {
  char *end = NULL;
  errno = 0;
  const long parsed = strtol(text, &end, 10);
  if (errno != 0 || end == text || *end != '\0' || parsed <= 0 || parsed > INT_MAX)
    return 0;

  *value = (int)parsed;
  return 1;
}

static unsigned char *read_file(const char *path, size_t *length) {
  FILE *file = fopen(path, "rb");
  if (file == NULL)
    return NULL;

  if (fseek(file, 0, SEEK_END) != 0) {
    fclose(file);
    return NULL;
  }

  const long size = ftell(file);
  if (size <= 0 || fseek(file, 0, SEEK_SET) != 0) {
    fclose(file);
    return NULL;
  }

  unsigned char *data = (unsigned char *)malloc((size_t)size);
  if (data == NULL) {
    fclose(file);
    return NULL;
  }

  if (fread(data, 1, (size_t)size, file) != (size_t)size) {
    free(data);
    fclose(file);
    return NULL;
  }

  fclose(file);
  *length = (size_t)size;
  return data;
}

static int write_file(const char *path, const void *data, size_t length) {
  FILE *file = fopen(path, "wb");
  if (file == NULL)
    return 0;

  const int ok = fwrite(data, 1, length, file) == length;
  fclose(file);
  return ok;
}

static int expected_format(const char *kind, unsigned int *format, int *is_signed) {
  *is_signed = 0;
  if (strcmp(kind, "bc7") == 0) {
    *format = HapTextureFormat_RGBA_BPTC_UNORM;
    return 1;
  }
  if (strcmp(kind, "bc6u") == 0) {
    *format = HapTextureFormat_RGB_BPTC_UNSIGNED_FLOAT;
    return 1;
  }
  if (strcmp(kind, "bc6s") == 0) {
    *format = HapTextureFormat_RGB_BPTC_SIGNED_FLOAT;
    *is_signed = 1;
    return 1;
  }
  return 0;
}

int main(int argc, char **argv) {
  if (argc != 7) {
    fprintf(stderr, "usage: %s <bc7|bc6u|bc6s> <frame.hap> <texture.bin> <pixels.bin> <width> <height>\n", argv[0]);
    return 2;
  }

  int width;
  int height;
  if (!parse_dimension(argv[5], &width) || !parse_dimension(argv[6], &height)
      || (width & 3) != 0 || (height & 3) != 0) {
    fprintf(stderr, "width and height must be positive multiples of four\n");
    return 2;
  }

  unsigned int wanted_format;
  int is_signed;
  if (!expected_format(argv[1], &wanted_format, &is_signed)) {
    fprintf(stderr, "unknown texture kind '%s'\n", argv[1]);
    return 2;
  }

  size_t frame_bytes = 0;
  unsigned char *frame = read_file(argv[2], &frame_bytes);
  if (frame == NULL) {
    fprintf(stderr, "could not read Hap frame '%s'\n", argv[2]);
    return 3;
  }

  unsigned int texture_count = 0;
  unsigned int result = HapGetFrameTextureCount(frame, (unsigned long)frame_bytes, &texture_count);
  if (result != HapResult_No_Error || texture_count != 1) {
    fprintf(stderr, "HapGetFrameTextureCount returned %u with count %u\n", result, texture_count);
    free(frame);
    return 4;
  }

  unsigned int declared_format = 0;
  result = HapGetFrameTextureFormat(frame, (unsigned long)frame_bytes, 0, &declared_format);
  if (result != HapResult_No_Error || declared_format != wanted_format) {
    fprintf(stderr, "HapGetFrameTextureFormat returned %u with format 0x%X, expected 0x%X\n",
      result, declared_format, wanted_format);
    free(frame);
    return 5;
  }

  const size_t block_count = (size_t)(width / 4) * (size_t)(height / 4);
  const size_t texture_bytes = block_count * BCDEC_BC7_BLOCK_SIZE;
  unsigned char *texture = (unsigned char *)malloc(texture_bytes);
  if (texture == NULL) {
    free(frame);
    return 6;
  }

  unsigned long decoded_texture_bytes = 0;
  unsigned int decoded_format = 0;
  result = HapDecode(
    frame,
    (unsigned long)frame_bytes,
    0,
    serial_decode,
    NULL,
    texture,
    (unsigned long)texture_bytes,
    &decoded_texture_bytes,
    &decoded_format);
  free(frame);

  if (result != HapResult_No_Error || decoded_format != wanted_format || decoded_texture_bytes != texture_bytes) {
    fprintf(stderr, "HapDecode returned %u, format 0x%X, %lu bytes; expected 0x%X and %zu bytes\n",
      result, decoded_format, decoded_texture_bytes, wanted_format, texture_bytes);
    free(texture);
    return 7;
  }

  if (!write_file(argv[3], texture, texture_bytes)) {
    fprintf(stderr, "could not write extracted texture '%s'\n", argv[3]);
    free(texture);
    return 8;
  }

  if (wanted_format == HapTextureFormat_RGBA_BPTC_UNORM) {
    const size_t pixel_bytes = (size_t)width * (size_t)height * 4;
    unsigned char *pixels = (unsigned char *)calloc(pixel_bytes, 1);
    if (pixels == NULL) {
      free(texture);
      return 9;
    }

    const unsigned char *source = texture;
    for (int y = 0; y < height; y += 4)
      for (int x = 0; x < width; x += 4) {
        unsigned char *destination = pixels + ((size_t)y * (size_t)width + (size_t)x) * 4;
        bcdec_bc7(source, destination, width * 4);
        source += BCDEC_BC7_BLOCK_SIZE;
      }

    if (!write_file(argv[4], pixels, pixel_bytes)) {
      fprintf(stderr, "could not write BC7 samples '%s'\n", argv[4]);
      free(pixels);
      free(texture);
      return 10;
    }
    free(pixels);
  } else {
    const size_t sample_count = (size_t)width * (size_t)height * 3;
    float *pixels = (float *)calloc(sample_count, sizeof(float));
    if (pixels == NULL) {
      free(texture);
      return 9;
    }

    const unsigned char *source = texture;
    for (int y = 0; y < height; y += 4)
      for (int x = 0; x < width; x += 4) {
        float *destination = pixels + ((size_t)y * (size_t)width + (size_t)x) * 3;
        bcdec_bc6h_float(source, destination, width * 3, is_signed);
        source += BCDEC_BC6H_BLOCK_SIZE;
      }

    if (!write_file(argv[4], pixels, sample_count * sizeof(float))) {
      fprintf(stderr, "could not write BC6H samples '%s'\n", argv[4]);
      free(pixels);
      free(texture);
      return 10;
    }
    free(pixels);
  }

  printf("Vidvox HapDecode format=0x%X texture=%zu bytes; bcdec %s decoded %dx%d samples\n",
    decoded_format, texture_bytes, argv[1], width, height);

  free(texture);
  return 0;
}
