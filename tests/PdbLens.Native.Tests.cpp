#include "../src/PdbLens.Native/PdbLensNative.h"

#include <cstdio>
#include <string>
#include <thread>

namespace {

int ExtractInt(const std::string &json, const char *key, int defaultValue = -1)
{
    const std::string needle = std::string("\"") + key + "\":";
    const size_t position = json.find(needle);
    if (position == std::string::npos) {
        return defaultValue;
    }
    return std::atoi(json.c_str() + position + needle.size());
}

bool Contains(const std::string &value, const char *needle)
{
    return value.find(needle) != std::string::npos;
}

class JsonResult {
public:
    explicit JsonResult(char *value) : Value(value) {}
    ~JsonResult() { PdbLens_FreeString(Value); }
    JsonResult(const JsonResult &) = delete;
    JsonResult &operator=(const JsonResult &) = delete;

    std::string Text() const { return Value != nullptr ? Value : ""; }
    char *Value = nullptr;
};

} // namespace

struct PdbLensTestChild {
    int ChildValue;
};

struct PdbLensTestStruct {
    int Value;
    PdbLensTestChild Child;
};

volatile PdbLensTestStruct g_testStruct = { 1, { 2 } };

int wmain(int argc, wchar_t **argv)
{
    (void)g_testStruct;
    if (argc < 2) {
        printf("Usage: PdbLens.Native.Tests <pdb-path>\n");
        return 2;
    }

    void *workspace = PdbLens_CreateWorkspace();
    if (workspace == nullptr) {
        printf("CreateWorkspace failed\n");
        return 3;
    }

    char *openJson = nullptr;
    int hr = PdbLens_OpenPdb(workspace, argv[1], &openJson);
    JsonResult openResult(openJson);
    printf("OpenPdb hr=0x%08X\n%s\n", static_cast<unsigned>(hr), openResult.Text().c_str());
    if (hr < 0 || !Contains(openResult.Text(), "\"ok\":true")) {
        PdbLens_DestroyWorkspace(workspace);
        return 4;
    }
    const int pdbId = ExtractInt(openResult.Text(), "pdbId");
    if (pdbId <= 0) {
        PdbLens_DestroyWorkspace(workspace);
        return 5;
    }

    char *allQueryJson = nullptr;
    const int allQueryHr = PdbLens_QuerySymbols(workspace, 0, PdbLensCategoryDefault,
        nullptr, 0, 20, &allQueryJson);
    JsonResult allQueryResult(allQueryJson);
    printf("QueryAll hr=0x%08X\n%s\n", static_cast<unsigned>(allQueryHr), allQueryResult.Text().c_str());

    char *queryJson = nullptr;
    hr = PdbLens_QuerySymbols(workspace, 0, PdbLensCategoryDefault,
        L"PdbLensTestStruct", 0, 20, &queryJson);
    JsonResult queryResult(queryJson);
    printf("QuerySymbols hr=0x%08X\n%s\n", static_cast<unsigned>(hr), queryResult.Text().c_str());
    if (hr < 0 || !Contains(queryResult.Text(), "PdbLensTestStruct")) {
        PdbLens_DestroyWorkspace(workspace);
        return 6;
    }

    const int symIndexId = ExtractInt(queryResult.Text(), "symIndexId");
    if (symIndexId < 0) {
        PdbLens_DestroyWorkspace(workspace);
        return 7;
    }

    char *definitionJson = nullptr;
    hr = PdbLens_GetDefinition(workspace, static_cast<uint32_t>(pdbId),
        static_cast<uint32_t>(symIndexId), &definitionJson);
    JsonResult definitionResult(definitionJson);
    printf("GetDefinition hr=0x%08X\n%s\n", static_cast<unsigned>(hr), definitionResult.Text().c_str());
    if (hr < 0 || !Contains(definitionResult.Text(), "PdbLensTestChild") ||
        !Contains(definitionResult.Text(), "\"links\":[{")) {
        PdbLens_DestroyWorkspace(workspace);
        return 8;
    }

    HRESULT wrongThreadResult = S_OK;
    std::thread worker([&]() {
        char *wrongThreadJson = nullptr;
        wrongThreadResult = PdbLens_QuerySymbols(workspace, 0, PdbLensCategoryDefault,
            nullptr, 0, 10, &wrongThreadJson);
        PdbLens_FreeString(wrongThreadJson);
    });
    worker.join();
    printf("WrongThread hr=0x%08X\n", static_cast<unsigned>(wrongThreadResult));
    if (wrongThreadResult != RPC_E_WRONG_THREAD) {
        PdbLens_DestroyWorkspace(workspace);
        return 9;
    }

    PdbLens_DestroyWorkspace(workspace);
    printf("All native tests passed\n");
    return 0;
}
