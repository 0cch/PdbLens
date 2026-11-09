#ifndef PDBLENS_NATIVE_EXPORTS
#define PDBLENS_NATIVE_EXPORTS
#endif
#include "PdbLensNative.h"

#include "SymWrap.h"

#include <algorithm>
#include <cstdlib>
#include <cwchar>
#include <filesystem>
#include <memory>
#include <mutex>
#include <shlwapi.h>
#include <sstream>
#include <unordered_set>
#include <string>
#include <vector>

#pragma comment(lib, "shlwapi.lib")

namespace {

struct SymbolRef {
    uint32_t SymIndexId = 0;
    uint32_t Category = 0;
};

struct PdbEntry {
    uint32_t Id = 0;
    std::wstring Path;
    std::wstring Name;
    std::unique_ptr<SymLoader> Loader;
    CComPtr<IDiaSymbol> GlobalSymbol;
    std::vector<SymbolRef> Symbols;
};

struct SymbolRow {
    uint32_t PdbId = 0;
    uint32_t SymIndexId = 0;
    uint32_t Category = 0;
    CString Name;
};

struct LinkSpan {
    int Start = 0;
    int Length = 0;
    uint32_t TargetPdbId = 0;
    uint32_t TargetSymIndexId = 0;
    CString TargetName;
    uint32_t TargetCategory = 0;
};

std::string WideToUtf8(const std::wstring &value)
{
    if (value.empty()) {
        return std::string();
    }

    const int size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS,
        value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (size <= 0) {
        return std::string();
    }

    std::string result(static_cast<size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS,
        value.data(), static_cast<int>(value.size()), result.data(), size, nullptr, nullptr);
    return result;
}

std::wstring WideFromUtf8(const char *value)
{
    if (value == nullptr || *value == '\0') {
        return std::wstring();
    }

    const int size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value, -1, nullptr, 0);
    if (size <= 0) {
        return L"Native exception";
    }

    std::wstring result(static_cast<size_t>(size), L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value, -1, result.data(), size);
    result.resize(wcslen(result.c_str()));
    return result;
}

std::string JsonQuote(const std::wstring &value)
{
    const std::string utf8 = WideToUtf8(value);
    std::ostringstream output;
    output << '"';
    for (const unsigned char ch : utf8) {
        switch (ch) {
        case '"': output << "\\\""; break;
        case '\\': output << "\\\\"; break;
        case '\b': output << "\\b"; break;
        case '\f': output << "\\f"; break;
        case '\n': output << "\\n"; break;
        case '\r': output << "\\r"; break;
        case '\t': output << "\\t"; break;
        default:
            if (ch < 0x20) {
                const char hex[] = "0123456789ABCDEF";
                output << "\\u00" << hex[(ch >> 4) & 0x0F] << hex[ch & 0x0F];
            } else {
                output << static_cast<char>(ch);
            }
            break;
        }
    }
    output << '"';
    return output.str();
}

std::string JsonQuote(const CString &value)
{
    return JsonQuote(std::wstring(value.GetString(), static_cast<size_t>(value.GetLength())));
}

std::string JsonQuote(const wchar_t *value)
{
    return JsonQuote(std::wstring(value != nullptr ? value : L""));
}

void SetResult(char **resultJson, const std::string &json)
{
    if (resultJson == nullptr) {
        return;
    }

    char *copy = static_cast<char *>(malloc(json.size() + 1));
    if (copy == nullptr) {
        *resultJson = nullptr;
        return;
    }
    memcpy(copy, json.data(), json.size());
    copy[json.size()] = '\0';
    *resultJson = copy;
}

std::string ErrorJson(HRESULT hr, const std::wstring &message)
{
    std::ostringstream output;
    output << "{\"ok\":false,\"hr\":";
    output << static_cast<long>(hr);
    output << ",\"error\":";
    output << JsonQuote(message);
    output << '}';
    return output.str();
}

const wchar_t *KindName(uint32_t category)
{
    switch (category) {
    case PdbLensCategoryStruct: return L"struct";
    case PdbLensCategoryClass: return L"class";
    case PdbLensCategoryUnion: return L"union";
    case PdbLensCategoryEnum: return L"enum";
    case PdbLensCategoryTypedef: return L"typedef";
    case PdbLensCategoryFunction: return L"function";
    case PdbLensCategoryData: return L"data";
    case PdbLensCategoryPublicSymbol: return L"publicSymbol";
    default: return L"unknown";
    }
}

