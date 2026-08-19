#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <roapi.h>
#include <wrl.h>
#include <wrl/event.h>
#include <wrl/wrappers/corewrappers.h>

#include <DocumentSource.h>
#include <DocumentTarget.h>
#include <PrintManagerInterop.h>
#include <PrintPreview.h>
#include <d2d1_1.h>
#include <d2d1_1helper.h>
#include <d3d11_1.h>
#include <dxgi1_2.h>
#include <wincodec.h>
#include <windows.foundation.h>
#include <windows.graphics.printing.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <cwctype>
#include <limits>
#include <string>
#include <vector>

namespace wrl = Microsoft::WRL;
namespace wrlw = Microsoft::WRL::Wrappers;
namespace printing = ABI::Windows::Graphics::Printing;
namespace foundation = ABI::Windows::Foundation;

extern "C" {
int HSPDF_Initialize();
void* HSPDF_OpenDocument(const wchar_t* path);
void* HSPDF_OpenDocumentMemory(const unsigned char* data,
                               unsigned long long length);
void HSPDF_CloseDocument(void* handle);
int HSPDF_GetPageCount(void* handle);
int HSPDF_GetPageSize(void* handle,
                      int page_index,
                      double* width_points,
                      double* height_points);
int HSPDF_RenderPage(void* handle,
                     int page_index,
                     int width_pixels,
                     int height_pixels,
                     int rotation_quarter_turns,
                     int printing,
                     void* bgra_buffer,
                     int stride);
int HSPDF_GetAttachmentCount(void* handle);
int HSPDF_GetAttachmentName(void* handle,
                            int index,
                            void* utf16_buffer,
                            int capacity_chars);
unsigned long long HSPDF_GetAttachmentSize(void* handle, int index);
int HSPDF_CopyAttachmentData(void* handle,
                             int index,
                             void* buffer,
                             unsigned long long capacity);
}

