// AVP Evolution: PC Edition, as a TRG Launcher 2.0 project. Everything specific to the port is in this file; the
// dashboard, Android installs and settings screens come from trg-launcher (launcher2/README.md).
//
// The game reads its settings from games/avp_evolution/avp_evolution.toml (the avp_ keys below) when it starts:
// see AVPE_Play/ExportedProject/Assets/Plugins/PCPort/PCSettings.cs.

#include <algorithm>
#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <functional>
#include <string>
#include <system_error>
#include <thread>
#include <vector>

#include <trg/download.h>
#include <trg/hub.h>
#include <trg/platform.h>

#include <windows.h>
#include <bcrypt.h>
#include <shellapi.h>

#include "logo_png.h"

namespace fs = std::filesystem;

namespace {

std::string Utf8(const fs::path& p) {
  const auto s = p.u8string();
  return std::string(s.begin(), s.end());
}

std::wstring Wide(const std::string& s) {
  if (s.empty()) return std::wstring();
  std::wstring w(MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0), L'\0');
  MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), w.data(), (int)w.size());
  return w;
}

fs::path ExeDir() {
  wchar_t buf[MAX_PATH] = L"";
  GetModuleFileNameW(nullptr, buf, MAX_PATH);
  return fs::path(buf).parent_path();
}

// For development: AVP_PAYLOAD names an already built game, which the install copies instead of building one.
std::string CopyPayload(trg::Task& task, const fs::path& from, const fs::path& dest) {
  std::error_code ec;
  if (!fs::exists(from / "AVPEvolution.exe", ec)) return "AVP_PAYLOAD has no AVPEvolution.exe: " + Utf8(from);
  long long total = 0, done = 0;
  std::vector<fs::path> files;
  for (fs::recursive_directory_iterator it(from, ec), end; !ec && it != end; it.increment(ec)) {
    if (!it->is_regular_file(ec)) continue;
    files.push_back(it->path());
    total += (long long)it->file_size(ec);
  }
  task.SetLabel("Installing AVP Evolution");
  for (const fs::path& f : files) {
    if (task.cancelled()) return "Cancelled.";
    const fs::path rel = fs::relative(f, from, ec);
    fs::create_directories((dest / rel).parent_path(), ec);
    fs::copy_file(f, dest / rel, fs::copy_options::overwrite_existing, ec);
    if (ec) return "Could not copy " + Utf8(rel) + ": " + ec.message();
    done += (long long)fs::file_size(f, ec);
    task.Progress(done, total);
  }
  return std::string();
}

// ------------------------------------------------------------- converter ---
// The install builds the game on this PC from the player's own .apk and .obb: converter\convert.py, beside the
// launcher, exports the game with AssetRipper and builds it with Unity 5.6.7f1. Nothing from the game comes with the
// launcher. The tools are downloaded into converter\tools the first time, each checked against its SHA-256.

struct Download {
  const char* file;
  const char* url;
  const char* sha256;
  const char* what;  // for the progress label
};

const Download kPython = {"python-3.12.10-embed-amd64.zip",
                          "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip",
                          "4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3", "Python"};
const Download kPillow = {
    "pillow-12.3.0-cp312-cp312-win_amd64.whl",
    "https://files.pythonhosted.org/packages/45/89/da2f7971a317f83d807fdd4065c0af40208e59e692cc43d315a71a0e96d1/"
    "pillow-12.3.0-cp312-cp312-win_amd64.whl",
    "a2b55dd6b2a4c4b7d87ffa56bdb33fdc5fdb9a462173861a7bc097f17d91cb09", "Pillow"};
const Download kDecoder = {
    "texture2ddecoder-1.0.6-cp311-abi3-win_amd64.whl",
    "https://files.pythonhosted.org/packages/ac/f8/cbbefcb1b3435aea8ef1b3d65622c933f79ea7c70af4d4571f667277cc86/"
    "texture2ddecoder-1.0.6-cp311-abi3-win_amd64.whl",
    "1bad8209d9dfbfc7daf13ea2e545236ea2bbc1e423b2992b6e7563909975a47b", "the texture decoder"};
const Download kAssetRipper = {"AssetRipper_win_x64.zip",
                               "https://github.com/AssetRipper/AssetRipper/releases/download/2.0.0/AssetRipper_win_x64.zip",
                               "9a7ef0e7c5c3ea5b90b4e6d855e2d98d5f7ec8c3f9e26fccbc194c6a7b01baf7", "AssetRipper 2.0.0"};