const wchar_t *DisplayKindName(uint32_t category)
{
    switch (category) {
    case PdbLensCategoryStruct: return L"STRUCT";
    case PdbLensCategoryClass: return L"CLASS";
    case PdbLensCategoryUnion: return L"UNION";
    case PdbLensCategoryEnum: return L"ENUM";
    case PdbLensCategoryTypedef: return L"TYPEDEF";
    case PdbLensCategoryFunction: return L"FUNC";
    case PdbLensCategoryData: return L"DATA";
    case PdbLensCategoryPublicSymbol: return L"PUBLIC";
    default: return L"UNKNOWN";
    }
}

uint32_t CategoryForUdtKind(ULONG udtKind)
{
    switch (udtKind) {
    case UdtClass:
    case UdtInterface:
        return PdbLensCategoryClass;
    case UdtUnion:
    case UdtTaggedUnion:
        return PdbLensCategoryUnion;
    case UdtStruct:
    default:
        return PdbLensCategoryStruct;
    }
}

HRESULT GetSymbolById(IDiaSession *session, uint32_t symIndexId, CComPtr<IDiaSymbol> &symbol)
{
    symbol.Release();
    if (session == nullptr) {
        return E_UNEXPECTED;
    }
    return session->symbolById(symIndexId, &symbol);
}

HRESULT GetSymbolName(IDiaSymbol *symbol, CString &name)
{
    name.Empty();
    if (symbol == nullptr) {
        return E_UNEXPECTED;
    }
    CComBSTR value;
    HRESULT hr = symbol->get_name(&value);
    if (SUCCEEDED(hr) && value.m_str != nullptr) {
        name = value.m_str;
    }
    return hr;
}

class Workspace {
public:
    Workspace()
        : _OwnerThread(GetCurrentThreadId())
    {
    }

    HRESULT OpenPdb(const std::wstring &path, std::string &json)
    {
        if (!OnOwnerThread()) {
            json = ErrorJson(RPC_E_WRONG_THREAD, L"PDB workspace was accessed from a different thread");
            return RPC_E_WRONG_THREAD;
        }

        std::lock_guard<std::mutex> lock(_Mutex);
        if (path.empty()) {
            json = ErrorJson(E_INVALIDARG, L"PDB path is empty");
            return E_INVALIDARG;
        }

        auto entry = std::make_unique<PdbEntry>();
        entry->Id = _NextPdbId++;
        entry->Path = path;
        entry->Name = std::filesystem::path(path).filename().wstring();
        entry->Loader = std::make_unique<SymLoader>();

        HRESULT hr = entry->Loader->Init();
        if (FAILED(hr)) {
            json = ErrorJson(hr, L"Failed to initialize DIA");
            return hr;
        }

        hr = entry->Loader->Open(path.c_str());
        if (FAILED(hr)) {
            json = ErrorJson(hr, L"Failed to load PDB");
            return hr;
        }

        hr = entry->Loader->GetSymbol(entry->GlobalSymbol);
        if (FAILED(hr) || entry->GlobalSymbol == nullptr) {
            json = ErrorJson(FAILED(hr) ? hr : E_UNEXPECTED, L"Failed to get PDB global scope");
            return FAILED(hr) ? hr : E_UNEXPECTED;
        }

        hr = BuildSymbolIndex(*entry);
        if (FAILED(hr)) {
            json = ErrorJson(hr, L"Failed to index PDB symbols");
            return hr;
        }

        const uint32_t pdbId = entry->Id;
        const std::wstring pdbName = entry->Name;
        const size_t symbolCount = entry->Symbols.size();
        _Pdbs.push_back(std::move(entry));

        std::ostringstream output;
        output << "{\"ok\":true,\"pdbId\":" << pdbId
               << ",\"path\":" << JsonQuote(path)
               << ",\"name\":" << JsonQuote(pdbName)
               << ",\"symbolCount\":" << symbolCount
               << '}';
        json = output.str();
        return S_OK;
    }

