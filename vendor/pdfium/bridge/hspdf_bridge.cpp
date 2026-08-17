#define NOMINMAX
#include <windows.h>

#include <cstdint>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <new>

#include "public/fpdf_attachment.h"
#include "public/fpdfview.h"

namespace {

std::recursive_mutex g_pdfium_mutex;
bool g_initialized = false;

struct DocumentHolder {
  FPDF_DOCUMENT document = nullptr;
  HANDLE file = INVALID_HANDLE_VALUE;
  std::unique_ptr<unsigned char[]> memory;
  size_t memory_size = 0;
  FPDF_FILEACCESS access = {};

  ~DocumentHolder() {
    if (document) {
      FPDF_CloseDocument(document);
      document = nullptr;
    }
    if (file != INVALID_HANDLE_VALUE) {
      CloseHandle(file);
      file = INVALID_HANDLE_VALUE;
    }
  }
};

std::unique_ptr<DocumentHolder> MakeHolder() {
  return std::unique_ptr<DocumentHolder>(
      new (std::nothrow) DocumentHolder());
}

int GetFileBlock(void* parameter,
                 unsigned long position,
                 unsigned char* buffer,
                 unsigned long size) {
  auto* holder = static_cast<DocumentHolder*>(parameter);
  if (!holder || holder->file == INVALID_HANDLE_VALUE || !buffer || size == 0) {
    return 0;
  }

  LARGE_INTEGER offset = {};
  offset.QuadPart = static_cast<LONGLONG>(position);
  if (!SetFilePointerEx(holder->file, offset, nullptr, FILE_BEGIN)) {
    return 0;
  }

  DWORD bytes_read = 0;
  if (!ReadFile(holder->file, buffer, static_cast<DWORD>(size), &bytes_read,
                nullptr)) {
    return 0;
  }
  return bytes_read == size ? 1 : 0;
}

DocumentHolder* AsHolder(void* handle) {
  return static_cast<DocumentHolder*>(handle);
}

FPDF_ATTACHMENT GetAttachment(DocumentHolder* holder, int index) {
  if (!holder || !holder->document || index < 0) {
    return nullptr;
  }
  const int count = FPDFDoc_GetAttachmentCount(holder->document);
  if (index >= count) {
    return nullptr;
  }
  return FPDFDoc_GetAttachment(holder->document, index);
}

}  // namespace

