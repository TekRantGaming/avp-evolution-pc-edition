# AVP Evolution: PC Edition's launcher (TRG Launcher 2.0). Build with build.bat.
cmake_minimum_required(VERSION 3.21)
project(avp_launcher CXX C)

set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
set(TRG_LAUNCHER_STANDALONE ON CACHE BOOL "" FORCE)
set(TRG_LAUNCHER_BUILD_DEMO OFF CACHE BOOL "" FORCE)
set(TRG_LAUNCHER_2 ON CACHE BOOL "" FORCE)
# The launcher library: a local checkout of trg-launcher (branch avp-evolution) beside this folder.
add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../trg-launcher ${CMAKE_BINARY_DIR}/trg-launcher)

file(READ ${CMAKE_CURRENT_SOURCE_DIR}/VERSION AVP_VERSION)
string(STRIP "${AVP_VERSION}" AVP_VERSION)
string(REPLACE "." "," _commas "${AVP_VERSION}")

add_executable(avp_launcher WIN32 src/project.cpp src/launcher.rc)
target_link_libraries(avp_launcher PRIVATE trg::launcher2)
target_compile_definitions(avp_launcher PRIVATE PROJECT_VERSION="${AVP_VERSION}" PROJECT_VERSION_COMMAS=${_commas},0)
if(MSVC)
  target_compile_options(avp_launcher PRIVATE /W4 /utf-8 /permissive-)
  target_link_options(avp_launcher PRIVATE /ENTRY:mainCRTStartup)
endif()