const Download kShaders = {"builtin_shaders-5.6.7f1.zip",
                           "https://download.unity3d.com/download_unity/e80cc3114ac1/builtin_shaders-5.6.7f1.zip",
                           "082ca6e84a5f079101e12e4158b4ea8fbeeb992a966d6cb88493bee86518abd6", "Unity's shaders"};
const Download kUnity = {
    "UnitySetup64-5.6.7f1.exe",
    "https://download.unity3d.com/download_unity/e80cc3114ac1/Windows64EditorInstaller/UnitySetup64-5.6.7f1.exe",
    "918fcd42a2c8eb7984972508e1d8dab693f9e7721cae7642ec60aa4e99c72d12", "Unity 5.6.7f1 (about 570 MB)"};

// AVP_CONVERTER overrides the folder.
fs::path ConverterDir() {
  wchar_t buf[MAX_PATH] = L"";
  if (GetEnvironmentVariableW(L"AVP_CONVERTER", buf, MAX_PATH) > 0) return fs::path(buf);
  return ExeDir() / "converter";
}

std::string Sha256(const fs::path& file) {
  FILE* f = trg::OpenFile(Utf8(file), "rb");
  if (!f) return std::string();
  BCRYPT_ALG_HANDLE alg = nullptr;
  BCRYPT_HASH_HANDLE hash = nullptr;
  std::string hex;
  if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, nullptr, 0) == 0 &&
      BCryptCreateHash(alg, &hash, nullptr, 0, nullptr, 0, 0) == 0) {
    std::vector<unsigned char> buf(1 << 20);
    size_t n;
    while ((n = fread(buf.data(), 1, buf.size(), f)) > 0) BCryptHashData(hash, buf.data(), (ULONG)n, 0);
    unsigned char digest[32];
    if (BCryptFinishHash(hash, digest, sizeof digest, 0) == 0) {
      static const char* kHex = "0123456789abcdef";
      for (unsigned char b : digest) {
        hex += kHex[b >> 4];
        hex += kHex[b & 15];
      }
    }
  }
  if (hash) BCryptDestroyHash(hash);
  if (alg) BCryptCloseAlgorithmProvider(alg, 0);
  fclose(f);
  return hex;
}

// Downloads one file into converter\tools\downloads, or keeps the copy already there if its hash is right.
std::string Fetch(trg::Task& task, const Download& d, fs::path* out) {
  const fs::path dir = ConverterDir() / "tools" / "downloads";
  std::error_code ec;
  fs::create_directories(dir, ec);
  *out = dir / d.file;
  if (fs::exists(*out, ec) && Sha256(*out) == d.sha256) return std::string();
  task.SetLabel(std::string("Downloading ") + d.what);
  const std::string err = trg::DownloadFile(task, d.url, Utf8(*out));
  if (task.cancelled()) return "Cancelled.";
  if (!err.empty()) return std::string("Could not download ") + d.what + ": " + err;
  task.SetLabel(std::string("Checking ") + d.what);
  if (Sha256(*out) != d.sha256) {
    fs::remove(*out, ec);
    return std::string("The download of ") + d.what + " wasn't the expected file. Try again later.";
  }
  return std::string();
}

