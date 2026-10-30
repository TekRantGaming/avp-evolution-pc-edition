// PC port of AVP Evolution: NVIDIA DLSS and Intel XeSS for the Unity 5.6 player (Direct3D 11), as a native
// rendering plugin. This software contains source code provided by NVIDIA Corporation.
//
// C# (PCGraphics.cs, PCDlss.cs) renders the world at a lower resolution with a jittered projection, copies the depth
// and motion vectors into its own textures, hands the native texture pointers to PCUp_SetFrame and then issues
// GL.IssuePluginEvent(PCUp_GetRenderEventFunc(), kEventEvaluate); everything that touches the GPU runs on Unity's
// render thread. One upscaler is active at a time: kind 1 is DLSS, kind 2 is XeSS (D3D11 XeSS runs on Intel Arc only;
// libxess_dx11.dll is loaded when asked for, so the plugin works without it).
#include <windows.h>
#include <d3d11.h>

#include <cstdarg>
#include <cstdio>
#include <mutex>

#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D11.h"
#include "IUnityInterface.h"
#include "nvsdk_ngx.h"
#include "nvsdk_ngx_helpers.h"
#include "nvsdk_ngx_helpers_d3d.h"
#include "xess/xess.h"
#include "xess/xess_d3d11.h"

namespace {

enum Kind { kDlss = 1, kXess = 2 };
// Render events: (kind << 8) | event.
enum Event { kEventInit = 1, kEventEvaluate = 2, kEventRelease = 3 };

// Status for C#: 0 not tried yet, 1 ready, negative: why not (PCDlss.StatusText).
enum Status {
  kNotTried = 0,
  kReady = 1,
  kNoD3D11 = -1,
  kInitFailed = -2,
  kNoCapabilities = -3,
  kUnsupported = -4,
  kDriverTooOld = -5,
  kCreateFailed = -6,
  kEvaluateFailed = -7,
  kLibraryMissing = -8,
};

struct Frame {
  ID3D11Resource* color = nullptr;
  ID3D11Resource* depth = nullptr;
  ID3D11Resource* motion = nullptr;
  ID3D11Resource* output = nullptr;
  float jitter_x = 0, jitter_y = 0;
  float mv_scale_x = 1, mv_scale_y = 1;
  int render_w = 0, render_h = 0, output_w = 0, output_h = 0;
  int quality = 0;
  int reset = 0;
  int depth_inverted = 1;
};

bool SameFeature(const Frame& a, const Frame& b) {
  return a.render_w == b.render_w && a.render_h == b.render_h && a.output_w == b.output_w &&
         a.output_h == b.output_h && a.quality == b.quality && a.depth_inverted == b.depth_inverted;
}

IUnityInterfaces* g_unity = nullptr;
IUnityGraphics* g_graphics = nullptr;
ID3D11Device* g_device = nullptr;
std::mutex g_mutex;
Frame g_frame[3];  // by kind
volatile int g_status[3] = {0, 0, 0};
volatile int g_last_result[3] = {0, 0, 0};
wchar_t g_plugin_dir[MAX_PATH] = L"";
wchar_t g_data_dir[MAX_PATH] = L"";

void Log(const char* fmt, ...) {
  char line[512];
  va_list args;
  va_start(args, fmt);
  vsnprintf(line, sizeof line, fmt, args);
  va_end(args);
  OutputDebugStringA(line);
  wchar_t path[MAX_PATH];
  swprintf(path, MAX_PATH, L"%s\\pc_upscalers.log", g_data_dir);
  FILE* f = nullptr;
  if (_wfopen_s(&f, path, L"a") == 0 && f) {
    fprintf(f, "%s\n", line);
    fclose(f);
  }
}

Frame TakeFrame(int kind) {
  std::lock_guard<std::mutex> lock(g_mutex);
  Frame f = g_frame[kind];
  g_frame[kind].reset = 0;
  return f;
}

bool Complete(const Frame& f) {
  return f.color && f.depth && f.motion && f.output && f.render_w > 0 && f.output_w > 0;
}

// ------------------------------------------------------------------------------------------------------ DLSS --

namespace dlss {

NVSDK_NGX_Parameter* params = nullptr;
NVSDK_NGX_Handle* feature = nullptr;
Frame feature_frame;

void ReleaseFeature() {
  if (feature) {
    NVSDK_NGX_D3D11_ReleaseFeature(feature);
    feature = nullptr;
  }
}

void Shutdown() {
  ReleaseFeature();
  if (params) {
    NVSDK_NGX_D3D11_DestroyParameters(params);
    params = nullptr;
  }
  if (g_device && g_status[kDlss] == kReady) NVSDK_NGX_D3D11_Shutdown1(g_device);
  g_status[kDlss] = kNotTried;
}

void Init() {
  if (g_status[kDlss] != kNotTried) return;
  if (!g_device) {
    g_status[kDlss] = kNoD3D11;
    return;
  }
  // NGX looks for nvngx_dlss.dll in the application folder first, then in these.
  const wchar_t* paths[] = {g_plugin_dir};
  NVSDK_NGX_FeatureCommonInfo info = {};
  info.PathListInfo.Path = paths;
  info.PathListInfo.Length = 1;
  NVSDK_NGX_Result r = NVSDK_NGX_D3D11_Init_with_ProjectID("6a3a5b1e-4f7c-4f3a-9b1e-a7e0c0de2026",
                                                          NVSDK_NGX_ENGINE_TYPE_CUSTOM, "1.0", g_data_dir,
                                                          g_device, &info);
  g_last_result[kDlss] = (int)r;
  if (NVSDK_NGX_FAILED(r)) {
    Log("NGX init failed: 0x%08x", (unsigned)r);
    g_status[kDlss] = kInitFailed;
    return;
  }
  r = NVSDK_NGX_D3D11_GetCapabilityParameters(&params);
  if (NVSDK_NGX_FAILED(r) || !params) {
    Log("NGX capability parameters failed: 0x%08x", (unsigned)r);
    g_status[kDlss] = kNoCapabilities;
    return;
  }
  int needs_driver = 0, available = 0;
  NVSDK_NGX_Parameter_GetI(params, NVSDK_NGX_Parameter_SuperSampling_NeedsUpdatedDriver, &needs_driver);
  NVSDK_NGX_Parameter_GetI(params, NVSDK_NGX_Parameter_SuperSampling_Available, &available);
  if (!available) {
    Log("DLSS not available (needs newer driver: %d)", needs_driver);
    g_status[kDlss] = needs_driver ? kDriverTooOld : kUnsupported;
    return;
  }
  Log("DLSS ready");
  g_status[kDlss] = kReady;
}

int OptimalSize(int quality, int output_w, int output_h, int* render_w, int* render_h) {
  if (g_status[kDlss] != kReady || !params) return 0;
  unsigned w = 0, h = 0, max_w = 0, max_h = 0, min_w = 0, min_h = 0;
  float sharpness = 0;
  NVSDK_NGX_Result r = NGX_DLSS_GET_OPTIMAL_SETTINGS(params, (unsigned)output_w, (unsigned)output_h,
                                                     (NVSDK_NGX_PerfQuality_Value)quality, &w, &h, &max_w, &max_h,
                                                     &min_w, &min_h, &sharpness);
  if (NVSDK_NGX_FAILED(r) || w == 0 || h == 0) return 0;
  *render_w = (int)w;
  *render_h = (int)h;
  return 1;
}

void Evaluate() {
  if (g_status[kDlss] != kReady) return;
  Frame f = TakeFrame(kDlss);
  if (!Complete(f)) return;
  ID3D11DeviceContext* ctx = nullptr;
  g_device->GetImmediateContext(&ctx);
  if (!ctx) return;
  if (!feature || !SameFeature(feature_frame, f)) {
    ReleaseFeature();
    NVSDK_NGX_DLSS_Create_Params create = {};
    create.Feature.InWidth = (unsigned)f.render_w;
    create.Feature.InHeight = (unsigned)f.render_h;
    create.Feature.InTargetWidth = (unsigned)f.output_w;
    create.Feature.InTargetHeight = (unsigned)f.output_h;
    create.Feature.InPerfQualityValue = (NVSDK_NGX_PerfQuality_Value)f.quality;
    create.InFeatureCreateFlags = NVSDK_NGX_DLSS_Feature_Flags_MVLowRes |
                                  (f.depth_inverted ? NVSDK_NGX_DLSS_Feature_Flags_DepthInverted : 0);
    NVSDK_NGX_Result r = NGX_D3D11_CREATE_DLSS_EXT(ctx, &feature, params, &create);
    g_last_result[kDlss] = (int)r;
    if (NVSDK_NGX_FAILED(r)) {
      Log("DLSS create failed: 0x%08x (%dx%d -> %dx%d, quality %d)", (unsigned)r, f.render_w, f.render_h,
          f.output_w, f.output_h, f.quality);
      feature = nullptr;
      g_status[kDlss] = kCreateFailed;
      ctx->Release();
      return;
    }
    Log("DLSS feature %dx%d -> %dx%d, quality %d", f.render_w, f.render_h, f.output_w, f.output_h, f.quality);
    feature_frame = f;
    f.reset = 1;
  }
  NVSDK_NGX_D3D11_DLSS_Eval_Params eval = {};
  eval.Feature.pInColor = f.color;
  eval.Feature.pInOutput = f.output;
  eval.pInDepth = f.depth;
  eval.pInMotionVectors = f.motion;
  eval.InJitterOffsetX = f.jitter_x;
  eval.InJitterOffsetY = f.jitter_y;
  eval.InRenderSubrectDimensions.Width = (unsigned)f.render_w;
  eval.InRenderSubrectDimensions.Height = (unsigned)f.render_h;
  eval.InReset = f.reset;
  eval.InMVScaleX = f.mv_scale_x;
  eval.InMVScaleY = f.mv_scale_y;
  NVSDK_NGX_Result r = NGX_D3D11_EVALUATE_DLSS_EXT(ctx, feature, params, &eval);
  g_last_result[kDlss] = (int)r;
  if (NVSDK_NGX_FAILED(r)) {
    Log("DLSS evaluate failed: 0x%08x", (unsigned)r);
    g_status[kDlss] = kEvaluateFailed;
  }
  ctx->Release();
}

}  // namespace dlss

// ------------------------------------------------------------------------------------------------------ XeSS --

namespace xess {

using CreateContextFn = xess_result_t (*)(ID3D11Device*, xess_context_handle_t*);
using InitFn = xess_result_t (*)(xess_context_handle_t, const xess_d3d11_init_params_t*);
using ExecuteFn = xess_result_t (*)(xess_context_handle_t, const xess_d3d11_execute_params_t*);
using OptimalFn = xess_result_t (*)(xess_context_handle_t, const xess_2d_t*, xess_quality_settings_t, xess_2d_t*,
                                    xess_2d_t*, xess_2d_t*);
using ScaleFn = xess_result_t (*)(xess_context_handle_t, float, float);
using DestroyFn = xess_result_t (*)(xess_context_handle_t);

HMODULE library = nullptr;
CreateContextFn create_context = nullptr;
InitFn init = nullptr;
ExecuteFn execute = nullptr;
OptimalFn optimal = nullptr;
ScaleFn set_velocity_scale = nullptr;
DestroyFn destroy = nullptr;
xess_context_handle_t context = nullptr;
bool initialised = false;
Frame init_frame;

void Shutdown() {
  if (context && destroy) destroy(context);
  context = nullptr;
  initialised = false;
  g_status[kXess] = kNotTried;
}

void Init() {
  if (g_status[kXess] != kNotTried) return;
  if (!g_device) {
    g_status[kXess] = kNoD3D11;
    return;
  }
  wchar_t path[MAX_PATH];
  swprintf(path, MAX_PATH, L"%s\\libxess_dx11.dll", g_plugin_dir);
  library = LoadLibraryW(path);
  if (!library) library = LoadLibraryW(L"libxess_dx11.dll");
  if (!library) {
    Log("XeSS: libxess_dx11.dll not found");
    g_status[kXess] = kLibraryMissing;
    return;
  }
  create_context = (CreateContextFn)GetProcAddress(library, "xessD3D11CreateContext");
  init = (InitFn)GetProcAddress(library, "xessD3D11Init");
  execute = (ExecuteFn)GetProcAddress(library, "xessD3D11Execute");
  optimal = (OptimalFn)GetProcAddress(library, "xessGetOptimalInputResolution");
  set_velocity_scale = (ScaleFn)GetProcAddress(library, "xessSetVelocityScale");
  destroy = (DestroyFn)GetProcAddress(library, "xessDestroyContext");
  if (!create_context || !init || !execute || !optimal || !destroy) {
    Log("XeSS: libxess_dx11.dll is missing functions");
    g_status[kXess] = kLibraryMissing;
    return;
  }
  xess_result_t r = create_context(g_device, &context);
  g_last_result[kXess] = (int)r;
  if (r != XESS_RESULT_SUCCESS) {
    Log("XeSS: context failed: %d", (int)r);
    g_status[kXess] = (r == XESS_RESULT_ERROR_UNSUPPORTED_DRIVER) ? kDriverTooOld : kUnsupported;
    context = nullptr;
    return;
  }
  Log("XeSS ready");
  g_status[kXess] = kReady;
}

int OptimalSize(int quality, int output_w, int output_h, int* render_w, int* render_h) {
  if (g_status[kXess] != kReady || !context) return 0;
  xess_2d_t out = {(uint32_t)output_w, (uint32_t)output_h}, best = {}, low = {}, high = {};
  if (optimal(context, &out, (xess_quality_settings_t)quality, &best, &low, &high) != XESS_RESULT_SUCCESS) return 0;
  *render_w = (int)best.x;
  *render_h = (int)best.y;
  return best.x > 0 && best.y > 0;
}

void Evaluate() {
  if (g_status[kXess] != kReady) return;
  Frame f = TakeFrame(kXess);
  if (!Complete(f)) return;
  if (!initialised || init_frame.output_w != f.output_w || init_frame.output_h != f.output_h ||
      init_frame.quality != f.quality || init_frame.depth_inverted != f.depth_inverted) {
    xess_d3d11_init_params_t params = {};
    params.outputResolution.x = (uint32_t)f.output_w;
    params.outputResolution.y = (uint32_t)f.output_h;
    params.qualitySetting = (xess_quality_settings_t)f.quality;
    params.initFlags = XESS_INIT_FLAG_LDR_INPUT_COLOR | (f.depth_inverted ? XESS_INIT_FLAG_INVERTED_DEPTH : 0);
    xess_result_t r = init(context, &params);
    g_last_result[kXess] = (int)r;
    if (r != XESS_RESULT_SUCCESS) {
      Log("XeSS init failed: %d", (int)r);
      g_status[kXess] = kCreateFailed;
      return;
    }
    Log("XeSS %dx%d, quality %d", f.output_w, f.output_h, f.quality);
    initialised = true;
    init_frame = f;
    f.reset = 1;
  }
  if (set_velocity_scale) set_velocity_scale(context, f.mv_scale_x, f.mv_scale_y);
  xess_d3d11_execute_params_t exec = {};
  exec.pColorTexture = f.color;
  exec.pVelocityTexture = f.motion;
  exec.pDepthTexture = f.depth;
  exec.pOutputTexture = f.output;
  exec.jitterOffsetX = f.jitter_x;
  exec.jitterOffsetY = f.jitter_y;
  exec.exposureScale = 1.0f;
  exec.resetHistory = (uint32_t)f.reset;
  exec.inputWidth = (uint32_t)f.render_w;
  exec.inputHeight = (uint32_t)f.render_h;
  xess_result_t r = execute(context, &exec);
  g_last_result[kXess] = (int)r;
  if (r != XESS_RESULT_SUCCESS) {
    Log("XeSS execute failed: %d", (int)r);
    g_status[kXess] = kEvaluateFailed;
  }
}

}  // namespace xess

// ----------------------------------------------------------------------------------------------------- Unity --

void UNITY_INTERFACE_API OnRenderEvent(int event) {
  const int kind = event >> 8;
  switch (event & 0xff) {
    case kEventInit:
      if (kind == kXess) xess::Init();
      else dlss::Init();
      break;
    case kEventEvaluate:
      if (kind == kXess) xess::Evaluate();
      else dlss::Evaluate();
      break;
    case kEventRelease:
      if (kind == kXess) xess::Shutdown();
      else dlss::ReleaseFeature();
      break;
  }
}

void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType type) {
  if (type == kUnityGfxDeviceEventInitialize) {
    if (g_graphics->GetRenderer() == kUnityGfxRendererD3D11) {
      IUnityGraphicsD3D11* d3d = g_unity->Get<IUnityGraphicsD3D11>();
      g_device = d3d ? d3d->GetDevice() : nullptr;
    }
  } else if (type == kUnityGfxDeviceEventShutdown) {
    dlss::Shutdown();
    xess::Shutdown();
    g_device = nullptr;
  }
}

