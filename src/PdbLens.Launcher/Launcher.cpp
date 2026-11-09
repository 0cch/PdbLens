#include <windows.h>
#include <shellapi.h>

#include <algorithm>
#include <cctype>
#include <cwctype>
#include <filesystem>
#include <string>
#include <vector>

namespace {

constexpr wchar_t kDotNetDownloadUrl[] =
    L"https://dotnet.microsoft.com/en-us/download/dotnet/10.0";

std::wstring GetEnvironmentValue(const wchar_t *name)
{
    const DWORD required = GetEnvironmentVariableW(name, nullptr, 0);
    if (required == 0) {
        return std::wstring();
    }

    std::wstring value(required, L'\0');
    const DWORD written = GetEnvironmentVariableW(name, value.data(), required);
    if (written == 0 || written >= required) {
        return std::wstring();
    }
    value.resize(written);
    return value;
}

bool StartsWith(const std::wstring &value, const wchar_t *prefix)
{
    return value.rfind(prefix, 0) == 0;
}

bool HasDotNet10UnderRoot(const std::filesystem::path &root)
{
    std::error_code error;
    const auto runtimeRoot = root / L"shared" / L"Microsoft.WindowsDesktop.App";
    if (!std::filesystem::is_directory(runtimeRoot, error)) {
        return false;
    }

    for (std::filesystem::directory_iterator it(runtimeRoot, error), end;
         !error && it != end; it.increment(error)) {
        if (!it->is_directory(error)) {
            continue;
        }
        const std::wstring name = it->path().filename().wstring();
        if (StartsWith(name, L"10.")) {
            return true;
        }
    }
    return false;
}

std::wstring FindSystemDotNet10Root()
{
    const std::wstring forcedRoot = GetEnvironmentValue(L"PDBLENS_DOTNET_ROOT");
    if (!forcedRoot.empty()) {
        return HasDotNet10UnderRoot(std::filesystem::path(forcedRoot))
            ? forcedRoot
            : std::wstring();
    }

    std::vector<std::filesystem::path> roots;
    for (const wchar_t *variable : {L"DOTNET_ROOT_X64", L"DOTNET_ROOT"}) {
        const std::wstring value = GetEnvironmentValue(variable);
        if (!value.empty()) {
            roots.emplace_back(value);
        }
    }

    const std::wstring programFiles = GetEnvironmentValue(L"ProgramFiles");
    if (!programFiles.empty()) {
        roots.emplace_back(std::filesystem::path(programFiles) / L"dotnet");
    }

    const std::wstring localAppData = GetEnvironmentValue(L"LOCALAPPDATA");
    if (!localAppData.empty()) {
        roots.emplace_back(std::filesystem::path(localAppData) / L"Microsoft" / L"dotnet");
    }

    const std::wstring userProfile = GetEnvironmentValue(L"USERPROFILE");
    if (!userProfile.empty()) {
        roots.emplace_back(std::filesystem::path(userProfile) / L".dotnet");
    }

    std::sort(roots.begin(), roots.end());
    roots.erase(std::unique(roots.begin(), roots.end()), roots.end());
    for (const auto &root : roots) {
        if (HasDotNet10UnderRoot(root)) {
            return root.wstring();
        }
    }
    return std::wstring();
}

std::filesystem::path GetExecutableDirectory()
{
    std::vector<wchar_t> buffer(1024);
    for (;;) {
        const DWORD length = GetModuleFileNameW(nullptr, buffer.data(),
            static_cast<DWORD>(buffer.size()));
        if (length == 0) {
            return std::filesystem::current_path();
        }
        if (length < buffer.size() - 1) {
            buffer.resize(length);
            return std::filesystem::path(buffer.data()).parent_path();
        }
        buffer.resize(buffer.size() * 2);
    }
}

std::wstring QuoteArgument(const std::filesystem::path &path)
{
    return L"\"" + path.wstring() + L"\"";
}

std::wstring GetForwardedArguments()
{
    const wchar_t *cursor = GetCommandLineW();
    if (*cursor == L'"') {
        ++cursor;
        while (*cursor != L'\0' && *cursor != L'"') {
            ++cursor;
        }
        if (*cursor == L'"') {
            ++cursor;
        }
    } else {
        while (*cursor != L'\0' && !std::iswspace(*cursor)) {
            ++cursor;
        }
    }
    return cursor;
}

bool HasLocalRuntime(const std::filesystem::path &appDirectory)
{
    std::error_code error;
    return std::filesystem::is_regular_file(appDirectory / L"hostfxr.dll", error) &&
        std::filesystem::is_regular_file(appDirectory / L"hostpolicy.dll", error);
}

int LaunchManagedApp(const std::filesystem::path &appDirectory, const std::wstring &dotNetRoot)
{
    const auto appPath = appDirectory / L"PdbLens.App.exe";
    std::error_code error;
    if (!std::filesystem::is_regular_file(appPath, error)) {
        MessageBoxW(nullptr, L"PdbLens.App.exe was not found next to the launcher.",
            L"PdbLens could not start", MB_OK | MB_ICONERROR);
        return 3;
    }

    const std::wstring commandLine = QuoteArgument(appPath) + GetForwardedArguments();
    std::vector<wchar_t> mutableCommandLine(commandLine.begin(), commandLine.end());
    mutableCommandLine.push_back(L'\0');
    const std::wstring workingDirectory = appDirectory.wstring();
    if (!dotNetRoot.empty()) {
        SetEnvironmentVariableW(L"DOTNET_ROOT_X64", dotNetRoot.c_str());
        SetEnvironmentVariableW(L"DOTNET_ROOT", dotNetRoot.c_str());
    }

    STARTUPINFOW startupInfo{};
    startupInfo.cb = sizeof(startupInfo);
    PROCESS_INFORMATION processInfo{};
    if (!CreateProcessW(appPath.c_str(), mutableCommandLine.data(), nullptr, nullptr,
            FALSE, 0, nullptr, workingDirectory.c_str(), &startupInfo, &processInfo)) {
        const DWORD errorCode = GetLastError();
        const std::wstring message = L"Failed to start PdbLens.App.exe (error " +
            std::to_wstring(errorCode) + L").";
        MessageBoxW(nullptr, message.c_str(), L"PdbLens could not start",
            MB_OK | MB_ICONERROR);
        return 4;
    }

    CloseHandle(processInfo.hThread);
    WaitForSingleObject(processInfo.hProcess, INFINITE);
    DWORD exitCode = 0;
    GetExitCodeProcess(processInfo.hProcess, &exitCode);
    CloseHandle(processInfo.hProcess);
    return static_cast<int>(exitCode);
}

} // namespace

int APIENTRY wWinMain(HINSTANCE, HINSTANCE, LPWSTR, int)
{
    const auto appDirectory = GetExecutableDirectory();
    const bool localRuntimeAvailable = HasLocalRuntime(appDirectory);
    const std::wstring systemDotNetRoot = localRuntimeAvailable
        ? std::wstring()
        : FindSystemDotNet10Root();
    const bool runtimeAvailable = localRuntimeAvailable || !systemDotNetRoot.empty();
    const std::wstring commandLine = GetCommandLineW();
    if (commandLine.find(L"--runtime-check") != std::wstring::npos) {
        return runtimeAvailable ? 0 : 2;
    }

    if (!runtimeAvailable) {
        const int result = MessageBoxW(nullptr,
            L"PdbLens requires the .NET 10 Desktop Runtime (x64).\n\n"
            L"Open Microsoft's official download page now?",
            L".NET 10 is required", MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON1);
        if (result == IDYES) {
            ShellExecuteW(nullptr, L"open", kDotNetDownloadUrl, nullptr, nullptr, SW_SHOWNORMAL);
        }
        return 2;
    }

    return LaunchManagedApp(appDirectory, systemDotNetRoot);
}