// Runs a program with no window and waits. Each line it prints goes to `line`. Cancel stops it and everything it
// started: a job object holds the whole process tree.
int RunHidden(trg::Task* task, const std::wstring& exe, std::wstring cmd, const fs::path& cwd,
              const std::function<void(const std::string&)>& line) {
  SECURITY_ATTRIBUTES sa{sizeof(sa), nullptr, TRUE};
  HANDLE read = nullptr, write = nullptr;
  if (!CreatePipe(&read, &write, &sa, 0)) return -1;
  SetHandleInformation(read, HANDLE_FLAG_INHERIT, 0);
  STARTUPINFOW si{};
  si.cb = sizeof(si);
  si.dwFlags = STARTF_USESTDHANDLES;
  si.hStdOutput = write;
  si.hStdError = write;
  si.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
  PROCESS_INFORMATION pi{};
  HANDLE job = CreateJobObjectW(nullptr, nullptr);
  JOBOBJECT_EXTENDED_LIMIT_INFORMATION limit{};
  limit.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
  SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limit, sizeof limit);
  const BOOL ok = CreateProcessW(exe.c_str(), cmd.data(), nullptr, nullptr, TRUE, CREATE_NO_WINDOW | CREATE_SUSPENDED,
                                 nullptr, cwd.empty() ? nullptr : cwd.c_str(), &si, &pi);
  CloseHandle(write);
  if (!ok) {
    CloseHandle(read);
    CloseHandle(job);
    return -1;
  }
  AssignProcessToJobObject(job, pi.hProcess);
  ResumeThread(pi.hThread);
  std::string pending;
  char buf[4096];
  bool killed = false;
  for (;;) {
    if (task && task->cancelled() && !killed) {
      TerminateJobObject(job, 1);
      killed = true;
    }
    DWORD avail = 0;
    if (!PeekNamedPipe(read, nullptr, 0, nullptr, &avail, nullptr)) break;  // every writer has closed the pipe
    if (avail == 0) {
      // A program it started may still hold the pipe after it has ended.
      if (WaitForSingleObject(pi.hProcess, 50) == WAIT_OBJECT_0 &&
          (!PeekNamedPipe(read, nullptr, 0, nullptr, &avail, nullptr) || avail == 0))
        break;
      continue;
    }
    DWORD n = 0;
    if (!ReadFile(read, buf, sizeof buf, &n, nullptr) || n == 0) break;
    pending.append(buf, n);
    size_t at;
    while ((at = pending.find('\n')) != std::string::npos) {
      std::string l = pending.substr(0, at);
      if (!l.empty() && l.back() == '\r') l.pop_back();
      if (line) line(l);
      pending.erase(0, at + 1);
    }
  }
  if (!pending.empty() && line) line(pending);
  WaitForSingleObject(pi.hProcess, INFINITE);
  DWORD code = 1;
  GetExitCodeProcess(pi.hProcess, &code);
  CloseHandle(pi.hThread);
  CloseHandle(pi.hProcess);
  CloseHandle(read);
  CloseHandle(job);
  return killed ? -2 : (int)code;
}

std::string Unpack(trg::Task& task, const fs::path& zip, const fs::path& into) {
  std::error_code ec;
  fs::create_directories(into, ec);
  wchar_t sys[MAX_PATH] = L"";
  GetSystemDirectoryW(sys, MAX_PATH);
  const std::wstring tar = std::wstring(sys) + L"\\tar.exe";
  const int code = RunHidden(&task, tar, L"\"" + tar + L"\" -xf \"" + zip.wstring() + L"\" -C \"" + into.wstring() + L"\"",
                             fs::path(), nullptr);
  return code == 0 ? std::string() : "Could not unpack " + Utf8(zip.filename());
}

// Python with the two packages the converter uses. The embeddable Python only looks where its ._pth file says, so
// Lib\site-packages is added there.
std::string SetUpPython(trg::Task& task, const fs::path& dir) {
  std::error_code ec;
  if (fs::exists(dir / "python.exe", ec) && fs::exists(dir / "Lib" / "site-packages" / "PIL", ec) &&
      fs::exists(dir / "Lib" / "site-packages" / "texture2ddecoder", ec))
    return std::string();
  fs::path zip, pillow, decoder;
  std::string err = Fetch(task, kPython, &zip);
  if (err.empty()) err = Fetch(task, kPillow, &pillow);
  if (err.empty()) err = Fetch(task, kDecoder, &decoder);
  if (!err.empty()) return err;
  task.SetLabel("Setting up Python");
  err = Unpack(task, zip, dir);
  if (err.empty()) err = Unpack(task, pillow, dir / "Lib" / "site-packages");
  if (err.empty()) err = Unpack(task, decoder, dir / "Lib" / "site-packages");
  if (!err.empty()) return err;
  FILE* f = trg::OpenFile(Utf8(dir / "python312._pth"), "wb");
  if (!f) return "Could not set up Python in " + Utf8(dir);
  fputs("python312.zip\n.\nLib\\site-packages\n", f);
  fclose(f);
  return std::string();
}

std::string SetUpZip(trg::Task& task, const Download& d, const fs::path& dir, const char* marker) {
  std::error_code ec;
  if (fs::exists(dir / marker, ec)) return std::string();
  fs::path zip;
  const std::string err = Fetch(task, d, &zip);
  if (!err.empty()) return err;
  task.SetLabel(std::string("Unpacking ") + d.what);
  return Unpack(task, zip, dir);
}

