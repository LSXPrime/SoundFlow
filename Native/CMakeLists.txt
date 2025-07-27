cmake_minimum_required(VERSION 3.26)
project(miniaudio)

set(CMAKE_CXX_STANDARD 20)

include_directories(${CMAKE_CURRENT_SOURCE_DIR})

# Building Shared Library
add_library(miniaudio SHARED
    library.cpp
    library.h
    Submodules/miniaudio/miniaudio.h)

# Platform-specific configurations
if (CMAKE_SYSTEM_NAME STREQUAL "Windows")
    if (CMAKE_COMPILER_IS_GNUCC)
        set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -static-libgcc -static-libstdc++")

        target_link_libraries(miniaudio
            -static
            gcc
            stdc++
            winpthread
            -dynamic
        )
    endif()

elseif (CMAKE_SYSTEM_NAME STREQUAL "Linux")
    set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -static-libgcc -static-libstdc++")

    target_link_libraries(miniaudio
        dl
        pthread
        m
    )

elseif (CMAKE_SYSTEM_NAME STREQUAL "macOS")
    # Define MA_NO_RUNTIME_LINKING for macOS builds
    add_definitions(-DMA_NO_RUNTIME_LINKING)

    # Explicitly link required frameworks as per miniaudio docs
    target_link_libraries(miniaudio
        "-framework CoreFoundation"
        "-framework CoreAudio"
        "-framework AudioToolbox"
        "-lpthread"
        "-lm"
    )
    set_target_properties(miniaudio PROPERTIES
        # Enable code signing for macOS (ad-hoc signing)
        CODE_SIGNING_REQUIRED TRUE
        CODE_SIGNING_ALLOWED TRUE
        CODE_SIGN_IDENTITY "-"
    )

elseif (CMAKE_SYSTEM_NAME STREQUAL "iOS")
    # Define MA_NO_RUNTIME_LINKING for iOS builds
    add_definitions(-DMA_NO_RUNTIME_LINKING)

    # Use Objective-C++ for library.cpp file.
    set_source_files_properties(library.cpp PROPERTIES LANGUAGE OBJCXX)

    # Link with frameworks
    target_link_libraries(miniaudio
        "-framework CoreFoundation"
        "-framework CoreAudio"
        "-framework AudioToolbox"
        "-framework AVFoundation"
        "-lpthread"
        "-lm"
    )

    # Mark the miniaudio target as a framework.
    set_target_properties(miniaudio PROPERTIES
        FRAMEWORK TRUE
        FRAMEWORK_VERSION A
        MACOSX_FRAMEWORK_IDENTIFIER com.mackron.miniaudio
        MACOSX_FRAMEWORK_SHORT_VERSION_STRING "1.0"
        MACOSX_FRAMEWORK_BUNDLE_VERSION "1"
        XCODE_ATTRIBUTE_CODE_SIGN_IDENTITY ""
        XCODE_ATTRIBUTE_CODE_SIGNING_REQUIRED "NO"
        XCODE_ATTRIBUTE_CODE_SIGNING_ALLOWED "NO"
    )

elseif (CMAKE_SYSTEM_NAME STREQUAL "Emscripten")
    # Link with emscripten
    target_link_options(miniaudio PUBLIC
        "-s SIDE_MODULE=1"
        "-s USE_PTHREADS=1"
        "-s EXPORT_ALL=1" # Export all available functions
        "-s MODULARIZE=1"
        "-s EXPORT_ES6=1"
        "-s EXPORT_NAME='miniaudio'"
    )

endif()