namespace {

constexpr unsigned long long kMaxEmbeddedPdfBytes = 128ull * 1024ull * 1024ull;
constexpr double kMaxRenderPixels = 32000000.0;
constexpr float kMaxPrintRenderDpi = 300.0f;
constexpr foundation::AsyncStatus kAsyncStarted =
    static_cast<foundation::AsyncStatus>(0);
constexpr foundation::AsyncStatus kAsyncError =
    static_cast<foundation::AsyncStatus>(3);

enum PrintSessionState {
  kPrintSessionIdle = 0,
  kPrintSessionActive = 1,
  kPrintSessionCompleted = 2,
  kPrintSessionError = 3,
};

bool EndsWithPdf(const std::wstring& value) {
  if (value.size() < 4) {
    return false;
  }
  const size_t offset = value.size() - 4;
  return value[offset] == L'.' &&
         std::towlower(value[offset + 1]) == L'p' &&
         std::towlower(value[offset + 2]) == L'd' &&
         std::towlower(value[offset + 3]) == L'f';
}

int CompareNatural(const std::wstring& left, const std::wstring& right) {
  size_t a = 0;
  size_t b = 0;
  while (a < left.size() && b < right.size()) {
    const bool a_digit = std::iswdigit(left[a]) != 0;
    const bool b_digit = std::iswdigit(right[b]) != 0;
    if (a_digit && b_digit) {
      size_t a_end = a;
      size_t b_end = b;
      while (a_end < left.size() && std::iswdigit(left[a_end])) {
        ++a_end;
      }
      while (b_end < right.size() && std::iswdigit(right[b_end])) {
        ++b_end;
      }

      size_t a_sig = a;
      size_t b_sig = b;
      while (a_sig + 1 < a_end && left[a_sig] == L'0') {
        ++a_sig;
      }
      while (b_sig + 1 < b_end && right[b_sig] == L'0') {
        ++b_sig;
      }

      const size_t a_digits = a_end - a_sig;
      const size_t b_digits = b_end - b_sig;
      if (a_digits != b_digits) {
        return a_digits < b_digits ? -1 : 1;
      }
      for (size_t index = 0; index < a_digits; ++index) {
        if (left[a_sig + index] != right[b_sig + index]) {
          return left[a_sig + index] < right[b_sig + index] ? -1 : 1;
        }
      }
      const size_t a_run = a_end - a;
      const size_t b_run = b_end - b;
      if (a_run != b_run) {
        return a_run < b_run ? -1 : 1;
      }
      a = a_end;
      b = b_end;
      continue;
    }

    const wchar_t a_char = static_cast<wchar_t>(std::towlower(left[a]));
    const wchar_t b_char = static_cast<wchar_t>(std::towlower(right[b]));
    if (a_char != b_char) {
      return a_char < b_char ? -1 : 1;
    }
    ++a;
    ++b;
  }
  if (a < left.size()) {
    return 1;
  }
  if (b < right.size()) {
    return -1;
  }
  return 0;
}

struct AttachmentRef {
  int index = -1;
  std::wstring name;
};

struct PageRef {
  void* document = nullptr;
  int page_index = 0;
};

class PrintSession;

class ModernPrintDocumentSource final
    : public wrl::RuntimeClass<
          wrl::RuntimeClassFlags<wrl::WinRtClassicComMix>,
          printing::IPrintDocumentSource,
          IPrintDocumentPageSource,
          IPrintPreviewPageCollection> {
 public:
  HRESULT RuntimeClassInitialize(PrintSession* session);

  STDMETHOD(GetPreviewPageCollection)(
      IPrintDocumentPackageTarget* package_target,
      IPrintPreviewPageCollection** page_collection) override;
  STDMETHOD(MakeDocument)(IInspectable* options,
                           IPrintDocumentPackageTarget* package_target) override;
  STDMETHOD(Paginate)(UINT32 current_job_page, IInspectable* options) override;
  STDMETHOD(MakePage)(UINT32 desired_job_page,
                       FLOAT width,
                       FLOAT height) override;

 private:
  HRESULT EnsureDevices();
  HRESULT GetPageDescription(IInspectable* options,
                             printing::PrintPageDescription* description);
  HRESULT RenderFittedPage(UINT32 zero_based_page,
                           int max_width,
                           int max_height,
                           bool printing_mode,
                           std::vector<unsigned char>* pixels,
                           int* width,
                           int* height);
  HRESULT CreatePreviewSurface(UINT32 zero_based_page,
                               FLOAT width,
                               FLOAT height,
                               IDXGISurface** surface);
  HRESULT AddPrintedPage(ID2D1PrintControl* print_control,
                         UINT32 zero_based_page,
                         const printing::PrintPageDescription& page_description,
                         float render_dpi);

  PrintSession* session_ = nullptr;
  printing::PrintPageDescription preview_description_ = {};
  bool has_preview_description_ = false;
  wrl::ComPtr<IPrintPreviewDxgiPackageTarget> preview_target_;
  wrl::ComPtr<ID3D11Device> d3d_device_;
  wrl::ComPtr<ID2D1Factory1> d2d_factory_;
  wrl::ComPtr<ID2D1Device> d2d_device_;
  wrl::ComPtr<IWICImagingFactory> wic_factory_;
};

class PrintSession {
 public:
  PrintSession() = default;
  ~PrintSession() {
    if (task_ && completed_registered_) {
      task_->remove_Completed(completed_token_);
    }
    if (manager_ && requested_registered_) {
      manager_->remove_PrintTaskRequested(requested_token_);
    }
    source_.Reset();
    task_.Reset();
    manager_.Reset();
    print_ui_async_.Reset();
    for (void* document : documents_) {
      if (document) {
        HSPDF_CloseDocument(document);
      }
    }
    documents_.clear();
    pages_.clear();
  }

  bool AddFile(const wchar_t* path) {
    if (!path || !*path || state_.load() != kPrintSessionIdle) {
      return false;
    }
    paths_.emplace_back(path);
    return true;
  }