// Unity 5.6.7f1: one already installed in the usual places, else its installer, run silently into converter\tools.
// The installer asks Windows for permission.
std::string FindOrInstallUnity(trg::Task& task, const fs::path& tools, fs::path* unity) {
  std::error_code ec;
  std::vector<fs::path> places = {tools / "Unity567" / "Editor" / "Unity.exe"};
  wchar_t pf[MAX_PATH] = L"";
  if (GetEnvironmentVariableW(L"ProgramFiles", pf, MAX_PATH) > 0) {
    places.push_back(fs::path(pf) / "Unity 5.6.7f1" / "Editor" / "Unity.exe");
    places.push_back(fs::path(pf) / "Unity" / "Hub" / "Editor" / "5.6.7f1" / "Editor" / "Unity.exe");
  }
  for (const fs::path& p : places)
    if (fs::exists(p, ec)) {
      *unity = p;
      return std::string();
    }
  fs::path setup;
  const std::string err = Fetch(task, kUnity, &setup);
  if (!err.empty()) return err;
  task.SetLabel("Installing Unity 5.6.7f1: allow it when Windows asks");
  task.Progress(0, -1);
  const std::wstring args = L"/S /D=" + (tools / "Unity567").wstring();
  SHELLEXECUTEINFOW sei{};
  sei.cbSize = sizeof(sei);
  sei.fMask = SEE_MASK_NOCLOSEPROCESS;
  sei.lpVerb = L"runas";
  sei.lpFile = setup.c_str();
  sei.lpParameters = args.c_str();
  sei.nShow = SW_HIDE;
  if (!ShellExecuteExW(&sei) || !sei.hProcess)
    return "Unity 5.6.7f1 wasn't installed: Windows didn't allow its installer to run. Choose Install again and "
           "allow it.";
  WaitForSingleObject(sei.hProcess, INFINITE);
  CloseHandle(sei.hProcess);
  if (!fs::exists(places[0], ec))
    return "Unity 5.6.7f1's installer finished, but Unity wasn't found in " + Utf8(tools / "Unity567") + ".";
  *unity = places[0];
  return std::string();
}

// Unity only builds once it has a licence, and a free Personal one needs a Unity account. Without one, Unity is
// opened for the player to sign in.
std::string CheckUnityLicence(const fs::path& unity) {
  wchar_t pd[MAX_PATH] = L"";
  GetEnvironmentVariableW(L"ProgramData", pd, MAX_PATH);
  std::error_code ec;
  if (fs::exists(fs::path(pd) / "Unity" / "Unity_v5.x.ulf", ec)) return std::string();
  ShellExecuteW(nullptr, L"open", unity.c_str(), nullptr, unity.parent_path().c_str(), SW_SHOWNORMAL);
  return "Unity needs a licence before it can build the game. Unity is opening: sign in with a free Unity account, "
         "choose Unity Personal, then close Unity and choose Install again.";
}

// What each of the converter's stages is called on screen, and about how long it takes, for the progress bar.
struct Stage {
  const char* name;
  const char* label;
  int seconds;
};
const Stage kStages[] = {
    {"merge", "Unpacking your copy of the game", 10},
    {"export", "Exporting the game with AssetRipper", 100},
    {"aperture", "Exporting the cutscenes", 100},
    {"patch", "Applying the PC port's changes", 2},
    {"rewired", "Applying the PC port's changes", 1},
    {"cleanup", "Applying the PC port's changes", 1},
    {"plugins", "Applying the PC port's changes", 1},
    {"shaders", "Adding the shaders", 2},
    {"clips", "Restoring the animations", 1},
    {"textures", "Converting the textures", 5},
    {"port", "Adding the PC port", 2},
    {"unity", "Building the game with Unity: the long part, about 25 minutes", 1500},
    {"art", "Making the launcher's pictures", 1},
};

