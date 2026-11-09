#pragma once

#include <windows.h>
#include <cstdint>

#if defined(PDBLENS_NATIVE_EXPORTS)
#define PDBLENS_API extern "C" __declspec(dllexport)
#else
#define PDBLENS_API extern "C" __declspec(dllimport)
#endif

enum PdbLensSymbolCategory : uint32_t {
    PdbLensCategoryStruct = 1u << 0,
    PdbLensCategoryClass = 1u << 1,
    PdbLensCategoryUnion = 1u << 2,
    PdbLensCategoryEnum = 1u << 3,
    PdbLensCategoryTypedef = 1u << 4,
    PdbLensCategoryFunction = 1u << 5,
    PdbLensCategoryData = 1u << 6,
    PdbLensCategoryPublicSymbol = 1u << 7,
    PdbLensCategoryAllTypes = PdbLensCategoryStruct | PdbLensCategoryClass | PdbLensCategoryUnion,
    PdbLensCategoryDefault = PdbLensCategoryAllTypes | PdbLensCategoryEnum |
        PdbLensCategoryTypedef | PdbLensCategoryFunction
};

PDBLENS_API void* PdbLens_CreateWorkspace();
PDBLENS_API void PdbLens_DestroyWorkspace(void* workspace);

PDBLENS_API int PdbLens_OpenPdb(void* workspace, const wchar_t* path, char** resultJson);
PDBLENS_API int PdbLens_ClosePdb(void* workspace, uint32_t pdbId, char** resultJson);

PDBLENS_API int PdbLens_QuerySymbols(
    void* workspace,
    uint32_t pdbFilter,
    uint32_t categories,
    const wchar_t* searchText,
    uint32_t offset,
    uint32_t limit,
    char** resultJson);

PDBLENS_API int PdbLens_LocateSymbol(
    void* workspace,
    uint32_t pdbFilter,
    uint32_t pdbId,
    uint32_t symIndexId,
    uint32_t categories,
    char** resultJson);

PDBLENS_API int PdbLens_GetDefinition(
    void* workspace,
    uint32_t pdbId,
    uint32_t symIndexId,
    char** resultJson);

PDBLENS_API void PdbLens_FreeString(char* value);
