cmake_minimum_required(VERSION 3.20)
project(SoundFlowFFmpeg C)

cmake_policy(SET CMP0135 NEW)

# Helper – normalise CPU names that vary between OSes / toolchains
function(normalise_arch IN OUT)
    string(TOLOWER "${IN}" _arch)
    if(_arch STREQUAL "x86_64" OR _arch STREQUAL "amd64")
        set(${OUT} "x64"      PARENT_SCOPE)
    elseif(_arch STREQUAL "i686" OR _arch STREQUAL "x86")
        set(${OUT} "x86"      PARENT_SCOPE)
    elseif(_arch MATCHES "^armv?7")
        set(${OUT} "arm"      PARENT_SCOPE)
    elseif(_arch STREQUAL "aarch64" OR _arch STREQUAL "arm64")
        set(${OUT} "arm64"    PARENT_SCOPE)
    else()
        set(${OUT} "${_arch}" PARENT_SCOPE)   # fall-through
    endif()
endfunction()

# Platform / RID detection
if(CMAKE_SYSTEM_NAME STREQUAL "Windows")
    set(TARGET_OS "win")
    if(CMAKE_SYSTEM_PROCESSOR MATCHES "ARM64|AARCH64")
        normalise_arch("${CMAKE_SYSTEM_PROCESSOR}" TARGET_ARCH)
    elseif(CMAKE_SIZEOF_VOID_P EQUAL 8)
        set(TARGET_ARCH "x64")
    elseif(CMAKE_SIZEOF_VOID_P EQUAL 4)
        set(TARGET_ARCH "x86")
    else()
        # Fallback for any other unforeseen cases
        normalise_arch("${CMAKE_SYSTEM_PROCESSOR}" TARGET_ARCH)
    endif()