  HRESULT Begin(HWND owner, const wchar_t* title) {
    if (!owner || paths_.empty() || state_.load() != kPrintSessionIdle) {
      return E_INVALIDARG;
    }
    if (!HSPDF_Initialize()) {
      return E_FAIL;
    }

    HRESULT hr = RoInitialize(RO_INIT_SINGLETHREADED);
    if (FAILED(hr) && hr != RPC_E_CHANGED_MODE) {
      return hr;
    }

    wrl::ComPtr<IPrintManagerInterop> interop;
    wrlw::HStringReference print_manager_class(
        RuntimeClass_Windows_Graphics_Printing_PrintManager);
    hr = RoGetActivationFactory(print_manager_class.Get(),
                                IID_PPV_ARGS(&interop));
    if (FAILED(hr)) {
      return hr;
    }

    hr = interop->GetForWindow(owner, __uuidof(printing::IPrintManager),
                               reinterpret_cast<void**>(manager_.GetAddressOf()));
    if (FAILED(hr)) {
      return hr;
    }

    auto requested_handler = wrl::Callback<
        __FITypedEventHandler_2_Windows__CGraphics__CPrinting__CPrintManager_Windows__CGraphics__CPrinting__CPrintTaskRequestedEventArgs>(
        this, &PrintSession::OnPrintRequested);
    if (!requested_handler) {
      return E_OUTOFMEMORY;
    }
    hr = manager_->add_PrintTaskRequested(requested_handler.Get(),
                                          &requested_token_);
    if (FAILED(hr)) {
      return hr;
    }
    requested_registered_ = true;

    title_.Set((title && *title) ? title : L"HSPdf");
    state_.store(kPrintSessionActive);

    wrl::ComPtr<foundation::IAsyncOperation<boolean>> async_operation;
    hr = interop->ShowPrintUIForWindowAsync(
        owner, __uuidof(foundation::IAsyncOperation<boolean>),
        reinterpret_cast<void**>(async_operation.GetAddressOf()));
    if (FAILED(hr)) {
      error_.store(hr);
      state_.store(kPrintSessionError);
      return hr;
    }
    if (async_operation) {
      async_operation.As(&print_ui_async_);
    }
    return S_OK;
  }

  int State() {
    const int current = state_.load();
    if (current != kPrintSessionActive || task_created_.load() ||
        !print_ui_async_) {
      return current;
    }

    foundation::AsyncStatus async_status = kAsyncStarted;
    if (SUCCEEDED(print_ui_async_->get_Status(&async_status)) &&
        async_status != kAsyncStarted) {
      if (async_status == kAsyncError) {
        HRESULT hr = E_FAIL;
        print_ui_async_->get_ErrorCode(&hr);
        error_.store(hr);
        state_.store(kPrintSessionError);
      } else {
        completion_.store(printing::PrintTaskCompletion_Canceled);
        state_.store(kPrintSessionCompleted);
      }
    }
    return state_.load();
  }

  HRESULT Error() const { return error_.load(); }
  int Completion() const { return completion_.load(); }
  int Skipped() const { return skipped_.load(); }

  HRESULT EnsurePrepared() {
    AcquireSRWLockExclusive(&prepare_lock_);
    if (prepared_) {
      const HRESULT result = prepare_result_;
      ReleaseSRWLockExclusive(&prepare_lock_);
      return result;
    }

    HRESULT result = S_OK;
    for (const std::wstring& path : paths_) {
      void* parent = HSPDF_OpenDocument(path.c_str());
      if (!parent) {
        skipped_.fetch_add(1);
        continue;
      }
      AddDocument(parent);

      std::vector<AttachmentRef> attachments;
      const int attachment_count = HSPDF_GetAttachmentCount(parent);
      for (int index = 0; index < attachment_count; ++index) {
        const int required = HSPDF_GetAttachmentName(parent, index, nullptr, 0);
        if (required <= 1 || required > 32768) {
          continue;
        }
        std::vector<wchar_t> name(static_cast<size_t>(required), L'\0');
        const int copied = HSPDF_GetAttachmentName(
            parent, index, name.data(), static_cast<int>(name.size()));
        if (copied <= 1) {
          continue;
        }
        std::wstring attachment_name(name.data());
        if (EndsWithPdf(attachment_name)) {
          attachments.push_back({index, std::move(attachment_name)});
        }
      }

      std::sort(attachments.begin(), attachments.end(),
                [](const AttachmentRef& left, const AttachmentRef& right) {
                  return CompareNatural(left.name, right.name) < 0;
                });

      for (const AttachmentRef& attachment : attachments) {
        const unsigned long long size =
            HSPDF_GetAttachmentSize(parent, attachment.index);
        if (size == 0 || size > kMaxEmbeddedPdfBytes ||
            size > static_cast<unsigned long long>(
                       std::numeric_limits<size_t>::max())) {
          skipped_.fetch_add(1);
          continue;
        }
        std::vector<unsigned char> bytes(static_cast<size_t>(size));
        if (!HSPDF_CopyAttachmentData(parent, attachment.index, bytes.data(),
                                      size)) {
          skipped_.fetch_add(1);
          continue;
        }
        void* child = HSPDF_OpenDocumentMemory(bytes.data(), size);
        if (!child) {
          skipped_.fetch_add(1);
          continue;
        }
        AddDocument(child);
      }
    }

    if (pages_.empty()) {
      result = E_FAIL;
    }
    prepare_result_ = result;
    prepared_ = true;
    ReleaseSRWLockExclusive(&prepare_lock_);
    return result;
  }