extern "C" {

__declspec(dllexport) int HSPDF_Initialize() {
  std::lock_guard<std::recursive_mutex> lock(g_pdfium_mutex);
  if (!g_initialized) {
    FPDF_InitLibrary();
    g_initialized = true;
  }
  return 1;
}

__declspec(dllexport) void HSPDF_Shutdown() {
  std::lock_guard<std::recursive_mutex> lock(g_pdfium_mutex);
  if (g_initialized) {
    FPDF_DestroyLibrary();
    g_initialized = false;
  }
}

__declspec(dllexport) void* HSPDF_OpenDocument(const wchar_t* path) {
  std::lock_guard<std::recursive_mutex> lock(g_pdfium_mutex);
  if (!g_initialized || !path || !*path) {
    return nullptr;
  }

  auto holder = MakeHolder();
  if (!holder) {
    return nullptr;
  }

  holder->file = CreateFileW(path, GENERIC_READ,
                             FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                             nullptr, OPEN_EXISTING,
                             FILE_ATTRIBUTE_NORMAL | FILE_FLAG_RANDOM_ACCESS, nullptr);
  if (holder->file == INVALID_HANDLE_VALUE) {
    return nullptr;
  }

  LARGE_INTEGER size = {};
  if (!GetFileSizeEx(holder->file, &size) || size.QuadPart <= 0 ||
      static_cast<unsigned long long>(size.QuadPart) >
          std::numeric_limits<unsigned long>::max()) {
    return nullptr;
  }

  holder->access.m_FileLen = static_cast<unsigned long>(size.QuadPart);
  holder->access.m_GetBlock = &GetFileBlock;
  holder->access.m_Param = holder.get();
  holder->document = FPDF_LoadCustomDocument(&holder->access, nullptr);
  if (!holder->document) {
    return nullptr;
  }

  return holder.release();
}

__declspec(dllexport) void* HSPDF_OpenDocumentMemory(
    const unsigned char* data,
    unsigned long long length) {
  std::lock_guard<std::recursive_mutex> lock(g_pdfium_mutex);
  if (!g_initialized || !data || length == 0 ||
      length > static_cast<unsigned long long>(std::numeric_limits<size_t>::max())) {
    return nullptr;
  }

  auto holder = MakeHolder();
  if (!holder) {
    return nullptr;
  }

  holder->memory_size = static_cast<size_t>(length);
  holder->memory.reset(new (std::nothrow) unsigned char[holder->memory_size]);
  if (!holder->memory) {
    return nullptr;
  }
  std::memcpy(holder->memory.get(), data, holder->memory_size);

  holder->document = FPDF_LoadMemDocument64(holder->memory.get(),
                                             holder->memory_size, nullptr);
  if (!holder->document) {
    return nullptr;
  }
  return holder.release();
}

__declspec(dllexport) void HSPDF_CloseDocument(void* handle) {
  std::lock_guard<std::recursive_mutex> lock(g_pdfium_mutex);
  delete AsHolder(handle);
}

__declspec(dllexport) unsigned long HSPDF_GetLastError() {
  std::lock_guard<std::recursive_mutex> lock(g_pdfium_mutex);
  return FPDF_GetLastError();
}

__declspec(dllexport) int HSPDF_GetPageCount(void* handle) {
  std::lock_guard<std::recursive_mutex> lock(g_pdfium_mutex);
  auto* holder = AsHolder(handle);
  return holder && holder->document ? FPDF_GetPageCount(holder->document) : 0;
}

__declspec(dllexport) int HSPDF_GetPageSize(void* handle,
                                            int page_index,
                                            double* width_points,
                                            double* height_points) {
  std::lock_guard<std::recursive_mutex> lock(g_pdfium_mutex);
  auto* holder = AsHolder(handle);
  if (!holder || !holder->document || !width_points || !height_points ||
      page_index < 0) {
    return 0;
  }

  FS_SIZEF size = {};
  if (!FPDF_GetPageSizeByIndexF(holder->document, page_index, &size)) {
    return 0;
  }
  *width_points = static_cast<double>(size.width);
  *height_points = static_cast<double>(size.height);
  return 1;
}

__declspec(dllexport) int HSPDF_RenderPage(void* handle,
                                           int page_index,
                                           int width_pixels,
                                           int height_pixels,
                                           int rotation_quarter_turns,
                                           int printing,
                                           void* bgra_buffer,
                                           int stride) {
  std::lock_guard<std::recursive_mutex> lock(g_pdfium_mutex);
  auto* holder = AsHolder(handle);
  if (!holder || !holder->document || page_index < 0 || width_pixels <= 0 ||
      height_pixels <= 0 || !bgra_buffer || stride < width_pixels * 4) {
    return 0;
  }

  FPDF_PAGE page = FPDF_LoadPage(holder->document, page_index);
  if (!page) {
    return 0;
  }

  FPDF_BITMAP bitmap = FPDFBitmap_CreateEx(width_pixels, height_pixels,
                                            FPDFBitmap_BGRA, bgra_buffer, stride);
  if (!bitmap) {
    FPDF_ClosePage(page);
    return 0;
  }

  FPDFBitmap_FillRect(bitmap, 0, 0, width_pixels, height_pixels, 0xFFFFFFFF);
  int rotation = rotation_quarter_turns % 4;
  if (rotation < 0) {
    rotation += 4;
  }

  int flags = FPDF_ANNOT | FPDF_RENDER_LIMITEDIMAGECACHE;
  if (printing) {
    flags |= FPDF_PRINTING;
  } else {
    flags |= FPDF_LCD_TEXT;
  }

  FPDF_RenderPageBitmap(bitmap, page, 0, 0, width_pixels, height_pixels,
                        rotation, flags);
  FPDFBitmap_Destroy(bitmap);
  FPDF_ClosePage(page);
  return 1;
}

__declspec(dllexport) int HSPDF_GetAttachmentCount(void* handle) {
  std::lock_guard<std::recursive_mutex> lock(g_pdfium_mutex);
  auto* holder = AsHolder(handle);
  if (!holder || !holder->document) {
    return 0;
  }
  const int count = FPDFDoc_GetAttachmentCount(holder->document);
  return count > 0 ? count : 0;
}

__declspec(dllexport) int HSPDF_GetAttachmentName(void* handle,
                                                  int index,
                                                  void* utf16_buffer,
                                                  int capacity_chars) {
  std::lock_guard<std::recursive_mutex> lock(g_pdfium_mutex);
  auto* holder = AsHolder(handle);
  FPDF_ATTACHMENT attachment = GetAttachment(holder, index);
  if (!attachment) {
    return 0;
  }

  const unsigned long required_bytes =
      FPDFAttachment_GetName(attachment, nullptr, 0);
  if (required_bytes == 0 || (required_bytes % sizeof(FPDF_WCHAR)) != 0) {
    return 0;
  }

  const int required_chars =
      static_cast<int>(required_bytes / sizeof(FPDF_WCHAR));
  if (!utf16_buffer || capacity_chars <= 0) {
    return required_chars;
  }
  if (capacity_chars < required_chars ||
      static_cast<unsigned long long>(capacity_chars) * sizeof(FPDF_WCHAR) >
          std::numeric_limits<unsigned long>::max()) {
    return required_chars;
  }

  const unsigned long copied_bytes = FPDFAttachment_GetName(
      attachment, reinterpret_cast<FPDF_WCHAR*>(utf16_buffer),
      static_cast<unsigned long>(capacity_chars * sizeof(FPDF_WCHAR)));
  return copied_bytes == 0
             ? 0
             : static_cast<int>(copied_bytes / sizeof(FPDF_WCHAR));
}

__declspec(dllexport) unsigned long long HSPDF_GetAttachmentSize(void* handle,
                                                                 int index) {
  std::lock_guard<std::recursive_mutex> lock(g_pdfium_mutex);
  auto* holder = AsHolder(handle);
  FPDF_ATTACHMENT attachment = GetAttachment(holder, index);
  if (!attachment) {
    return 0;
  }

  unsigned long length = 0;
  if (!FPDFAttachment_GetFile(attachment, nullptr, 0, &length)) {
    return 0;
  }
  return static_cast<unsigned long long>(length);
}

__declspec(dllexport) int HSPDF_CopyAttachmentData(void* handle,
                                                   int index,
                                                   void* buffer,
                                                   unsigned long long capacity) {
  std::lock_guard<std::recursive_mutex> lock(g_pdfium_mutex);
  auto* holder = AsHolder(handle);
  FPDF_ATTACHMENT attachment = GetAttachment(holder, index);
  if (!attachment || !buffer || capacity == 0 ||
      capacity > std::numeric_limits<unsigned long>::max()) {
    return 0;
  }

  unsigned long actual = 0;
  if (!FPDFAttachment_GetFile(attachment, buffer,
                              static_cast<unsigned long>(capacity), &actual)) {
    return 0;
  }
  return actual <= capacity ? 1 : 0;
}

}  // extern "C"
