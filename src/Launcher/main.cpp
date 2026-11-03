#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <vector>
#include <shlobj.h>
#include <cstring>

// The bootstrapper reads the same standalone language preference as the app.
static int LanguageIndex()
{
    PWSTR local = nullptr;
    if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local))) return 0;
    const std::wstring preference = std::wstring(local) + L"\\LMUDualSenseHaptics\\ui-language.txt";
    CoTaskMemFree(local);
    HANDLE file = CreateFileW(preference.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return 0;
    char code[32]{}; DWORD read = 0;
    const BOOL success = ReadFile(file, code, sizeof(code)-1, &read, nullptr);
    CloseHandle(file);
    if (!success) return 0;
    while (read && (code[read-1]=='\n' || code[read-1]=='\r' || code[read-1]==' ')) code[--read] = 0;
    const char* languages[] = {"zh-CN", "en", "fr", "es", "ru", "ar"};
    for (int i=0; i<6; ++i) if (std::strcmp(code, languages[i])==0) return i;
    return 0;
}

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
        const wchar_t* messages[] = {
            L"无法启动 LMU DualSense Haptics。请完整解压压缩包，保留 EXE 旁的 bin 文件夹。",
            L"Cannot start LMU DualSense Haptics. Extract the entire archive and keep the bin folder beside the EXE.",
            L"Impossible de démarrer LMU DualSense Haptics. Extrayez toute l’archive et conservez le dossier bin à côté de l’EXE.",
            L"No se puede iniciar LMU DualSense Haptics. Extraiga todo el archivo y conserve la carpeta bin junto al EXE.",
            L"Не удалось запустить LMU DualSense Haptics. Распакуйте весь архив и сохраните папку bin рядом с EXE.",
            L"تعذّر تشغيل LMU DualSense Haptics. استخرج الأرشيف كاملًا وأبقِ مجلد bin بجانب ملف EXE."
        };
        const wchar_t* labels[] = {
            L"Windows 错误代码：",
            L"Windows error code: ",
            L"Code d’erreur Windows : ",
            L"Código de error de Windows: ",
            L"Код ошибки Windows: ",
            L"رمز خطأ Windows: "
        };
        const int language = LanguageIndex();
        const std::wstring message = std::wstring(messages[language]) + L"\n\n" + executable
            + L"\n" + labels[language] + std::to_wstring(error);
        MessageBoxW(nullptr, message.c_str(), L"LMU DualSense Haptics",
            MB_OK | MB_ICONERROR | (language==5 ? MB_RTLREADING | MB_RIGHT : 0));
        return 1;
    }
    CloseHandle(process.hThread);
    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD result = 1;
    GetExitCodeProcess(process.hProcess, &result);
    CloseHandle(process.hProcess);
    return static_cast<int>(result);
}