  UINT32 PageCount() const {
    if (pages_.size() > std::numeric_limits<UINT32>::max()) {
      return 0;
    }
    return static_cast<UINT32>(pages_.size());
  }

  bool GetPage(UINT32 index, PageRef* page) const {
    if (!page || index >= pages_.size()) {
      return false;
    }
    *page = pages_[index];
    return true;
  }

 private:
  void AddDocument(void* document) {
    documents_.push_back(document);
    const int page_count = HSPDF_GetPageCount(document);
    if (page_count <= 0) {
      skipped_.fetch_add(1);
      return;
    }
    for (int page = 0; page < page_count; ++page) {
      pages_.push_back({document, page});
    }
  }

  HRESULT OnPrintRequested(printing::IPrintManager*,
                           printing::IPrintTaskRequestedEventArgs* event_args) {
    if (!event_args) {
      return E_POINTER;
    }
    wrl::ComPtr<printing::IPrintTaskRequest> request;
    HRESULT hr = event_args->get_Request(&request);
    if (FAILED(hr)) {
      error_.store(hr);
      return hr;
    }

    auto source_handler =
        wrl::Callback<printing::IPrintTaskSourceRequestedHandler>(
            this, &PrintSession::OnSourceRequested);
    if (!source_handler) {
      return E_OUTOFMEMORY;
    }
    hr = request->CreatePrintTask(title_.Get(), source_handler.Get(), &task_);
    if (FAILED(hr)) {
      error_.store(hr);
      return hr;
    }
    task_created_.store(true);

    auto completed_handler = wrl::Callback<
        __FITypedEventHandler_2_Windows__CGraphics__CPrinting__CPrintTask_Windows__CGraphics__CPrinting__CPrintTaskCompletedEventArgs>(
        this, &PrintSession::OnCompleted);
    if (!completed_handler) {
      return E_OUTOFMEMORY;
    }
    hr = task_->add_Completed(completed_handler.Get(), &completed_token_);
    if (SUCCEEDED(hr)) {
      completed_registered_ = true;
    } else {
      error_.store(hr);
    }
    return hr;
  }

  HRESULT OnSourceRequested(printing::IPrintTaskSourceRequestedArgs* args) {
    if (!args) {
      return E_POINTER;
    }
    if (!source_) {
      wrl::ComPtr<ModernPrintDocumentSource> concrete_source;
      HRESULT hr = wrl::MakeAndInitialize<ModernPrintDocumentSource>(
          &concrete_source, this);
      if (FAILED(hr)) {
        error_.store(hr);
        return hr;
      }
      hr = concrete_source.As(&source_);
      if (FAILED(hr)) {
        error_.store(hr);
        return hr;
      }
    }
    return args->SetSource(source_.Get());
  }

  HRESULT OnCompleted(printing::IPrintTask*,
                      printing::IPrintTaskCompletedEventArgs* args) {
    printing::PrintTaskCompletion completion =
        printing::PrintTaskCompletion_Abandoned;
    HRESULT hr = args ? args->get_Completion(&completion) : E_POINTER;
    if (SUCCEEDED(hr)) {
      completion_.store(static_cast<int>(completion));
      if (completion == printing::PrintTaskCompletion_Failed) {
        state_.store(kPrintSessionError);
      } else {
        state_.store(kPrintSessionCompleted);
      }
    } else {
      error_.store(hr);
      state_.store(kPrintSessionError);
    }
    return S_OK;
  }

  std::vector<std::wstring> paths_;
  std::vector<void*> documents_;
  std::vector<PageRef> pages_;
  SRWLOCK prepare_lock_ = SRWLOCK_INIT;
  bool prepared_ = false;
  HRESULT prepare_result_ = E_PENDING;

  std::atomic<int> state_{kPrintSessionIdle};
  std::atomic<HRESULT> error_{S_OK};
  std::atomic<int> completion_{printing::PrintTaskCompletion_Abandoned};
  std::atomic<int> skipped_{0};
  std::atomic<bool> task_created_{false};

