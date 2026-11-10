# 018. The runtime builds on macOS

Parts: runtime (build).

## Symptom

`monogame/runtime/build.sh` on macOS stopped at the link of `libmgruntime.dylib`, in three ways one after another:

- `_vkGetPipelineExecutablePropertiesKHR` and `_vkGetPipelineExecutableStatisticsKHR` were undefined for arm64.
- The linker refused FAudio's static distance curves: `pointer not aligned in '_F3DAudioCalculate.lpfReverbDefault'`.
- The x86_64 half of the library found none of SDL2's symbols.

## Cause

- macOS links MoltenVK statically instead of loading Vulkan through volk, and MoltenVK exports no extension entry
  points; the pipeline statistics of issue 012 named the two functions of `VK_KHR_pipeline_executable_properties`
  directly.
- FAudio declares its 3D audio structures packed to one byte, so a static curve holding a pointer sits on a 4-byte
  boundary. The linker of current Xcode (ld-prime) refuses an unaligned pointer in chained fixups, and it ignores
  `-ld_classic`, the older linker the build asked for to avoid it.
- `premake5.lua` links the runtime for arm64 and x86_64 in one library, while `build.sh` built the static SDL2 and
  FAudio for the host's architecture only.

## Change

- `src/vulkan/MGG_Vulkan.cpp`: the device takes the two statistics functions from `vkGetDeviceProcAddr` after its
  creation, on every platform, and keeps the statistics off when either is missing (issue 012).
- `src/premake5.lua`: FAudio's macOS link option is `-no_fixup_chains` instead of `-ld_classic`, so the linker writes
  the unaligned pointers as classic rebase entries.
- `build.sh`: on macOS SDL2 and FAudio are configured with `CMAKE_OSX_ARCHITECTURES=arm64;x86_64`, recorded in their
  build stamps so an existing single-architecture build is rebuilt.

The runtime's interface is unchanged.

## Test

macOS 26.6 on Apple silicon, Xcode linker ld-27106, Vulkan SDK 1.4.363.1: `build.sh` writes
`bin/osx/libmgruntime.dylib`, `lipo -info` lists x86_64 and arm64, and it exports the runtime's 157 `MGG_`, `MGP_` and
`MGA_` functions. The client has not yet been run on it, and Linux and Windows builds were not rerun: the source change
there is the statistics lookup only, which those platforms reach through the same `vkGetDeviceProcAddr`.