    HRESULT ClosePdb(uint32_t pdbId, std::string &json)
    {
        if (!OnOwnerThread()) {
            json = ErrorJson(RPC_E_WRONG_THREAD, L"PDB workspace was accessed from a different thread");
            return RPC_E_WRONG_THREAD;
        }

        std::lock_guard<std::mutex> lock(_Mutex);
        const auto it = std::find_if(_Pdbs.begin(), _Pdbs.end(),
            [pdbId](const std::unique_ptr<PdbEntry> &entry) { return entry->Id == pdbId; });
        if (it == _Pdbs.end()) {
            json = ErrorJson(E_INVALIDARG, L"PDB was not found");
            return E_INVALIDARG;
        }

        _Pdbs.erase(it);
        json = "{\"ok\":true}";
        return S_OK;
    }

    HRESULT QuerySymbols(uint32_t pdbFilter, uint32_t categories, const std::wstring &searchText,
        uint32_t offset, uint32_t limit, std::string &json)
    {
        if (!OnOwnerThread()) {
            json = ErrorJson(RPC_E_WRONG_THREAD, L"PDB workspace was accessed from a different thread");
            return RPC_E_WRONG_THREAD;
        }

        std::lock_guard<std::mutex> lock(_Mutex);
        if (categories == 0) {
            categories = PdbLensCategoryDefault;
        }
        limit = std::clamp<uint32_t>(limit, 1, 1000);

        std::vector<SymbolRow> items;
        uint32_t total = 0;
        for (const auto &entry : _Pdbs) {
            if (pdbFilter != 0 && entry->Id != pdbFilter) {
                continue;
            }
            CComPtr<IDiaSession> session;
            if (FAILED(entry->Loader->GetSession(session)) || session == NULL) {
                continue;
            }
            for (const SymbolRef &symbol : entry->Symbols) {
                if ((symbol.Category & categories) == 0) {
                    continue;
                }

                CComPtr<IDiaSymbol> diaSymbol;
                if (FAILED(GetSymbolById(session, symbol.SymIndexId, diaSymbol))) {
                    continue;
                }
                CString name;
                if (FAILED(GetSymbolName(diaSymbol, name)) || name.IsEmpty()) {
                    continue;
                }
                if (!searchText.empty() && StrStrIW(name.GetString(), searchText.c_str()) == nullptr) {
                    continue;
                }

                if (total >= offset && items.size() < limit) {
                    items.push_back({ entry->Id, symbol.SymIndexId, symbol.Category, name });
                }
                ++total;
            }
        }

        std::ostringstream output;
        output << "{\"ok\":true,\"offset\":" << offset
               << ",\"limit\":" << limit
               << ",\"total\":" << total
               << ",\"hasMore\":" << ((offset + items.size()) < total ? "true" : "false")
               << ",\"items\":[";
        for (size_t i = 0; i < items.size(); ++i) {
            if (i != 0) {
                output << ',';
            }
            const SymbolRow &item = items[i];
            output << "{\"pdbId\":" << item.PdbId
                   << ",\"symIndexId\":" << item.SymIndexId
                   << ",\"name\":" << JsonQuote(item.Name)
                   << ",\"kind\":" << JsonQuote(KindName(item.Category))
                   << ",\"displayKind\":" << JsonQuote(DisplayKindName(item.Category))
                   << '}';
        }
        output << "]}";
        json = output.str();
        return S_OK;
    }

    HRESULT LocateSymbol(uint32_t pdbFilter, uint32_t pdbId, uint32_t symIndexId, uint32_t categories,
        std::string &json)
    {
        if (!OnOwnerThread()) {
            json = ErrorJson(RPC_E_WRONG_THREAD, L"PDB workspace was accessed from a different thread");
            return RPC_E_WRONG_THREAD;
        }

        std::lock_guard<std::mutex> lock(_Mutex);
        if (categories == 0) {
            categories = PdbLensCategoryDefault;
        }

        uint32_t ordinal = 0;
        bool found = false;
        for (const auto &entry : _Pdbs) {
            if (pdbFilter != 0 && entry->Id != pdbFilter) {
                continue;
            }
            for (const SymbolRef &symbol : entry->Symbols) {
                if ((symbol.Category & categories) == 0) {
                    continue;
                }
                if (!found && entry->Id == pdbId && symbol.SymIndexId == symIndexId) {
                    found = true;
                } else if (!found) {
                    ++ordinal;
                }
            }
        }

        if (!found) {
            json = ErrorJson(E_NOT_SET, L"Target symbol is not present in the selected symbol categories");
            return E_NOT_SET;
        }

        std::ostringstream output;
        output << "{\"ok\":true,\"pdbId\":" << pdbId
               << ",\"symIndexId\":" << symIndexId
               << ",\"ordinal\":" << ordinal
               << '}';
        json = output.str();
        return S_OK;
    }