std::string InstallFromApk(trg::Task& task, const trg::hub::ApkInstall& in) {
  const fs::path dest = fs::u8path(in.dest);
  wchar_t buf[MAX_PATH] = L"";
  if (GetEnvironmentVariableW(L"AVP_PAYLOAD", buf, MAX_PATH) > 0) return CopyPayload(task, fs::path(buf), dest);

  const fs::path conv = ConverterDir();
  const fs::path tools = conv / "tools";
  std::error_code ec;
  if (!fs::exists(conv / "convert.py", ec) || !fs::exists(conv / "pack" / "port", ec))
    return "The converter wasn't found beside the launcher (the converter folder). Download the launcher again.";

  // The work folder holds the exported project, about 7.5 GB, and goes once the game is built. Unity needs about
  // 2 GB more the first time, and the game 1.5 GB.
  const fs::path work = dest.parent_path() / "avp_evolution_build";
  ULARGE_INTEGER free_bytes{};
  if (!fs::exists(work, ec) && GetDiskFreeSpaceExW(dest.parent_path().c_str(), &free_bytes, nullptr, nullptr) &&
      free_bytes.QuadPart < 12ull * 1024 * 1024 * 1024)
    return "Building the game needs about 12 GB of free space on this drive while it runs, and 1.5 GB once it's "
           "done. Free some space, then try again.";

  std::string err = SetUpPython(task, tools / "python");
  if (err.empty()) err = SetUpZip(task, kAssetRipper, tools / "AssetRipper", "AssetRipper.GUI.Free.exe");
  if (err.empty()) err = SetUpZip(task, kShaders, tools / "builtin_shaders", "CGIncludes");
  fs::path unity;
  if (err.empty()) err = FindOrInstallUnity(task, tools, &unity);
  if (err.empty()) err = CheckUnityLicence(unity);
  if (!err.empty()) return err;
  if (task.cancelled()) return "Cancelled.";

  fs::create_directories(work, ec);
  FILE* log = trg::OpenFile(Utf8(work / "convert.log"), "ab");
  long long total = 0;
  for (const Stage& s : kStages) total += s.seconds * 1000LL;
  std::atomic<long long> before{0};  // the expected milliseconds of the stages already done
  std::atomic<int> expected{0};
  std::atomic<DWORD> started{GetTickCount()};
  std::atomic<bool> running{true};
  std::string last;
  task.SetLabel("Starting the converter");
  task.Progress(0, total);
  const fs::path python = tools / "python" / "python.exe";
  std::wstring cmd = L"\"" + python.wstring() + L"\" -u \"" + (conv / "convert.py").wstring() + L"\" --apk \"" +
                     Wide(in.apk) + L"\" --obb \"" + Wide(in.obb) + L"\" --work \"" + work.wstring() + L"\" --out \"" +
                     dest.wstring() + L"\" --tools \"" + tools.wstring() + L"\" --unity \"" + unity.wstring() + L"\"";
  SetEnvironmentVariableW(L"PYTHONIOENCODING", L"utf-8");
  // Unity prints nothing for 25 minutes, so the bar moves with the time each stage usually takes.
  std::thread ticker([&] {
    while (running) {
      const long long in_stage = std::min<long long>(GetTickCount() - started, expected * 950LL);
      task.Progress(before + in_stage, total);
      Sleep(500);
    }
  });
  const int code = RunHidden(&task, python.wstring(), cmd, conv, [&](const std::string& l) {
    if (log) {
      fprintf(log, "%s\n", l.c_str());
      fflush(log);
    }
    if (l.rfind("STAGE ", 0) == 0) {
      const std::string name = l.substr(6);
      before += expected * 1000LL;
      int next = 0;
      for (const Stage& s : kStages)
        if (name == s.name) {
          task.SetLabel(s.label);
          next = s.seconds;
        }
      expected = next;
      started = GetTickCount();
    } else if (!l.empty()) {
      last = l;
    }
  });
  running = false;
  ticker.join();
  if (log) fclose(log);
  if (code == -2 || task.cancelled()) return "Cancelled. What's done is kept: choose Install again to carry on.";
  if (code != 0)
    return "The game couldn't be built: " + (last.empty() ? std::string("the converter didn't start") : last) +
           ". The details are in " + Utf8(work / "convert.log") + ".";
  task.SetLabel("Removing the work files");
  task.Progress(0, -1);
  fs::remove_all(work, ec);
  return std::string();
}