  wrlw::HString title_;
  wrl::ComPtr<printing::IPrintManager> manager_;
  EventRegistrationToken requested_token_ = {};
  bool requested_registered_ = false;
  wrl::ComPtr<printing::IPrintTask> task_;
  EventRegistrationToken completed_token_ = {};
  bool completed_registered_ = false;
  wrl::ComPtr<printing::IPrintDocumentSource> source_;
  wrl::ComPtr<foundation::IAsyncInfo> print_ui_async_;
};

HRESULT ModernPrintDocumentSource::RuntimeClassInitialize(PrintSession* session) {
  if (!session) {
    return E_INVALIDARG;
  }
  session_ = session;
  return EnsureDevices();
}

HRESULT ModernPrintDocumentSource::EnsureDevices() {
  if (d3d_device_ && d2d_device_ && wic_factory_) {
    return S_OK;
  }

  UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
  D3D_FEATURE_LEVEL feature_level = D3D_FEATURE_LEVEL_9_1;
  wrl::ComPtr<ID3D11DeviceContext> immediate_context;
  HRESULT hr = D3D11CreateDevice(
      nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags, nullptr, 0,
      D3D11_SDK_VERSION, &d3d_device_, &feature_level, &immediate_context);
  if (FAILED(hr)) {
    hr = D3D11CreateDevice(
        nullptr, D3D_DRIVER_TYPE_WARP, nullptr, flags, nullptr, 0,
        D3D11_SDK_VERSION, &d3d_device_, &feature_level, &immediate_context);
  }
  if (FAILED(hr)) {
    return hr;
  }

  wrl::ComPtr<IDXGIDevice> dxgi_device;
  hr = d3d_device_.As(&dxgi_device);
  if (FAILED(hr)) {
    return hr;
  }

  D2D1_FACTORY_OPTIONS options = {};
  hr = D2D1CreateFactory(D2D1_FACTORY_TYPE_MULTI_THREADED,
                         __uuidof(ID2D1Factory1), &options,
                         reinterpret_cast<void**>(d2d_factory_.GetAddressOf()));
  if (FAILED(hr)) {
    return hr;
  }
  hr = d2d_factory_->CreateDevice(dxgi_device.Get(), &d2d_device_);
  if (FAILED(hr)) {
    return hr;
  }

  hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr,
                        CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic_factory_));
  return hr;
}

HRESULT ModernPrintDocumentSource::GetPageDescription(
    IInspectable* options,
    printing::PrintPageDescription* description) {
  if (!options || !description) {
    return E_POINTER;
  }
  wrl::ComPtr<printing::IPrintTaskOptionsCore> print_options;
  HRESULT hr = options->QueryInterface(IID_PPV_ARGS(&print_options));
  if (FAILED(hr)) {
    return hr;
  }
  return print_options->GetPageDescription(1, description);
}

HRESULT ModernPrintDocumentSource::GetPreviewPageCollection(
    IPrintDocumentPackageTarget* package_target,
    IPrintPreviewPageCollection** page_collection) {
  if (!package_target || !page_collection) {
    return E_POINTER;
  }
  preview_target_.Reset();
  HRESULT hr = package_target->GetPackageTarget(
      __uuidof(IPrintPreviewDxgiPackageTarget),
      __uuidof(IPrintPreviewDxgiPackageTarget),
      reinterpret_cast<void**>(preview_target_.GetAddressOf()));
  if (FAILED(hr)) {
    return hr;
  }

  wrl::ComPtr<IPrintPreviewPageCollection> self;
  hr = QueryInterface(IID_PPV_ARGS(&self));
  if (FAILED(hr)) {
    return hr;
  }
  return self.CopyTo(page_collection);
}

HRESULT ModernPrintDocumentSource::Paginate(UINT32,
                                             IInspectable* options) {
  if (!session_ || !preview_target_) {
    return E_UNEXPECTED;
  }
  HRESULT hr = session_->EnsurePrepared();
  if (FAILED(hr)) {
    return hr;
  }
  hr = GetPageDescription(options, &preview_description_);
  if (FAILED(hr)) {
    return hr;
  }
  has_preview_description_ = true;

  hr = preview_target_->InvalidatePreview();
  if (FAILED(hr)) {
    return hr;
  }
  const UINT32 page_count = session_->PageCount();
  if (page_count == 0) {
    return E_FAIL;
  }
  return preview_target_->SetJobPageCount(FinalPageCount, page_count);
}