    HRESULT GetDefinition(uint32_t pdbId, uint32_t symIndexId, std::string &json)
    {
        if (!OnOwnerThread()) {
            json = ErrorJson(RPC_E_WRONG_THREAD, L"PDB workspace was accessed from a different thread");
            return RPC_E_WRONG_THREAD;
        }

        std::lock_guard<std::mutex> lock(_Mutex);
        PdbEntry *entry = FindPdb(pdbId);
        if (entry == nullptr) {
            json = ErrorJson(E_INVALIDARG, L"PDB was not found");
            return E_INVALIDARG;
        }

        CComPtr<IDiaSession> session;
        HRESULT hr = entry->Loader->GetSession(session);
        if (FAILED(hr)) {
            json = ErrorJson(hr, L"DIA session is unavailable");
            return hr;
        }

        CComPtr<IDiaSymbol> symbol;
        hr = GetSymbolById(session, symIndexId, symbol);
        if (FAILED(hr) || symbol == nullptr) {
            json = ErrorJson(FAILED(hr) ? hr : E_UNEXPECTED, L"Symbol was not found");
            return FAILED(hr) ? hr : E_UNEXPECTED;
        }

        CString name;
        hr = GetSymbolName(symbol, name);
        if (FAILED(hr)) {
            json = ErrorJson(hr, L"Failed to read symbol name");
            return hr;
        }

        std::unique_ptr<SymBase> wrapped(SymBase::SymNew(symbol));
        if (!wrapped) {
            json = ErrorJson(E_OUTOFMEMORY, L"Failed to create symbol wrapper");
            return E_OUTOFMEMORY;
        }

        ULONG tag = SymTagNull;
        hr = wrapped->GetTag(tag);
        if (FAILED(hr)) {
            json = ErrorJson(hr, L"Failed to read symbol tag");
            return hr;
        }

        uint32_t category = CategoryFor(entry, symIndexId);
        if (category == 0) {
            category = PdbLensCategoryTypedef;
        }

        CString code;
        std::vector<LinkSpan> links;
        if (tag == SymTagUDT) {
            hr = BuildUdtDefinition(*entry, *wrapped, code, links);
        } else if (tag == SymTagFunction || tag == SymTagEnum || tag == SymTagTypedef) {
            hr = wrapped->GetDef(code);
        } else if (tag == SymTagData) {
            hr = wrapped->GetDecl(code);
        } else if (tag == SymTagPublicSymbol) {
            code = name;
        } else {
            hr = wrapped->GetDecl(code);
        }

        if (FAILED(hr)) {
            json = ErrorJson(hr, L"Failed to build symbol definition");
            return hr;
        }
        if (code.IsEmpty()) {
            code = name;
        }

        std::ostringstream output;
        output << "{\"ok\":true,\"pdbId\":" << pdbId
               << ",\"symIndexId\":" << symIndexId
               << ",\"name\":" << JsonQuote(name)
               << ",\"kind\":" << JsonQuote(KindName(category))
               << ",\"displayKind\":" << JsonQuote(DisplayKindName(category))
               << ",\"code\":" << JsonQuote(code)
               << ",\"links\":[";
        for (size_t i = 0; i < links.size(); ++i) {
            if (i != 0) {
                output << ',';
            }
            const LinkSpan &link = links[i];
            output << "{\"start\":" << link.Start
                   << ",\"length\":" << link.Length
                   << ",\"targetPdbId\":" << link.TargetPdbId
                   << ",\"targetSymIndexId\":" << link.TargetSymIndexId
                   << ",\"targetName\":" << JsonQuote(link.TargetName)
                   << ",\"targetKind\":" << JsonQuote(KindName(link.TargetCategory))
                   << '}';
        }
        output << "]}";
        json = output.str();
        return S_OK;
    }

private:
    bool OnOwnerThread() const
    {
        return GetCurrentThreadId() == _OwnerThread;
    }

