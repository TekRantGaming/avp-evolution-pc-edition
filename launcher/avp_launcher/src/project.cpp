// AVP Evolution: PC Edition, as a TRG Launcher 2.0 project. Everything specific to the port is in this file; the
// dashboard, Android installs and settings screens come from trg-launcher (launcher2/README.md).
//
// The game reads its settings from games/avp_evolution/avp_evolution.toml (the avp_ keys below) when it starts:
// see AVPE_Play/ExportedProject/Assets/Plugins/PCPort/PCSettings.cs.

#include <cstdlib>
#include <filesystem>
#include <string>
#include <system_error>
#include <vector>

#include <trg/hub.h>

#include <windows.h>

#include "logo_png.h"

namespace fs = std::filesystem;

namespace {

std::string Utf8(const fs::path& p) {
  const auto s = p.u8string();
  return std::string(s.begin(), s.end());
}

// The port's program and data for this PC. Until the converter that builds the game from the player's APK and OBB
// is finished, the install copies a prepared build (the "payload" folder beside the launcher) once the player's own
// copy has been checked. AVP_PAYLOAD overrides the folder.
fs::path PayloadDir() {
  wchar_t buf[MAX_PATH] = L"";
  if (GetEnvironmentVariableW(L"AVP_PAYLOAD", buf, MAX_PATH) > 0) return fs::path(buf);
  GetModuleFileNameW(nullptr, buf, MAX_PATH);
  return fs::path(buf).parent_path() / "payload";
}

std::string InstallFromApk(trg::Task& task, const trg::hub::ApkInstall& in) {
  const fs::path from = PayloadDir();
  std::error_code ec;
  if (!fs::exists(from / "AVPEvolution.exe", ec))
    return "The PC port files were not found beside the launcher (the payload folder). Download the launcher again.";
  // Total size first, for progress.
  long long total = 0, done = 0;
  std::vector<fs::path> files;
  for (fs::recursive_directory_iterator it(from, ec), end; !ec && it != end; it.increment(ec)) {
    if (!it->is_regular_file(ec)) continue;
    files.push_back(it->path());
    total += (long long)it->file_size(ec);
  }
  const fs::path dest = fs::u8path(in.dest);
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
