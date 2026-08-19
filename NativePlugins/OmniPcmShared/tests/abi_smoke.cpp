#include "OmniPcmShared.h"

#include <cstdint>
#include <iostream>

#ifdef _WIN32
#include <windows.h>
#endif

static_assert(sizeof(OmniPcmAbiInfo) == 32, "OmniPcmAbiInfo ABI changed");
static_assert(sizeof(OmniPcmStreamDescriptionV2) == 48, "OmniPcmStreamDescriptionV2 ABI changed");
static_assert(sizeof(OmniPcmSnapshotV2) == 200, "OmniPcmSnapshotV2 ABI changed");

int main() {
    const uint32_t version = OmniPcm_GetAbiVersion();
    if (version != OMNI_PCM_ABI_VERSION || (version >> 16) != 2u) {
        std::cerr << "unexpected ABI version: " << version << '\n';
        return 1;
    }

    OmniPcmAbiInfo info{};
    info.size = sizeof(info);
    if (OmniPcm_GetAbiInfo(&info) != OMNI_PCM_OK) {
        std::cerr << "OmniPcm_GetAbiInfo failed\n";
        return 2;
    }
    if (info.size != sizeof(info) || info.abi_major != 2u ||
        info.min_shared_protocol != OMNI_PCM_VERSION_2 ||
        info.max_shared_protocol != OMNI_PCM_VERSION_2 ||
        (info.sample_format_mask & (1u << OMNI_PCM_SAMPLE_FORMAT_FLOAT32_INTERLEAVED)) == 0) {
        std::cerr << "unexpected ABI capability data\n";
        return 3;
    }

#ifdef _WIN32
    constexpr wchar_t local_map[] = L"Local\\OmniMixPlayer_PCM_abi-v2-local-fallback-test";
    HANDLE mapping = CreateFileMappingW(
        INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, 4096, local_map);
    if (!mapping) return 4;
    auto* view = static_cast<uint8_t*>(MapViewOfFile(mapping, FILE_MAP_WRITE, 0, 0, 4096));
    if (!view) {
        CloseHandle(mapping);
        return 5;
    }
    *reinterpret_cast<uint64_t*>(view) = OMNI_PCM_MAGIC;
    *reinterpret_cast<uint32_t*>(view + 0x08) = OMNI_PCM_VERSION_2;
    *reinterpret_cast<int64_t*>(view + 0xB8) = static_cast<int64_t>(GetTickCount64());

    OmniPcmHandle handle = OmniPcm_OpenInstanceUtf8("abi-v2-local-fallback-test");
    const bool opened = handle && OmniPcm_IsOpen(handle) == 1;
    const int64_t heartbeat_age = opened ? OmniPcm_GetHeartbeatAgeMs(handle) : -1;
    OmniPcm_Close(handle);
    UnmapViewOfFile(view);
    CloseHandle(mapping);
    if (!opened || heartbeat_age < 0 || heartbeat_age > 1000) {
        std::cerr << "Local mapping fallback or heartbeat check failed\n";
        return 6;
    }
#endif
    return 0;
}