    PdbEntry *FindPdb(uint32_t pdbId)
    {
        for (const auto &entry : _Pdbs) {
            if (entry->Id == pdbId) {
                return entry.get();
            }
        }
        return nullptr;
    }

    uint32_t CategoryFor(PdbEntry *entry, uint32_t symIndexId) const
    {
        for (const SymbolRef &symbol : entry->Symbols) {
            if (symbol.SymIndexId == symIndexId) {
                return symbol.Category;
            }
        }
        return 0;
    }

    bool IsIndexed(PdbEntry *entry, uint32_t symIndexId) const
    {
        return CategoryFor(entry, symIndexId) != 0;
    }

    void AddIndexedSymbol(PdbEntry &entry, IDiaSymbol *symbol, enum SymTagEnum tag,
        std::unordered_set<DWORD> &visited)
    {
        if (symbol == nullptr) {
            return;
        }

        DWORD symIndexId = 0;
        if (FAILED(symbol->get_symIndexId(&symIndexId)) || !visited.insert(symIndexId).second) {
            return;
        }

        uint32_t category = 0;
        if (tag == SymTagUDT) {
            ULONG udtKind = UdtStruct;
            if (FAILED(symbol->get_udtKind(&udtKind))) {
                return;
            }
            category = CategoryForUdtKind(udtKind);
        } else if (tag == SymTagEnum) {
            category = PdbLensCategoryEnum;
        } else if (tag == SymTagTypedef) {
            category = PdbLensCategoryTypedef;
        } else if (tag == SymTagFunction) {
            category = PdbLensCategoryFunction;
        } else if (tag == SymTagData) {
            category = PdbLensCategoryData;
        } else if (tag == SymTagPublicSymbol) {
            category = PdbLensCategoryPublicSymbol;
        }

        if (category != 0) {
            entry.Symbols.push_back({ symIndexId, category });
        }
    }

    void CollectScopeSymbols(PdbEntry &entry, IDiaSymbol *scope, bool includeData,
        int depth, std::unordered_set<DWORD> &visited)
    {
        if (scope == nullptr || depth > 64) {
            return;
        }

        const enum SymTagEnum tags[] = {
            SymTagUDT, SymTagEnum, SymTagTypedef, SymTagFunction,
            SymTagData, SymTagPublicSymbol
        };

        std::vector<CComPtr<IDiaSymbol>> udts;
        for (const enum SymTagEnum tag : tags) {
            if (tag == SymTagData && !includeData) {
                continue;
            }

            CComPtr<IDiaEnumSymbols> enumerator;
            if (FAILED(scope->findChildren(tag, NULL, nsNone, &enumerator)) || !enumerator) {
                continue;
            }

            SymEnumTool tool(enumerator);
            for (;;) {
                CComPtr<IDiaSymbol> symbol;
                const HRESULT hr = tool.Next(symbol);
                if (hr == S_FALSE || FAILED(hr) || !symbol) {
                    break;
                }
                AddIndexedSymbol(entry, symbol, tag, visited);
                if (tag == SymTagUDT) {
                    udts.push_back(symbol);
                }
            }
        }

        for (const CComPtr<IDiaSymbol> &udt : udts) {
            CollectScopeSymbols(entry, udt, false, depth + 1, visited);
        }

        CComPtr<IDiaEnumSymbols> compilands;
        if (SUCCEEDED(scope->findChildren(SymTagCompiland, NULL, nsNone, &compilands)) &&
            compilands != nullptr) {
            SymEnumTool compilandTool(compilands);
            for (;;) {
                CComPtr<IDiaSymbol> compiland;
                const HRESULT hr = compilandTool.Next(compiland);
                if (hr == S_FALSE || FAILED(hr) || !compiland) {
                    break;
                }
                CollectScopeSymbols(entry, compiland, true, depth + 1, visited);
            }
        }
    }

