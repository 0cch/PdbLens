cmake_minimum_required(VERSION 3.20)

project(PdbLens.Native LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(SYMWRAP_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/third_party/SymWrap")
if(NOT EXISTS "${SYMWRAP_ROOT}/SymWrap.cpp")
    message(FATAL_ERROR "SymWrap submodule is missing. Run: git submodule update --init --recursive")
endif()

set(VSWHERE "C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe")
if(NOT EXISTS "${VSWHERE}")
    set(VSWHERE "C:/Program Files/Microsoft Visual Studio/Installer/vswhere.exe")
endif()
if(EXISTS "${VSWHERE}")
    execute_process(
        COMMAND "${VSWHERE}" -latest -products * -property installationPath
        OUTPUT_VARIABLE VS_INSTALL_PATH
        OUTPUT_STRIP_TRAILING_WHITESPACE)
endif()

if(NOT VS_INSTALL_PATH)
    message(FATAL_ERROR "Visual Studio with the DIA SDK could not be located")
endif()

set(DIA_SDK_ROOT "${VS_INSTALL_PATH}/DIA SDK")
if(NOT EXISTS "${DIA_SDK_ROOT}/include/dia2.h")
    message(FATAL_ERROR "DIA SDK not found at ${DIA_SDK_ROOT}")
endif()

add_library(PdbLens.Native SHARED
    src/PdbLens.Native/PdbLensNative.cpp
    ${SYMWRAP_ROOT}/SymWrap.cpp)

target_include_directories(PdbLens.Native PRIVATE
    "${SYMWRAP_ROOT}"
    "${DIA_SDK_ROOT}/include")

target_compile_definitions(PdbLens.Native PRIVATE
    UNICODE
    _UNICODE
    NOMINMAX
    WIN32_LEAN_AND_MEAN
    PDBLENS_NATIVE_EXPORTS)

if(MSVC)
    target_compile_options(PdbLens.Native PRIVATE /W4 /permissive- /utf-8)
    target_link_directories(PdbLens.Native PRIVATE "${DIA_SDK_ROOT}/lib/amd64")
endif()

target_link_libraries(PdbLens.Native PRIVATE
    diaguids
    ole32
    oleaut32
    shlwapi)

set_target_properties(PdbLens.Native PROPERTIES
    OUTPUT_NAME "PdbLens.Native"
    PREFIX "")

enable_testing()

add_executable(PdbLens.Native.Tests
    tests/PdbLens.Native.Tests.cpp)

target_link_libraries(PdbLens.Native.Tests PRIVATE PdbLens.Native)

if(MSVC)
    target_compile_options(PdbLens.Native.Tests PRIVATE /W4 /permissive- /utf-8 /Zi)
    target_link_options(PdbLens.Native.Tests PRIVATE /DEBUG)
endif()

add_dependencies(PdbLens.Native.Tests PdbLens.Native)

add_custom_command(TARGET PdbLens.Native.Tests POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "${DIA_SDK_ROOT}/bin/amd64/msdia140.dll"
        "$<TARGET_FILE_DIR:PdbLens.Native.Tests>/msdia140.dll")

add_test(NAME PdbLens.Native.Tests
    COMMAND PdbLens.Native.Tests "$<TARGET_PDB_FILE:PdbLens.Native.Tests>")
set_tests_properties(PdbLens.Native.Tests PROPERTIES
    WORKING_DIRECTORY "$<TARGET_FILE_DIR:PdbLens.Native.Tests>")