std::vector<trg::hub::SettingsSection> GameSettings(int) {
  using Rows = trg::hub::SettingsRows;
  std::vector<trg::hub::SettingsSection> s;

  s.push_back({"Display", [](Rows& r) {
    r.Choice("Window mode", "Fullscreen fills your screen. Borderless is a window the size of the screen, quick to "
             "switch out of. Windowed opens a window you can move.", "avp_window_mode", "fullscreen",
             {{"fullscreen", "Fullscreen"}, {"borderless", "Borderless"}, {"windowed", "Windowed"}});
    r.Choice("Resolution", "The size the game draws at. Native uses your screen's own size.", "avp_resolution",
             "native", {{"native", "Native"}, {"1280x720", "1280 x 720"}, {"1600x900", "1600 x 900"},
                        {"1920x1080", "1920 x 1080"}, {"2560x1080", "2560 x 1080"}, {"2560x1440", "2560 x 1440"},
                        {"3440x1440", "3440 x 1440"}, {"3840x2160", "3840 x 2160"}, {"5120x1440", "5120 x 1440"}});
    r.Toggle("V-Sync", "Waits for your screen between frames: no tearing, but a little more input lag.",
             "avp_vsync", true);
    r.Choice("Frame rate limit", "The phone game ran at 30 frames a second. Higher is smoother; Unlimited runs as "
             "fast as your PC allows.", "avp_frame_limit", "60",
             {{"30", "30"}, {"60", "60"}, {"120", "120"}, {"144", "144"}, {"165", "165"}, {"240", "240"},
              {"0", "Unlimited"}});
  }});

  s.push_back({"Graphics", [](Rows& r) {
    // Presets set every row below. Changing a row afterwards makes the preset Custom.
    struct Preset {
      const char* id;
      std::vector<std::pair<const char*, const char*>> values;
    };
    static const std::vector<Preset> presets = {
        {"original", {{"avp_aa", "off"}, {"avp_aniso", "1"}, {"avp_bloom", "false"}, {"avp_ssao", "off"},
                      {"avp_tonemap", "original"}, {"avp_auto_exposure", "false"}, {"avp_grading", "false"}, {"avp_shadows", "off"},
                      {"avp_light_shafts", "false"}, {"avp_draw_distance", "original"},
                      {"avp_enhanced_lighting", "false"}}},
        {"balanced", {{"avp_aa", "smaa"}, {"avp_aniso", "8"}, {"avp_bloom", "true"}, {"avp_ssao", "off"},
                      {"avp_tonemap", "aces"}, {"avp_auto_exposure", "true"}, {"avp_grading", "true"}, {"avp_shadows", "low"},
                      {"avp_light_shafts", "false"}, {"avp_draw_distance", "far"},
                      {"avp_enhanced_lighting", "false"}}},
        {"enhanced", {{"avp_aa", "smaa"}, {"avp_aniso", "16"}, {"avp_bloom", "true"}, {"avp_ssao", "low"},
                      {"avp_tonemap", "aces"}, {"avp_auto_exposure", "true"}, {"avp_grading", "true"}, {"avp_shadows", "high"},
                      {"avp_light_shafts", "true"}, {"avp_draw_distance", "far"},
                      {"avp_enhanced_lighting", "true"}}},
        {"ultra", {{"avp_aa", "taa"}, {"avp_aniso", "16"}, {"avp_bloom", "true"}, {"avp_ssao", "high"},
                   {"avp_tonemap", "aces"}, {"avp_auto_exposure", "true"}, {"avp_grading", "true"}, {"avp_shadows", "ultra"},
                   {"avp_light_shafts", "true"}, {"avp_draw_distance", "max"}, {"avp_enhanced_lighting", "true"}}},
    };
    if (r.Choice("Preset", "Sets every graphics option at once. Original looks like the phone game; Ultra adds every "
                 "modern effect. Change any option below to make your own mix.", "avp_preset", "enhanced",
                 {{"original", "Original"}, {"balanced", "Balanced"}, {"enhanced", "Enhanced"}, {"ultra", "Ultra"},
                  {"custom", "Custom"}})) {
      const std::string id = r.store().Get("avp_preset", "enhanced");
      for (const Preset& p : presets)
        if (id == p.id)
          for (const auto& [k, v] : p.values) r.store().Set(k, v);
      r.store().Save();
    }
    bool changed = false;
    r.Heading("UPSCALING");
    changed |= r.Choice("Upscaler", "Draws fewer pixels and rebuilds a sharp image at your screen's size, for higher "
                        "frame rates. FSR 1 works on every graphics card; DLSS needs an NVIDIA RTX card and XeSS an "
                        "Intel Arc card (the game uses FSR 1 when yours cannot). Off draws every pixel.",
                        "avp_upscaler", "off",
                        {{"off", "Off"}, {"fsr1", "AMD FSR 1"}, {"dlss", "NVIDIA DLSS"}, {"xess", "Intel XeSS"}});
    changed |= r.Choice("Upscaler quality", "How much smaller the game draws before upscaling. Native keeps your "
                        "screen's size and uses DLSS or XeSS only to smooth edges (DLAA).",
                        "avp_upscaler_quality", "quality",
                        {{"native", "Native"}, {"ultra", "Ultra quality"}, {"quality", "Quality"},
                         {"balanced", "Balanced"}, {"performance", "Performance"}});
    changed |= r.SliderInt("Sharpening", "Restores fine detail after upscaling.", "avp_sharpness", 40, 0, 100, 5,
                           "%d%%");
    r.Heading("IMAGE");
    changed |= r.Choice("Anti-aliasing", "Smooths jagged edges. SMAA is sharp and cheap; TAA is the smoothest, "
                        "including shimmering detail.", "avp_aa", "smaa",
                        {{"off", "Off"}, {"fxaa", "FXAA"}, {"smaa", "SMAA"}, {"taa", "TAA"}});
    changed |= r.Choice("Texture filtering", "Keeps floors and walls sharp when seen at an angle.", "avp_aniso", "16",
                        {{"1", "Off"}, {"2", "2x"}, {"4", "4x"}, {"8", "8x"}, {"16", "16x"}});
    changed |= r.Choice("Tone mapping", "ACES gives a filmic range to the dark corridors and bright lights. "
                        "Original keeps the phone look.", "avp_tonemap", "aces",
                        {{"original", "Original"}, {"aces", "ACES filmic"}});
    changed |= r.Toggle("Campaign colour grading", "A cold blue-green grade for the Alien and Marines, a hotter "
                        "amber one for the Predator.", "avp_grading", true);
    r.Heading("LIGHTING");
    changed |= r.Toggle("Auto exposure", "With ACES: the darkest areas open up a little as your eyes would, and bright "
                        "ones stay as they are.", "avp_auto_exposure", true);
    changed |= r.Toggle("Bloom", "Lights, plasma and Predator tech glow.", "avp_bloom", true);
    changed |= r.Choice("Ambient occlusion", "Soft contact shadows in corners and around props.", "avp_ssao", "low",
                        {{"off", "Off"}, {"low", "Low"}, {"high", "High"}});
    changed |= r.Choice("Character shadows", "Real-time shadows from the main light on Aliens, Predators and "
                        "Marines, instead of the phone game's blobs.", "avp_shadows", "high",
                        {{"off", "Off"}, {"low", "Low"}, {"high", "High"}, {"ultra", "Ultra"}});
    changed |= r.Toggle("Light shafts", "Volumetric shafts from the scene's main lights.", "avp_light_shafts", true);
    changed |= r.Toggle("Enhanced lighting", "Per-pixel lighting with specular highlights and rim light on "
                        "characters and props.", "avp_enhanced_lighting", true);
    changed |= r.Choice("Draw distance", "How far away detail and effects are kept. The phone game hid things to "
                        "save power.", "avp_draw_distance", "far",
                        {{"original", "Original"}, {"far", "Far"}, {"max", "Maximum"}});
    if (changed && r.store().Get("avp_preset", "enhanced") != "custom") {
      r.store().Set("avp_preset", "custom");
      r.store().Save();
    }
  }});

  s.push_back({"Camera and HUD", [](Rows& r) {
    r.SliderInt("Field of view", "How much of the world the camera shows. The phone game used 60.", "avp_fov", 65, 50,
                100, 1, "%d");
    r.Toggle("Keep HUD at the screen edges", "On wide and ultrawide screens the health bar and buttons stay in the "
             "corners instead of the middle 16:9 area.", "avp_hud_edges", true);
    r.SliderInt("HUD size", "How large the on-screen display is.", "avp_hud_scale", 100, 50, 150, 5, "%d%%");
    r.Choice("Button prompts", "Which buttons the game shows. Automatic follows the last controller or keyboard "
             "you used.", "avp_glyphs", "auto",
             {{"auto", "Automatic"}, {"xbox", "Xbox"}, {"playstation", "PlayStation"}, {"keyboard", "Keyboard"}});
  }});

  s.push_back({"Controls", [](Rows& r) {
    r.SliderFloat("Mouse sensitivity", "How fast the mouse turns the camera.", "avp_mouse_sensitivity", 1.0f, 0.1f,
                  4.0f, 0.1f, "%.1f");
    r.SliderFloat("Stick sensitivity", "How fast the right stick turns the camera.", "avp_stick_sensitivity", 1.0f,
                  0.1f, 4.0f, 0.1f, "%.1f");
    r.Toggle("Invert vertical look", "Moving up looks down.", "avp_invert_y", false);
    r.Toggle("Capture the mouse", "While a level is played, the mouse turns the camera without stopping at the "
             "screen's edge. Menus and the pause screen free it.", "avp_mouse_capture", true);
    r.Toggle("Controller rumble", "Rumble on Xbox-style controllers: a small pulse when you strike, a heavy one when "
             "you are hit, and both sides when you slice an enemy.", "avp_rumble", true);
    // The game's Rewired keyboard map plus the keys the port adds (Assets/PCPort/PCKeys.cs); F1 shows them in game.
    r.Heading("KEYS AND BUTTONS");
    r.Paragraph("Press F1 in a level to see these in the game. The controller names are the Xbox ones.");
    r.Info("Move / camera", "W A S D, mouse  |  left stick, right stick");
    r.Info("Attack / use", "Left mouse  |  X");
    r.Info("Jump / evade", "Right mouse or Space  |  A");
    r.Info("Ranged / lock on", "F  |  Y");
    r.Info("Execution", "X  |  B");
    r.Info("Special abilities", "R, G  |  LB, RB, LT");
    r.Info("Weapons (Predator)", "Q, E  |  D-pad up, down");
    r.Info("Thermal / cloak", "T, C  |  D-pad left, right");
    r.Info("Switch attacks / rage (Alien)", "Q, E  |  D-pad");
    r.Info("Show the way / centre camera", "Tab, V  |  stick clicks");
    r.Info("Pause", "Esc  |  Menu or View");
    r.Info("Back to the launcher", "Hold Esc  |  hold View and Menu");
  }});
  return s;
}