HRESULT ModernPrintDocumentSource::RenderFittedPage(
    UINT32 zero_based_page,
    int max_width,
    int max_height,
    bool printing_mode,
    std::vector<unsigned char>* pixels,
    int* width,
    int* height) {
  if (!session_ || !pixels || !width || !height || max_width <= 0 ||
      max_height <= 0) {
    return E_INVALIDARG;
  }
  PageRef page;
  if (!session_->GetPage(zero_based_page, &page)) {
    return E_BOUNDS;
  }

  double page_width = 0.0;
  double page_height = 0.0;
  if (!HSPDF_GetPageSize(page.document, page.page_index, &page_width,
                         &page_height) ||
      page_width <= 0.0 || page_height <= 0.0) {
    return E_FAIL;
  }

  double scale = std::min(static_cast<double>(max_width) / page_width,
                          static_cast<double>(max_height) / page_height);
  int render_width =
      std::max(1, static_cast<int>(std::floor(page_width * scale)));
  int render_height =
      std::max(1, static_cast<int>(std::floor(page_height * scale)));

  double pixel_count =
      static_cast<double>(render_width) * static_cast<double>(render_height);
  if (pixel_count > kMaxRenderPixels) {
    const double correction = std::sqrt(kMaxRenderPixels / pixel_count);
    render_width =
        std::max(1, static_cast<int>(std::floor(render_width * correction)));
    render_height =
        std::max(1, static_cast<int>(std::floor(render_height * correction)));
  }

  const size_t stride = static_cast<size_t>(render_width) * 4u;
  if (stride > static_cast<size_t>(std::numeric_limits<int>::max()) ||
      static_cast<size_t>(render_height) >
          std::numeric_limits<size_t>::max() / stride) {
    return E_OUTOFMEMORY;
  }
  pixels->assign(stride * static_cast<size_t>(render_height), 0xFF);
  if (!HSPDF_RenderPage(page.document, page.page_index, render_width,
                        render_height, 0, printing_mode ? 1 : 0,
                        pixels->data(), static_cast<int>(stride))) {
    pixels->clear();
    return E_FAIL;
  }
  *width = render_width;
  *height = render_height;
  return S_OK;
}

HRESULT ModernPrintDocumentSource::CreatePreviewSurface(
    UINT32 zero_based_page,
    FLOAT width,
    FLOAT height,
    IDXGISurface** surface) {
  if (!surface || width <= 0.0f || height <= 0.0f ||
      !has_preview_description_) {
    return E_INVALIDARG;
  }
  *surface = nullptr;

  const int canvas_width = std::max(1, static_cast<int>(std::ceil(width)));
  const int canvas_height = std::max(1, static_cast<int>(std::ceil(height)));
  const size_t canvas_stride = static_cast<size_t>(canvas_width) * 4u;
  if (static_cast<size_t>(canvas_height) >
      std::numeric_limits<size_t>::max() / canvas_stride) {
    return E_OUTOFMEMORY;
  }
  std::vector<unsigned char> canvas(
      canvas_stride * static_cast<size_t>(canvas_height), 0xFF);

  const float page_width = std::max(1.0f, preview_description_.PageSize.Width);
  const float page_height = std::max(1.0f, preview_description_.PageSize.Height);
  const float scale_x = width / page_width;
  const float scale_y = height / page_height;
  const foundation::Rect& imageable = preview_description_.ImageableRect;
  int image_x = std::max(0, static_cast<int>(std::floor(imageable.X * scale_x)));
  int image_y = std::max(0, static_cast<int>(std::floor(imageable.Y * scale_y)));
  int image_width = std::max(
      1, static_cast<int>(std::floor(imageable.Width * scale_x)));
  int image_height = std::max(
      1, static_cast<int>(std::floor(imageable.Height * scale_y)));
  image_width = std::min(image_width, canvas_width - std::min(image_x, canvas_width - 1));
  image_height = std::min(image_height, canvas_height - std::min(image_y, canvas_height - 1));

  std::vector<unsigned char> page_pixels;
  int render_width = 0;
  int render_height = 0;
  HRESULT hr = RenderFittedPage(zero_based_page, image_width, image_height,
                                false, &page_pixels, &render_width,
                                &render_height);
  if (FAILED(hr)) {
    return hr;
  }

  const int dest_x = image_x + std::max(0, (image_width - render_width) / 2);
  const int dest_y = image_y + std::max(0, (image_height - render_height) / 2);
  const size_t render_stride = static_cast<size_t>(render_width) * 4u;
  for (int row = 0; row < render_height; ++row) {
    if (dest_y + row >= canvas_height) {
      break;
    }
    unsigned char* destination =
        canvas.data() + static_cast<size_t>(dest_y + row) * canvas_stride +
        static_cast<size_t>(dest_x) * 4u;
    const unsigned char* source =
        page_pixels.data() + static_cast<size_t>(row) * render_stride;
    const size_t bytes = std::min(
        render_stride,
        static_cast<size_t>(std::max(0, canvas_width - dest_x)) * 4u);
    std::memcpy(destination, source, bytes);
  }

  D3D11_TEXTURE2D_DESC description = {};
  description.Width = static_cast<UINT>(canvas_width);
  description.Height = static_cast<UINT>(canvas_height);
  description.MipLevels = 1;
  description.ArraySize = 1;
  description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
  description.SampleDesc.Count = 1;
  description.Usage = D3D11_USAGE_DEFAULT;
  description.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;

  D3D11_SUBRESOURCE_DATA initial = {};
  initial.pSysMem = canvas.data();
  initial.SysMemPitch = static_cast<UINT>(canvas_stride);

  wrl::ComPtr<ID3D11Texture2D> texture;
  hr = d3d_device_->CreateTexture2D(&description, &initial, &texture);
  if (FAILED(hr)) {
    return hr;
  }
  return texture->QueryInterface(IID_PPV_ARGS(surface));
}

