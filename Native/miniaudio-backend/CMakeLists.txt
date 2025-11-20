cmake_minimum_required(VERSION 3.26)
project(miniaudio C)

set(CMAKE_C_STANDARD 11)

include_directories(${CMAKE_CURRENT_SOURCE_DIR})

# Set base library name
set(LIBRARY_NAME "miniaudio")

# Building Shared Library
add_library(${LIBRARY_NAME} SHARED
        library.c
        library.h
        miniaudio/miniaudio.h)

# Platform-specific configurations
if (CMAKE_SYSTEM_NAME STREQUAL "Windows")
    add_definitions(-DMA_DLL)

    if (MSVC)
        add_definitions(-D_CRT_SECURE_NO_WARNINGS)
    endif()

    if (CMAKE_COMPILER_IS_GNUCC)
        set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} -static-libgcc")

        target_link_libraries(${LIBRARY_NAME}
                -static
                gcc
                winpthread
                -dynamic
        )
    endif ()

    # Windows: miniaudio.dll
    set_target_properties(${LIBRARY_NAME} PROPERTIES
            PREFIX ""
            SUFFIX ".dll"
    )

elseif (CMAKE_SYSTEM_NAME STREQUAL "Linux")
    set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} -static-libgcc")

    target_link_libraries(${LIBRARY_NAME}
            dl
            pthread
            m
    )

    # Linux: libminiaudio.so
    set_target_properties(${LIBRARY_NAME} PROPERTIES
            PREFIX "lib"
            SUFFIX ".so"
    )

elseif (CMAKE_SYSTEM_NAME STREQUAL "macOS")
    # Define MA_NO_RUNTIME_LINKING for macOS builds
    add_definitions(-DMA_NO_RUNTIME_LINKING)

    # Explicitly link required frameworks as per miniaudio docs
    target_link_libraries(${LIBRARY_NAME}
            "-framework CoreFoundation"
            "-framework CoreAudio"
            "-framework AudioToolbox"
            "-lpthread"
            "-lm"
    )

    # macOS: libminiaudio.dylib
    set_target_properties(${LIBRARY_NAME} PROPERTIES
            PREFIX "lib"
            SUFFIX ".dylib"
            # Enable code signing for macOS (ad-hoc signing)
            CODE_SIGNING_REQUIRED TRUE
            CODE_SIGNING_ALLOWED TRUE
            CODE_SIGN_IDENTITY "-"
    )

elseif (CMAKE_SYSTEM_NAME STREQUAL "iOS")
    # Set language to Objective-C for iOS
    set_source_files_properties(library.c PROPERTIES LANGUAGE OBJC)

    # Define MA_NO_RUNTIME_LINKING for iOS builds
    add_definitions(-DMA_NO_RUNTIME_LINKING)

    # Link with frameworks
    target_link_libraries(${LIBRARY_NAME}
            "-framework CoreFoundation"
            "-framework CoreAudio"
            "-framework AudioToolbox"
            "-framework AVFoundation"
            "-lpthread"
            "-lm"
    )

    # iOS: miniaudio.framework/miniaudio
    set_target_properties(${LIBRARY_NAME} PROPERTIES
            FRAMEWORK TRUE
            FRAMEWORK_VERSION A
            MACOSX_FRAMEWORK_IDENTIFIER com.mackron.miniaudio
            MACOSX_FRAMEWORK_SHORT_VERSION_STRING "1.0"
            MACOSX_FRAMEWORK_BUNDLE_VERSION "1"
            XCODE_ATTRIBUTE_CODE_SIGN_IDENTITY ""
            XCODE_ATTRIBUTE_CODE_SIGNING_REQUIRED "NO"
            XCODE_ATTRIBUTE_CODE_SIGNING_ALLOWED "NO"
    )

elseif (CMAKE_SYSTEM_NAME STREQUAL "Android")
    target_link_options(${LIBRARY_NAME} PRIVATE "-Wl,-z,max-page-size=16384")

    # Android: libminiaudio.so
    set_target_properties(${LIBRARY_NAME} PROPERTIES
            PREFIX "lib"
            SUFFIX ".so"
    )

elseif (CMAKE_SYSTEM_NAME STREQUAL "Emscripten")
    # Emscripten: libminiaudio.js and libminiaudio.wasm
    set_target_properties(${LIBRARY_NAME} PROPERTIES
            OUTPUT_NAME "libminiaudio" # Base name for output files (libminiaudio.wasm, libminiaudio.js)
            SUFFIX ".js"             # Emscripten will generate .js and .wasm
            LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
    )
endif ()