trg::hub::ProjectConfig Avp() {
  using trg::hub::GameInfo;
  using trg::hub::InstallSource;

  trg::hub::ProjectConfig p;
  p.name = "AVP Evolution: PC Edition";
  p.id = "avp_evolution_pc";
  p.version = PROJECT_VERSION;
  p.home_line = "The 2013 Android game, rebuilt for PC";
  p.about = "AVP Evolution, the 2013 mobile action game, for PC with modern graphics, keyboard and mouse and "
            "controllers. It needs your own copy of the Android game (the .apk and its .obb from your purchase).";
  p.made_by = "Angry Mob Games for Fox Digital Entertainment, 2013";
  p.console = "Android";
  p.region = "";
  p.logo_png = kAvpLogoPng;
  p.logo_png_size = sizeof kAvpLogoPng;
  p.port_kind = "porting the Android game to PC (Unity 5)";

  GameInfo avp = {
      "avp_evolution", "AVP Evolution", "AVPEvolution.exe", "AVPEvolution", 0,
      ImVec4(0.12f, 0.62f, 0.86f, 1.0f), "Play as the Predator and the Alien.", InstallSource::kApk,
      // No SteamGridDB art for this mobile game: the install puts the game's own art in launcher_art/.
      900131, "local:launcher_art/grid.png", "local:launcher_art/hero.png", "local:launcher_art/logo.png",
      "2.1 (808)", "com.fde.avpevolution_2.1.apk", "29,150,810 bytes", "BF9F6CE40C350D4E383B544C51803FB650BC2F01",
      "6C1AD1A1BBAB152E7DDBE5397CDC8146", "AD75ECCC", "", "about 2.4 GB"};
  avp.installed_marker = "AVPEvolution_Data/globalgamemanagers";
  avp.android_package = "com.fde.avpevolution";
  avp.needs_obb = true;
  avp.copy_obb = "main.808.com.fde.avpevolution.obb, 494,458,650 bytes";
  avp.install_apk = InstallFromApk;
  p.games = {avp};

  // Local for now: no releases repo, so the program comes with the install and there is no Updates section.
  p.repo = "";
  p.launcher_zip = "AVPEvolutionPC";
  p.launcher_exe = "avp_launcher";
  p.linux_data_folder = "avp_evolution_pc";
  p.setting_prefix = "avp_";
  p.from_launcher_flag = "--avp_from_launcher";
  p.test_background_env = "AVP_TEST_BACKGROUND";
  p.release_server_env = "AVP_RELEASE_SERVER";
  p.features.frame_rate = false;
  p.features.render_quality = false;
  p.features.controller_options = false;
  p.features.achievement_toasts = false;
  p.features.achievements = false;
  p.game_settings = GameSettings;
  return p;
}

}  // namespace

int main(int argc, char** argv) { return trg::hub::Run(Avp(), argc, argv); }
