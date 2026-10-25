#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <vector>

// Keep the distribution root clear. All .NET/native dependencies live in bin.
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR arguments, int show)
{
    std::vector<wchar_t> buffer(32768);
    DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (!length || length >= buffer.size()) return 1;
    std::wstring path(buffer.data(), length);
    const auto separator = path.find_last_of(L"\\/");
    if (separator == std::wstring::npos) return 1;
    const std::wstring directory = path.substr(0, separator) + L"\\bin";
    const std::wstring executable = directory + L"\\LMUDualSenseHaptics.exe";
    std::wstring command = L"\"" + executable + L"\"";
    if (arguments && *arguments) command += std::wstring(L" ") + arguments;
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESHOWWINDOW;
    startup.wShowWindow = static_cast<WORD>(show);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, 0,
                        nullptr, directory.c_str(), &startup, &process))
    {
        const DWORD error = GetLastError();
        const std::wstring message = L"无法启动 LMU DualSense Haptics。请完整解压压缩包，保留 EXE 旁的 bin 文件夹。\n\n"
            + executable + L"\nWindows 错误代码：" + std::to_wstring(error);
        MessageBoxW(nullptr, message.c_str(), L"LMU DualSense Haptics", MB_OK | MB_ICONERROR);
        return 1;
    }
    CloseHandle(process.hThread);
    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD result = 1;
    GetExitCodeProcess(process.hProcess, &result);
    CloseHandle(process.hProcess);
    return static_cast<int>(result);
}