elseif(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    set(TARGET_OS "linux")
    normalise_arch("${CMAKE_SYSTEM_PROCESSOR}" TARGET_ARCH)

elseif(CMAKE_SYSTEM_NAME STREQUAL "Darwin")
    set(TARGET_OS "osx")
    normalise_arch("${CMAKE_SYSTEM_PROCESSOR}" TARGET_ARCH)

elseif(CMAKE_SYSTEM_NAME STREQUAL "iOS")
    set(TARGET_OS "ios")
    set(TARGET_ARCH "arm64")

elseif(CMAKE_SYSTEM_NAME STREQUAL "Android")
    set(TARGET_OS "android")
    if(ANDROID_ABI STREQUAL "x86_64")
        set(TARGET_ARCH "x64")
    elseif(ANDROID_ABI STREQUAL "armeabi-v7a")
        set(TARGET_ARCH "arm")
    elseif(ANDROID_ABI STREQUAL "arm64-v8a")
        set(TARGET_ARCH "arm64")
    endif()

elseif(CMAKE_SYSTEM_NAME STREQUAL "FreeBSD")
    set(TARGET_OS "freebsd")
    normalise_arch("${CMAKE_SYSTEM_PROCESSOR}" TARGET_ARCH)
endif()

# Set symbolic linker flags for ELF-based systems
if(CMAKE_SYSTEM_NAME STREQUAL "Linux" OR
        CMAKE_SYSTEM_NAME STREQUAL "Android" OR
        CMAKE_SYSTEM_NAME STREQUAL "FreeBSD")
    set(CMAKE_SHARED_LINKER_FLAGS "-Wl,-Bsymbolic")
endif()

# .NET-style RID and output layout
set(RUNTIME_ID "${TARGET_OS}-${TARGET_ARCH}")
set(OUTPUT_DIR "${CMAKE_BINARY_DIR}/runtimes/${RUNTIME_ID}/native")
set(FFMPEG_INSTALL_DIR "${CMAKE_BINARY_DIR}/ffmpeg-install")

# FFmpeg – download/build parameters
set(FFMPEG_VERSION "8.0")
set(FFMPEG_URL "https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.gz")

if(NOT CMAKE_BUILD_PARALLEL_LEVEL)
    set(CMAKE_BUILD_PARALLEL_LEVEL 1)
endif()

# Common configure flags
set(FFMPEG_COMMON_FLAGS
        --disable-gpl
        --disable-nonfree

        # Build Type
        --disable-shared
        --enable-static
        --enable-pic
        --disable-symver

        # General Disables
        --disable-doc
        --disable-debug
        --disable-avdevice
        --disable-swscale
        --disable-programs
        --disable-avfilter
        --disable-network
        --disable-zlib
        --disable-lzma
        --disable-bzlib
        --disable-iconv
        --disable-libxcb
        --disable-bsfs
        --disable-filters
        --disable-parsers
        --disable-indevs
        --disable-outdevs
        --disable-encoders
        --disable-decoders
        --disable-hwaccels
        --disable-nvenc
        --disable-videotoolbox
        --disable-audiotoolbox

        # Enabled Protocols
        --disable-protocols
        --enable-protocol=file
        --enable-protocol=pipe

        # Enabled Demuxers
        --enable-demuxer=image2
        --enable-demuxer=aac
        --enable-demuxer=ac3
        --enable-demuxer=aiff
        --enable-demuxer=ape
        --enable-demuxer=asf
        --enable-demuxer=au
        --enable-demuxer=avi
        --enable-demuxer=flac
        --enable-demuxer=flv
        --enable-demuxer=matroska
        --enable-demuxer=mov
        --enable-demuxer=m4v
        --enable-demuxer=mp3
        --enable-demuxer=mpc
        --enable-demuxer=mpc8
        --enable-demuxer=ogg
        --enable-demuxer=pcm_alaw
        --enable-demuxer=pcm_mulaw
        --enable-demuxer=pcm_f64be
        --enable-demuxer=pcm_f64le
        --enable-demuxer=pcm_f32be
        --enable-demuxer=pcm_f32le
        --enable-demuxer=pcm_s32be
        --enable-demuxer=pcm_s32le
        --enable-demuxer=pcm_s24be
        --enable-demuxer=pcm_s24le
        --enable-demuxer=pcm_s16be
        --enable-demuxer=pcm_s16le
        --enable-demuxer=pcm_s8
        --enable-demuxer=pcm_u32be
        --enable-demuxer=pcm_u32le
        --enable-demuxer=pcm_u24be
        --enable-demuxer=pcm_u24le
        --enable-demuxer=pcm_u16be
        --enable-demuxer=pcm_u16le
        --enable-demuxer=pcm_u8
        --enable-demuxer=rm
        --enable-demuxer=shorten
        --enable-demuxer=tak
        --enable-demuxer=tta
        --enable-demuxer=wav
        --enable-demuxer=wv
        --enable-demuxer=xwma
        --enable-demuxer=dsf

        # Enabled Decoders
        --enable-decoder=aac
        --enable-decoder=aac_latm
        --enable-decoder=ac3
        --enable-decoder=alac
        --enable-decoder=als
        --enable-decoder=ape
        --enable-decoder=atrac1
        --enable-decoder=atrac3
        --enable-decoder=eac3
        --enable-decoder=flac
        --enable-decoder=gsm
        --enable-decoder=gsm_ms
        --enable-decoder=mp1
        --enable-decoder=mp1float
        --enable-decoder=mp2
        --enable-decoder=mp2float
        --enable-decoder=mp3
        --enable-decoder=mp3adu
        --enable-decoder=mp3adufloat
        --enable-decoder=mp3float
        --enable-decoder=mp3on4
        --enable-decoder=mp3on4float
        --enable-decoder=mpc7
        --enable-decoder=mpc8
        --enable-decoder=opus
        --enable-decoder=ra_144
        --enable-decoder=ra_288
        --enable-decoder=ralf
        --enable-decoder=shorten
        --enable-decoder=tak
        --enable-decoder=tta
        --enable-decoder=vorbis
        --enable-decoder=wavpack
        --enable-decoder=wmalossless
        --enable-decoder=wmapro
        --enable-decoder=wmav1
        --enable-decoder=wmav2
        --enable-decoder=wmavoice
        --enable-decoder=pcm_alaw
        --enable-decoder=pcm_bluray
        --enable-decoder=pcm_dvd
        --enable-decoder=pcm_f32be
        --enable-decoder=pcm_f32le
        --enable-decoder=pcm_f64be
        --enable-decoder=pcm_f64le
        --enable-decoder=pcm_lxf
        --enable-decoder=pcm_mulaw
        --enable-decoder=pcm_s8
        --enable-decoder=pcm_s8_planar
        --enable-decoder=pcm_s16be
        --enable-decoder=pcm_s16be_planar
        --enable-decoder=pcm_s16le
        --enable-decoder=pcm_s16le_planar
        --enable-decoder=pcm_s24be
        --enable-decoder=pcm_s24daud
        --enable-decoder=pcm_s24le
        --enable-decoder=pcm_s24le_planar
        --enable-decoder=pcm_s32be
        --enable-decoder=pcm_s32le
        --enable-decoder=pcm_s32le_planar
        --enable-decoder=pcm_u8
        --enable-decoder=pcm_u16be
        --enable-decoder=pcm_u16le
        --enable-decoder=pcm_u24be
        --enable-decoder=pcm_u24le
        --enable-decoder=pcm_u32be
        --enable-decoder=pcm_u32le
        --enable-decoder=dsd_lsbf
        --enable-decoder=dsd_msbf
        --enable-decoder=dsd_lsbf_planar
        --enable-decoder=dsd_msbf_planar

        # Enabled Parsers
        --enable-parser=aac
        --enable-parser=aac_latm
        --enable-parser=ac3
        --enable-parser=cook
        --enable-parser=dca
        --enable-parser=flac
        --enable-parser=gsm
        --enable-parser=mpegaudio
        --enable-parser=tak
        --enable-parser=vorbis

        # Enabled Muxers
        --enable-muxer=ac3
        --enable-muxer=adts
        --enable-muxer=aiff
        --enable-muxer=alaw
        --enable-muxer=flac
        --enable-muxer=ipod
        --enable-muxer=matroska
        --enable-muxer=mp2
        --enable-muxer=mp3
        --enable-muxer=mulaw
        --enable-muxer=ogg
        --enable-muxer=opus
        --enable-muxer=pcm_f32le
        --enable-muxer=pcm_f64le
        --enable-muxer=pcm_s16be
        --enable-muxer=pcm_s16le
        --enable-muxer=pcm_s24be
        --enable-muxer=pcm_s24le
        --enable-muxer=pcm_s32be
        --enable-muxer=pcm_s32le
        --enable-muxer=pcm_s8
        --enable-muxer=pcm_u8
        --enable-muxer=tta
        --enable-muxer=wav
        --enable-muxer=wv

        # Enabled Encoders
        # Lossy and Lossless encoders
        --enable-encoder=aac
        --enable-encoder=ac3
        --enable-encoder=alac
        --enable-encoder=eac3
        --enable-encoder=flac
        --enable-encoder=mp2
        --enable-encoder=nellymoser
        --enable-encoder=opus
        --enable-encoder=sbc
        --enable-encoder=sonic
        --enable-encoder=tta
        --enable-encoder=vorbis
        --enable-encoder=wavpack
        --enable-encoder=wmav1
        --enable-encoder=wmav2

        # PCM (Uncompressed) encoders
        --enable-encoder=pcm_alaw
        --enable-encoder=pcm_mulaw
        --enable-encoder=pcm_f32be
        --enable-encoder=pcm_f32le
        --enable-encoder=pcm_f64be
        --enable-encoder=pcm_f64le
        --enable-encoder=pcm_s8
        --enable-encoder=pcm_s8_planar
        --enable-encoder=pcm_s16be
        --enable-encoder=pcm_s16be_planar
        --enable-encoder=pcm_s16le
        --enable-encoder=pcm_s16le_planar
        --enable-encoder=pcm_s24be
        --enable-encoder=pcm_s24daud
        --enable-encoder=pcm_s24le
        --enable-encoder=pcm_s24le_planar
        --enable-encoder=pcm_s32be
        --enable-encoder=pcm_s32le
        --enable-encoder=pcm_s32le_planar
        --enable-encoder=pcm_s64be
        --enable-encoder=pcm_s64le
        --enable-encoder=pcm_u8
        --enable-encoder=pcm_u16be
        --enable-encoder=pcm_u16le
        --enable-encoder=pcm_u24be
        --enable-encoder=pcm_u24le
        --enable-encoder=pcm_u32be
        --enable-encoder=pcm_u32le
)

# Re-enable ffmpeg / ffprobe ONLY when we build natively
if(NOT CMAKE_CROSSCOMPILING)
    list(APPEND FFMPEG_COMMON_FLAGS
            --enable-ffmpeg
            --enable-ffprobe)
endif()

# Per-platform additions
if(TARGET_OS STREQUAL "win")
    if(TARGET_ARCH STREQUAL "x64")
        set(FFMPEG_ARCH "x86_64")
    elseif(TARGET_ARCH STREQUAL "x86")
        set(FFMPEG_ARCH "x86")
    elseif(TARGET_ARCH STREQUAL "arm64")
        set(FFMPEG_ARCH "aarch64")
    endif()

    list(APPEND FFMPEG_COMMON_FLAGS
            --target-os=mingw32
            --arch=${FFMPEG_ARCH}
            "--extra-cflags=-O2 -static -static-libgcc -static-libstdc++"
            "--extra-ldflags=-static")

    if(CMAKE_CROSSCOMPILING)
        list(APPEND FFMPEG_COMMON_FLAGS --cross-prefix=${FFMPEG_ARCH}-w64-mingw32-)
    endif()

elseif(TARGET_OS STREQUAL "osx")
    if(TARGET_ARCH STREQUAL "x64")
        set(FFMPEG_ARCH "x86_64")
        set(MACOS_TARGET "x86_64-apple-darwin")
    else()
        set(FFMPEG_ARCH "aarch64")
        set(MACOS_TARGET "arm64-apple-darwin")
    endif()

    list(APPEND FFMPEG_COMMON_FLAGS
            --target-os=darwin
            --arch=${FFMPEG_ARCH}
            --enable-cross-compile
            "--extra-cflags=-target ${MACOS_TARGET}"
            "--extra-ldflags=-target ${MACOS_TARGET}"
            --enable-runtime-cpudetect)

elseif(TARGET_OS STREQUAL "linux")
    if(TARGET_ARCH STREQUAL "x64")
        set(FFMPEG_ARCH "x86_64")
    elseif(TARGET_ARCH STREQUAL "arm")
        set(FFMPEG_ARCH "arm")
        set(LINUX_CROSS_PREFIX "arm-linux-gnueabihf-")
    else()
        set(FFMPEG_ARCH "aarch64")
        set(LINUX_CROSS_PREFIX "aarch64-linux-gnu-")
    endif()

    list(APPEND FFMPEG_COMMON_FLAGS
            --target-os=linux
            --arch=${FFMPEG_ARCH}
            --enable-pic)

    if(CMAKE_CROSSCOMPILING)
        list(APPEND FFMPEG_COMMON_FLAGS
                --enable-cross-compile
                --cross-prefix=${LINUX_CROSS_PREFIX})
    endif()

elseif(TARGET_OS STREQUAL "android")
    set(ANDROID_API_LEVEL 21)

    # Determine FFmpeg arch and NDK toolchain triple from ANDROID_ABI
    if(ANDROID_ABI STREQUAL "x86_64")
        set(TARGET_ARCH "x64")
        set(FFMPEG_ARCH "x86_64")
        set(ANDROID_TARGET_TRIPLE "x86_64-linux-android")
    elseif(ANDROID_ABI STREQUAL "armeabi-v7a")
        set(TARGET_ARCH "arm")
        set(FFMPEG_ARCH "arm")
        set(ANDROID_TARGET_TRIPLE "armv7a-linux-androideabi")
    elseif(ANDROID_ABI STREQUAL "arm64-v8a")
        set(TARGET_ARCH "arm64")
        set(FFMPEG_ARCH "aarch64")
        set(ANDROID_TARGET_TRIPLE "aarch64-linux-android")
    endif()

    # Get the directory where the NDK toolchain binaries are located
    get_filename_component(NDK_TOOLCHAIN_BIN_DIR ${CMAKE_C_COMPILER} DIRECTORY)

    # Construct the full path to the correct NDK compiler wrapper script
    set(ANDROID_COMPILER_WRAPPER "${NDK_TOOLCHAIN_BIN_DIR}/${ANDROID_TARGET_TRIPLE}${ANDROID_API_LEVEL}-clang")

    list(APPEND FFMPEG_COMMON_FLAGS
            --target-os=android
            --arch=${FFMPEG_ARCH}
            --enable-cross-compile
            --cc=${ANDROID_COMPILER_WRAPPER}
            --ar=${NDK_TOOLCHAIN_BIN_DIR}/llvm-ar
            --ranlib=${NDK_TOOLCHAIN_BIN_DIR}/llvm-ranlib
            --strip=${NDK_TOOLCHAIN_BIN_DIR}/llvm-strip)

elseif(TARGET_OS STREQUAL "ios")
    list(APPEND FFMPEG_COMMON_FLAGS
            --target-os=darwin
            --arch=aarch64
            --enable-cross-compile
            --disable-asm
            "--extra-cflags=-arch arm64 -isysroot ${CMAKE_OSX_SYSROOT} -miphoneos-version-min=${CMAKE_OSX_DEPLOYMENT_TARGET}"
            "--extra-ldflags=-arch arm64 -isysroot ${CMAKE_OSX_SYSROOT} -miphoneos-version-min=${CMAKE_OSX_DEPLOYMENT_TARGET}")

elseif(TARGET_OS STREQUAL "freebsd")
    if(TARGET_ARCH STREQUAL "x64")
        set(FFMPEG_ARCH "x86_64")
    else()
        set(FFMPEG_ARCH "aarch64")
    endif()

    list(APPEND FFMPEG_COMMON_FLAGS
            --target-os=freebsd
            --arch=${FFMPEG_ARCH}
            --cc=cc)

    if(CMAKE_CROSSCOMPILING)
        list(APPEND FFMPEG_COMMON_FLAGS --cross-prefix=${FFMPEG_ARCH}-unknown-freebsd-)
    endif()
endif()

# External build of FFmpeg
find_program(BASH_EXECUTABLE bash REQUIRED)

if(CMAKE_GENERATOR STREQUAL "MinGW Makefiles")
    find_program(FFMPEG_MAKE_EXECUTABLE make HINTS "C:/msys64/usr/bin")
    if(NOT FFMPEG_MAKE_EXECUTABLE)
        message(FATAL_ERROR "Could not find MSYS 'make'. It's required to build FFmpeg with MinGW Makefiles.")
    endif()
else()
    # For all other generators (Unix Makefiles, Ninja, etc.), CMAKE_MAKE_PROGRAM is fine.
    set(FFMPEG_MAKE_EXECUTABLE ${CMAKE_MAKE_PROGRAM})
endif()

include(ExternalProject)
ExternalProject_Add(ffmpeg_dependency
        URL               ${FFMPEG_URL}
        PREFIX            ${CMAKE_BINARY_DIR}/ffmpeg
        INSTALL_DIR       ${FFMPEG_INSTALL_DIR}
        BUILD_IN_SOURCE   1
        CONFIGURE_COMMAND ${BASH_EXECUTABLE} <SOURCE_DIR>/configure
        --prefix=${FFMPEG_INSTALL_DIR}
        ${FFMPEG_COMMON_FLAGS}
        BUILD_COMMAND     ${FFMPEG_MAKE_EXECUTABLE} -j${CMAKE_BUILD_PARALLEL_LEVEL}
        INSTALL_COMMAND   ${FFMPEG_MAKE_EXECUTABLE} install
        LOG_DOWNLOAD      1
        LOG_CONFIGURE     1
        LOG_BUILD         1
        LOG_INSTALL       1
)

# SoundFlow library
add_library(soundflow-ffmpeg SHARED
        soundflow-ffmpeg.c
        soundflow-ffmpeg.h)

add_dependencies(soundflow-ffmpeg ffmpeg_dependency)

# Naming / output
if(TARGET_OS STREQUAL "win")
    set_target_properties(soundflow-ffmpeg PROPERTIES
            PREFIX "" SUFFIX ".dll" OUTPUT_NAME "soundflow-ffmpeg")
elseif(TARGET_OS STREQUAL "linux" OR TARGET_OS STREQUAL "freebsd" OR TARGET_OS STREQUAL "android")
    set_target_properties(soundflow-ffmpeg PROPERTIES
            PREFIX "lib" SUFFIX ".so" OUTPUT_NAME "soundflow-ffmpeg")
elseif(TARGET_OS STREQUAL "osx")
    set_target_properties(soundflow-ffmpeg PROPERTIES
            PREFIX "lib" SUFFIX ".dylib" OUTPUT_NAME "soundflow-ffmpeg")
elseif(TARGET_OS STREQUAL "ios")
    set_target_properties(soundflow-ffmpeg PROPERTIES
            FRAMEWORK TRUE
            OUTPUT_NAME "soundflow-ffmpeg"
            FRAMEWORK_VERSION A
            MACOSX_FRAMEWORK_IDENTIFIER com.soundflow.ffmpeg)
endif()

set_target_properties(soundflow-ffmpeg PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY  "${OUTPUT_DIR}"
        LIBRARY_OUTPUT_DIRECTORY  "${OUTPUT_DIR}"
        ARCHIVE_OUTPUT_DIRECTORY  "${OUTPUT_DIR}")

# Include / link against FFmpeg
target_include_directories(soundflow-ffmpeg PRIVATE
        ${FFMPEG_INSTALL_DIR}/include)

target_link_directories(soundflow-ffmpeg PRIVATE
        ${FFMPEG_INSTALL_DIR}/lib)

target_link_libraries(soundflow-ffmpeg PRIVATE
        avformat avcodec swresample avutil)

# Platform system libraries
if(TARGET_OS STREQUAL "win")
    target_link_libraries(soundflow-ffmpeg PRIVATE
            bcrypt secur32 ws2_32 iphlpapi)
    target_link_options(soundflow-ffmpeg PRIVATE
            -static -static-libgcc -static-libstdc++)
elseif(TARGET_OS STREQUAL "osx" OR TARGET_OS STREQUAL "ios")
    find_library(CORE_FOUNDATION_FRAMEWORK CoreFoundation)
    find_library(VIDEO_TOOLBOX_FRAMEWORK VideoToolbox)
    find_library(AUDIO_TOOLBOX_FRAMEWORK AudioToolbox)
    target_link_libraries(soundflow-ffmpeg PRIVATE
            m pthread
            ${CORE_FOUNDATION_FRAMEWORK}
            ${VIDEO_TOOLBOX_FRAMEWORK}
            ${AUDIO_TOOLBOX_FRAMEWORK})
elseif(TARGET_OS STREQUAL "linux" OR TARGET_OS STREQUAL "freebsd")
    target_link_libraries(soundflow-ffmpeg PRIVATE m pthread)
elseif(TARGET_OS STREQUAL "android")
    target_link_libraries(soundflow-ffmpeg PRIVATE m atomic)
    target_link_options(soundflow-ffmpeg PRIVATE "-Wl,-z,max-page-size=16384")
endif()
