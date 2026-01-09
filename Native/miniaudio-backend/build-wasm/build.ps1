# Set up Paths
$Env:EMSDK_PYTHON = "C:\Program Files\dotnet\packs\Microsoft.NET.Runtime.Emscripten.3.1.34.Python.win-x64\8.0.22\tools\python.exe"
$Env:DOTNET_EMSCRIPTEN_LLVM_ROOT = "C:\Program Files\dotnet\packs\Microsoft.NET.Runtime.Emscripten.3.1.34.Sdk.win-x64\8.0.22\tools\bin"
$Env:DOTNET_EMSCRIPTEN_BINARYEN_ROOT = "C:\Program Files\dotnet\packs\Microsoft.NET.Runtime.Emscripten.3.1.34.Sdk.win-x64\8.0.22\tools"
$Env:DOTNET_EMSCRIPTEN_NODE_JS = "C:\Program Files\dotnet\packs\Microsoft.NET.Runtime.Emscripten.3.1.34.Node.win-x64\8.0.22\tools\bin\node.exe"

#  Set Cache Location
$Env:EM_CACHE = "$env:USERPROFILE\.emscripten_cache_dotnet"

# Unfreeze the cache so it can build system libraries
$Env:EM_FROZEN_CACHE = "0"

# Clean previous build artifacts, excluding this script
Write-Host "Cleaning build directory..."
Get-ChildItem -Path . -Exclude 'build.ps1' | Remove-Item -Recurse -Force

# Configure
Write-Host "Configuring with emcmake..."
& "C:\Program Files\dotnet\packs\Microsoft.NET.Runtime.Emscripten.3.1.34.Sdk.win-x64\8.0.22\tools\emscripten\emcmake.bat" cmake .. -DCMAKE_BUILD_TYPE=Release

# Build
Write-Host "Building with cmake..."
cmake --build .

Write-Host "Build complete! libminiaudio.a is ready."