HRESULT ModernPrintDocumentSource::MakePage(UINT32 desired_job_page,
                                             FLOAT width,
                                             FLOAT height) {
  if (!preview_target_ || !session_) {
    return E_UNEXPECTED;
  }
  if (desired_job_page == JOB_PAGE_APPLICATION_DEFINED) {
    desired_job_page = 1;
  }
  if (desired_job_page == 0 || desired_job_page > session_->PageCount()) {
    return E_BOUNDS;
  }

  wrl::ComPtr<IDXGISurface> surface;
  HRESULT hr = CreatePreviewSurface(desired_job_page - 1, width, height,
                                    &surface);
  if (FAILED(hr)) {
    return hr;
  }
  return preview_target_->DrawPage(desired_job_page, surface.Get(), 96.0f,
                                   96.0f);
}

HRESULT ModernPrintDocumentSource::AddPrintedPage(
    ID2D1PrintControl* print_control,
    UINT32 zero_based_page,
    const printing::PrintPageDescription& page_description,
    float render_dpi) {
  if (!print_control) {
    return E_POINTER;
  }
  const foundation::Rect& imageable = page_description.ImageableRect;
  const int max_width = std::max(
      1, static_cast<int>(std::ceil(imageable.Width * render_dpi / 96.0f)));
  const int max_height = std::max(
      1, static_cast<int>(std::ceil(imageable.Height * render_dpi / 96.0f)));

  std::vector<unsigned char> pixels;
  int render_width = 0;
  int render_height = 0;
  HRESULT hr = RenderFittedPage(zero_based_page, max_width, max_height, true,
                                &pixels, &render_width, &render_height);
  if (FAILED(hr)) {
    return hr;
  }

  PageRef page;
  if (!session_->GetPage(zero_based_page, &page)) {
    return E_BOUNDS;
  }
  double pdf_width = 0.0;
  double pdf_height = 0.0;
  if (!HSPDF_GetPageSize(page.document, page.page_index, &pdf_width,
                         &pdf_height) ||
      pdf_width <= 0.0 || pdf_height <= 0.0) {
    return E_FAIL;
  }
  const double dip_scale =
      std::min(static_cast<double>(imageable.Width) / pdf_width,
               static_cast<double>(imageable.Height) / pdf_height);
  const float draw_width = static_cast<float>(pdf_width * dip_scale);
  const float draw_height = static_cast<float>(pdf_height * dip_scale);
  const float draw_x = imageable.X + (imageable.Width - draw_width) / 2.0f;
  const float draw_y = imageable.Y + (imageable.Height - draw_height) / 2.0f;

  wrl::ComPtr<ID2D1DeviceContext> context;
  hr = d2d_device_->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE,
                                        &context);
  if (FAILED(hr)) {
    return hr;
  }

  D2D1_BITMAP_PROPERTIES1 bitmap_properties = D2D1::BitmapProperties1(
      D2D1_BITMAP_OPTIONS_NONE,
      D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,
                        D2D1_ALPHA_MODE_IGNORE),
      96.0f, 96.0f);
  wrl::ComPtr<ID2D1Bitmap1> bitmap;
  hr = context->CreateBitmap(
      D2D1::SizeU(static_cast<UINT32>(render_width),
                  static_cast<UINT32>(render_height)),
      pixels.data(), static_cast<UINT32>(render_width * 4),
      bitmap_properties, &bitmap);
  if (FAILED(hr)) {
    return hr;
  }

  wrl::ComPtr<ID2D1CommandList> command_list;
  hr = context->CreateCommandList(&command_list);
  if (FAILED(hr)) {
    return hr;
  }
  context->SetTarget(command_list.Get());
  context->BeginDraw();
  context->Clear(D2D1::ColorF(D2D1::ColorF::White));
  context->DrawBitmap(
      bitmap.Get(), D2D1::RectF(draw_x, draw_y, draw_x + draw_width,
                                draw_y + draw_height),
      1.0f, D2D1_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC);
  hr = context->EndDraw();
  HRESULT close_hr = command_list->Close();
  if (FAILED(hr)) {
    return hr;
  }
  if (FAILED(close_hr)) {
    return close_hr;
  }

  return print_control->AddPage(
      command_list.Get(),
      D2D1::SizeF(page_description.PageSize.Width,
                  page_description.PageSize.Height),
      nullptr);
}