    HRESULT BuildSymbolIndex(PdbEntry &entry)
    {
        std::unordered_set<DWORD> visited;
        CollectScopeSymbols(entry, entry.GlobalSymbol, true, 0, visited);
        return S_OK;
    }
    void CollectTypeCandidates(IDiaSymbol *type,
        std::vector<CComPtr<IDiaSymbol>> &candidates, int depth)
    {
        if (type == nullptr || depth > 16) {
            return;
        }

        ULONG tag = SymTagNull;
        if (FAILED(type->get_symTag(&tag))) {
            return;
        }

        if (tag == SymTagUDT || tag == SymTagEnum || tag == SymTagTypedef) {
            CComBSTR name;
            if (SUCCEEDED(type->get_name(&name)) && name.m_str != nullptr && name.Length() > 0) {
                candidates.push_back(type);
            }
        }

        if (tag == SymTagPointerType || tag == SymTagArrayType ||
            tag == SymTagTypedef || tag == SymTagFunctionArgType) {
            CComPtr<IDiaSymbol> next;
            if (SUCCEEDED(type->get_type(&next)) && next != nullptr) {
                CollectTypeCandidates(next, candidates, depth + 1);
            }
        }
    }

    bool TryAddTypeLink(PdbEntry &entry, IDiaSymbol *type, const CString &member,
        int codeOffset, std::vector<LinkSpan> &links)
    {
        std::vector<CComPtr<IDiaSymbol>> candidates;
        CollectTypeCandidates(type, candidates, 0);
        for (const CComPtr<IDiaSymbol> &candidate : candidates) {
            DWORD targetId = 0;
            if (FAILED(candidate->get_symIndexId(&targetId)) || !IsIndexed(&entry, targetId)) {
                continue;
            }

            CString targetName;
            if (FAILED(GetSymbolName(candidate, targetName)) || targetName.IsEmpty()) {
                continue;
            }

            const int position = member.Find(targetName.GetString());
            if (position < 0) {
                continue;
            }

            LinkSpan link;
            link.Start = codeOffset + position;
            link.Length = targetName.GetLength();
            link.TargetPdbId = entry.Id;
            link.TargetSymIndexId = targetId;
            link.TargetName = targetName;
            link.TargetCategory = CategoryFor(&entry, targetId);
            links.push_back(link);
            return true;
        }
        return false;
    }

    HRESULT BuildUdtDefinition(PdbEntry &entry, SymBase &symbol,
        CString &code, std::vector<LinkSpan> &links)
    {
        CString header;
        HRESULT hr = symbol.GetDecl(header);
        if (FAILED(hr) || header.IsEmpty()) {
            return FAILED(hr) ? hr : E_UNEXPECTED;
        }

        code.Empty();
        code += header;
        code += L" {\r\n";

        CComPtr<IDiaEnumSymbols> enumerator;
        hr = symbol.GetEnum(enumerator, SymTagNull);
        if (FAILED(hr)) {
            return hr;
        }

        if (enumerator) {
            SymEnumTool tool(enumerator);
            for (;;) {
                CComPtr<IDiaSymbol> child;
                hr = tool.Next(child);
                if (hr == S_FALSE || FAILED(hr) || !child) {
                    break;
                }

                ULONG tag = SymTagNull;
                if (FAILED(child->get_symTag(&tag))) {
                    continue;
                }
                if (tag != SymTagData && tag != SymTagFunction && tag != SymTagUDT &&
                    tag != SymTagEnum && tag != SymTagTypedef) {
                    continue;
                }

                std::unique_ptr<SymBase> wrapped(SymBase::SymNew(child));
                if (!wrapped) {
                    continue;
                }

                CString member;
                hr = wrapped->GetDecl(member);
                if (FAILED(hr) || member.IsEmpty()) {
                    continue;
                }
                if (member.GetAt(member.GetLength() - 1) != L';') {
                    member += L";";
                }

                code += L"\t";
                const int codeOffset = code.GetLength();
                code += member;
                code += L"\r\n";

                if (tag == SymTagData) {
                    CComPtr<IDiaSymbol> type;
                    if (SUCCEEDED(child->get_type(&type)) && type != nullptr) {
                        TryAddTypeLink(entry, type, member, codeOffset, links);
                    }
                } else {
                    DWORD targetId = 0;
                    CString targetName;
                    if (SUCCEEDED(child->get_symIndexId(&targetId)) &&
                        IsIndexed(&entry, targetId) &&
                        SUCCEEDED(GetSymbolName(child, targetName)) &&
                        !targetName.IsEmpty()) {
                        const int position = member.Find(targetName.GetString());
                        if (position >= 0) {
                            LinkSpan link;
                            link.Start = codeOffset + position;
                            link.Length = targetName.GetLength();
                            link.TargetPdbId = entry.Id;
                            link.TargetSymIndexId = targetId;
                            link.TargetName = targetName;
                            link.TargetCategory = CategoryFor(&entry, targetId);
                            links.push_back(link);
                        }
                    }
                }
            }
        }

        code += L"};";
        return S_OK;
    }

