#include <stdio.h>
#include <string.h>
#include <windows.h>
#include <winsvc.h>

static FILE *logf;

static void logmsg(const char *text) {
    if (!logf) {
        logf = fopen("C:\\Windows\\Temp\\tortoise-rdp.log", "a");
    }
    if (!logf) {
        return;
    }
    fputs(text, logf);
    fputc('\n', logf);
    fflush(logf);
}

static int set_sz(const char *path, const char *name, const char *value) {
    HKEY key;
    char line[320];
    LONG open_rc = RegOpenKeyExA(HKEY_LOCAL_MACHINE, path, 0, KEY_SET_VALUE, &key);
    if (open_rc != ERROR_SUCCESS) {
        snprintf(line, sizeof line, "reg open %s rc=%ld", name, open_rc);
        logmsg(line);
        return 0;
    }
    LONG set_rc = RegSetValueExA(key, name, 0, REG_EXPAND_SZ, (const BYTE *)value, (DWORD)strlen(value) + 1);
    RegCloseKey(key);
    snprintf(line, sizeof line, "reg %s rc=%ld", name, set_rc);
    logmsg(line);
    return set_rc == ERROR_SUCCESS;
}

static int set_dword(const char *path, const char *name, DWORD value) {
    HKEY key;
    char line[256];
    LONG open_rc = RegOpenKeyExA(HKEY_LOCAL_MACHINE, path, 0, KEY_SET_VALUE, &key);
    if (open_rc != ERROR_SUCCESS) {
        snprintf(line, sizeof line, "reg open %s rc=%ld", name, open_rc);
        logmsg(line);
        return 0;
    }
    LONG set_rc = RegSetValueExA(key, name, 0, REG_DWORD, (const BYTE *)&value, sizeof value);
    RegCloseKey(key);
    snprintf(line, sizeof line, "reg %s=%lu rc=%ld", name, value, set_rc);
    logmsg(line);
    return set_rc == ERROR_SUCCESS;
}

static DWORD service_state(SC_HANDLE service) {
    SERVICE_STATUS_PROCESS status;
    DWORD needed = 0;
    if (!QueryServiceStatusEx(service, SC_STATUS_PROCESS_INFO, (BYTE *)&status, sizeof status, &needed)) {
        return 0;
    }
    return status.dwCurrentState;
}

static void log_attr(const char *path) {
    char line[320];
    DWORD attr = GetFileAttributesA(path);
    snprintf(line, sizeof line, "attr %s = %lu err=%lu", path, attr, GetLastError());
    logmsg(line);
}

static void start_termservice(void) {
    char line[160];
    SC_HANDLE scm = OpenSCManagerA(NULL, NULL, SC_MANAGER_CONNECT);
    if (!scm) {
        snprintf(line, sizeof line, "scm err=%lu", GetLastError());
        logmsg(line);
        return;
    }
    SC_HANDLE service = OpenServiceA(scm, "TermService", SERVICE_START | SERVICE_QUERY_STATUS);
    if (!service) {
        snprintf(line, sizeof line, "open TermService err=%lu", GetLastError());
        logmsg(line);
        CloseServiceHandle(scm);
        return;
    }
    if (!StartServiceA(service, 0, NULL)) {
        snprintf(line, sizeof line, "start TermService err=%lu state=%lu", GetLastError(), service_state(service));
        logmsg(line);
    } else {
        logmsg("start TermService accepted");
    }
    for (int i = 0; i < 10; i++) {
        DWORD state = service_state(service);
        snprintf(line, sizeof line, "TermService state=%lu", state);
        logmsg(line);
        if (state == SERVICE_RUNNING) {
            FILE *tag = fopen("C:\\Windows\\TortoiseLabRdpReady.tag", "w");
            if (tag) {
                fputs("ready\n", tag);
                fclose(tag);
            }
            break;
        }
        Sleep(2000);
    }
    CloseServiceHandle(service);
    CloseServiceHandle(scm);
}

int main(void) {
    logmsg("=== exe shell ===");
    log_attr("C:\\Windows\\System32\\svchost.exe");
    log_attr("C:\\Windows\\System32\\termsrv.dll");
    log_attr("C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe");
    log_attr("C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\System.Core.dll");
    HMODULE termsrv = LoadLibraryA("termsrv.dll");
    char load_line[160];
    snprintf(load_line, sizeof load_line, "LoadLibrary termsrv=%p err=%lu", (void *)termsrv, GetLastError());
    logmsg(load_line);
    if (termsrv) {
        FreeLibrary(termsrv);
    }
    set_dword("System\\CurrentControlSet\\Control\\Terminal Server", "fDenyTSConnections", 0);
    set_dword("System\\CurrentControlSet\\Control\\Terminal Server\\WinStations\\RDP-Tcp", "UserAuthentication", 0);
    set_dword("System\\CurrentControlSet\\Control\\Terminal Server\\WinStations\\RDP-Tcp", "SecurityLayer", 1);
    start_termservice();
    set_sz("System\\CurrentControlSet\\Services\\TermService", "ImagePath", "C:\\Windows\\System32\\svchost.exe -k termsvcs");
    set_sz("System\\CurrentControlSet\\Services\\TermService\\Parameters", "ServiceDll", "C:\\Windows\\System32\\termsrv.dll");
    start_termservice();
    if (logf) {
        fclose(logf);
        logf = NULL;
    }
    STARTUPINFOA startup;
    PROCESS_INFORMATION process;
    ZeroMemory(&startup, sizeof startup);
    startup.cb = sizeof startup;
    ZeroMemory(&process, sizeof process);
    char dism[] = "C:\\Windows\\System32\\cmd.exe /c C:\\Windows\\System32\\dism.exe /online /cleanup-image /restorehealth /source:wim:D:\\sources\\install.wim:4 /limitaccess > C:\\Windows\\Temp\\tortoise-dism.log 2>&1";
    if (!CreateProcessA(NULL, dism, NULL, NULL, FALSE, 0, NULL, NULL, &startup, &process)) {
        logmsg("dism launch failed");
    } else {
        WaitForSingleObject(process.hProcess, 20 * 60 * 1000);
        CloseHandle(process.hProcess);
        CloseHandle(process.hThread);
    }
    if (logf) {
        fclose(logf);
    }
    return 0;
}