HRESULT ModernPrintDocumentSource::MakeDocument(
    IInspectable* options,
    IPrintDocumentPackageTarget* package_target) {
  if (!session_ || !package_target) {
    return E_POINTER;
  }
  HRESULT hr = session_->EnsurePrepared();
  if (FAILED(hr)) {
    return hr;
  }

  printing::PrintPageDescription page_description = {};
  hr = GetPageDescription(options, &page_description);
  if (FAILED(hr)) {
    return hr;
  }

  float render_dpi = static_cast<float>(
      std::max<UINT32>(96, std::min(page_description.DpiX,
                                   page_description.DpiY)));
  render_dpi = std::min(render_dpi, kMaxPrintRenderDpi);

  D2D1_PRINT_CONTROL_PROPERTIES properties = {};
  properties.rasterDPI = render_dpi;
  properties.colorSpace = D2D1_COLOR_SPACE_SRGB;
  properties.fontSubset = D2D1_PRINT_FONT_SUBSET_MODE_DEFAULT;

  wrl::ComPtr<ID2D1PrintControl> print_control;
  hr = d2d_device_->CreatePrintControl(wic_factory_.Get(), package_target,
                                       properties, &print_control);
  if (FAILED(hr)) {
    return hr;
  }

  const UINT32 page_count = session_->PageCount();
  for (UINT32 page = 0; page < page_count; ++page) {
    hr = AddPrintedPage(print_control.Get(), page, page_description,
                        render_dpi);
    if (FAILED(hr)) {
      break;
    }
  }
  const HRESULT close_hr = print_control->Close();
  if (FAILED(hr)) {
    return hr;
  }
  return close_hr;
}

}  // namespace

extern "C" {

__declspec(dllexport) void* HSPDF_CreatePrintSession() {
  return new (std::nothrow) PrintSession();
}

__declspec(dllexport) int HSPDF_PrintSessionAddFile(void* session,
                                                     const wchar_t* path) {
  auto* value = static_cast<PrintSession*>(session);
  return value && value->AddFile(path) ? 1 : 0;
}

__declspec(dllexport) int HSPDF_BeginModernPrint(void* session,
                                                 void* owner_hwnd,
                                                 const wchar_t* title) {
  auto* value = static_cast<PrintSession*>(session);
  if (!value) {
    return static_cast<int>(E_POINTER);
  }
  return static_cast<int>(value->Begin(static_cast<HWND>(owner_hwnd), title));
}

__declspec(dllexport) int HSPDF_GetModernPrintState(void* session) {
  auto* value = static_cast<PrintSession*>(session);
  return value ? value->State() : kPrintSessionError;
}

__declspec(dllexport) int HSPDF_GetModernPrintError(void* session) {
  auto* value = static_cast<PrintSession*>(session);
  return value ? static_cast<int>(value->Error()) : static_cast<int>(E_POINTER);
}

__declspec(dllexport) int HSPDF_GetModernPrintCompletion(void* session) {
  auto* value = static_cast<PrintSession*>(session);
  return value ? value->Completion()
               : printing::PrintTaskCompletion_Failed;
}

__declspec(dllexport) int HSPDF_GetModernPrintSkippedCount(void* session) {
  auto* value = static_cast<PrintSession*>(session);
  return value ? value->Skipped() : 0;
}

__declspec(dllexport) void HSPDF_DestroyPrintSession(void* session) {
  delete static_cast<PrintSession*>(session);
}

}  // extern "C"