    DWORD _OwnerThread = 0;
    uint32_t _NextPdbId = 1;
    std::mutex _Mutex;
    std::vector<std::unique_ptr<PdbEntry>> _Pdbs;
};

template <typename Operation>
int Execute(Operation &&operation, Workspace *workspace, char **resultJson)
{
    if (resultJson == nullptr) {
        return E_POINTER;
    }
    *resultJson = nullptr;
    if (workspace == nullptr) {
        SetResult(resultJson, ErrorJson(E_POINTER, L"Workspace is null"));
        return E_POINTER;
    }

    try {
        std::string json;
        const HRESULT hr = operation(*workspace, json);
        SetResult(resultJson, json);
        return hr;
    } catch (const std::exception &error) {
        SetResult(resultJson, ErrorJson(E_FAIL, WideFromUtf8(error.what())));
        return E_FAIL;
    } catch (...) {
        SetResult(resultJson, ErrorJson(E_FAIL, L"Unhandled native exception"));
        return E_FAIL;
    }
}

} // namespace

PDBLENS_API void* PdbLens_CreateWorkspace()
{
    try {
        return new Workspace();
    } catch (...) {
        return nullptr;
    }
}

PDBLENS_API void PdbLens_DestroyWorkspace(void* workspace)
{
    delete static_cast<Workspace *>(workspace);
}

PDBLENS_API int PdbLens_OpenPdb(void* workspace, const wchar_t* path, char** resultJson)
{
    const std::wstring pathValue = path != nullptr ? path : L"";
    return Execute([pathValue](Workspace &value, std::string &json) {
        return value.OpenPdb(pathValue, json);
    }, static_cast<Workspace *>(workspace), resultJson);
}

PDBLENS_API int PdbLens_ClosePdb(void* workspace, uint32_t pdbId, char** resultJson)
{
    return Execute([pdbId](Workspace &value, std::string &json) {
        return value.ClosePdb(pdbId, json);
    }, static_cast<Workspace *>(workspace), resultJson);
}

PDBLENS_API int PdbLens_QuerySymbols(void* workspace, uint32_t pdbFilter,
    uint32_t categories, const wchar_t* searchText, uint32_t offset, uint32_t limit,
    char** resultJson)
{
    const std::wstring search = searchText != nullptr ? searchText : L"";
    return Execute([pdbFilter, categories, search, offset, limit](Workspace &value, std::string &json) {
        return value.QuerySymbols(pdbFilter, categories, search, offset, limit, json);
    }, static_cast<Workspace *>(workspace), resultJson);
}

PDBLENS_API int PdbLens_LocateSymbol(void* workspace, uint32_t pdbFilter, uint32_t pdbId,
    uint32_t symIndexId, uint32_t categories, char** resultJson)
{
    return Execute([pdbFilter, pdbId, symIndexId, categories](Workspace &value, std::string &json) {
        return value.LocateSymbol(pdbFilter, pdbId, symIndexId, categories, json);
    }, static_cast<Workspace *>(workspace), resultJson);
}

PDBLENS_API int PdbLens_GetDefinition(void* workspace, uint32_t pdbId,
    uint32_t symIndexId, char** resultJson)
{
    return Execute([pdbId, symIndexId](Workspace &value, std::string &json) {
        return value.GetDefinition(pdbId, symIndexId, json);
    }, static_cast<Workspace *>(workspace), resultJson);
}

PDBLENS_API void PdbLens_FreeString(char* value)
{
    free(value);
}