int ValidKind(int kind) { return kind == kXess ? kXess : kDlss; }

}  // namespace

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
  if (reason == DLL_PROCESS_ATTACH) {
    GetModuleFileNameW(instance, g_plugin_dir, MAX_PATH);
    if (wchar_t* slash = wcsrchr(g_plugin_dir, L'\\')) *slash = 0;
    DWORD n = GetTempPathW(MAX_PATH, g_data_dir);
    if (n > 0 && g_data_dir[n - 1] == L'\\') g_data_dir[n - 1] = 0;
  }
  return TRUE;
}

extern "C" {

void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unity) {
  g_unity = unity;
  g_graphics = unity->Get<IUnityGraphics>();
  g_graphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
  OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
}

void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
  g_graphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
}

UNITY_INTERFACE_EXPORT UnityRenderingEvent UNITY_INTERFACE_API PCUp_GetRenderEventFunc() { return OnRenderEvent; }

UNITY_INTERFACE_EXPORT int UNITY_INTERFACE_API PCUp_Status(int kind) { return g_status[ValidKind(kind)]; }

UNITY_INTERFACE_EXPORT int UNITY_INTERFACE_API PCUp_LastResult(int kind) { return g_last_result[ValidKind(kind)]; }

// The render size the upscaler wants for an output size and quality (0 when it is not ready). quality is
// NVSDK_NGX_PerfQuality_Value for DLSS and xess_quality_settings_t for XeSS.
UNITY_INTERFACE_EXPORT int UNITY_INTERFACE_API PCUp_OptimalSize(int kind, int quality, int output_w, int output_h,
                                                                int* render_w, int* render_h) {
  *render_w = *render_h = 0;
  return ValidKind(kind) == kXess ? xess::OptimalSize(quality, output_w, output_h, render_w, render_h)
                                  : dlss::OptimalSize(quality, output_w, output_h, render_w, render_h);
}

UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API PCUp_SetFrame(int kind, void* color, void* depth, void* motion,
                                                              void* output, float jitter_x, float jitter_y,
                                                              float mv_scale_x, float mv_scale_y, int render_w,
                                                              int render_h, int output_w, int output_h, int quality,
                                                              int reset, int depth_inverted) {
  std::lock_guard<std::mutex> lock(g_mutex);
  Frame& f = g_frame[ValidKind(kind)];
  f.color = (ID3D11Resource*)color;
  f.depth = (ID3D11Resource*)depth;
  f.motion = (ID3D11Resource*)motion;
  f.output = (ID3D11Resource*)output;
  f.jitter_x = jitter_x;
  f.jitter_y = jitter_y;
  f.mv_scale_x = mv_scale_x;
  f.mv_scale_y = mv_scale_y;
  f.render_w = render_w;
  f.render_h = render_h;
  f.output_w = output_w;
  f.output_h = output_h;
  f.quality = quality;
  f.reset |= reset;
  f.depth_inverted = depth_inverted;
}

}  // extern "C"
