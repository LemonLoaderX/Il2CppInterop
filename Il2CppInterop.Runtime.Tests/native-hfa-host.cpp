#include <dirent.h>
#include <dlfcn.h>
#include <cstdio>
#include <string>
#include <unistd.h>

// Standalone CoreCLR embedding for the Android ABI fixture. No game or Loader.
int main(int argc, char **argv) {
    if (argc != 3) return 2;
    const std::string runtime = argv[1], application = argv[2];
    std::string tpa;
    for (const auto &directory : {runtime, application}) {
        DIR *entries = opendir(directory.c_str());
        if (!entries) return 3;
        while (auto *entry = readdir(entries)) {
            std::string name = entry->d_name;
            if (name.size() >= 4 && name.compare(name.size() - 4, 4, ".dll") == 0)
                tpa += directory + "/" + name + ":";
        }
        closedir(entries);
    }
    void *library = dlopen((runtime + "/libcoreclr.so").c_str(), RTLD_NOW | RTLD_GLOBAL);
    if (!library) { std::fprintf(stderr, "%s\n", dlerror()); return 4; }
    using Initialize = int (*)(const char *, const char *, int, const char **, const char **, void **, unsigned *);
    using Execute = int (*)(void *, unsigned, int, const char **, const char *, unsigned *);
    auto initialize = reinterpret_cast<Initialize>(dlsym(library, "coreclr_initialize"));
    auto execute = reinterpret_cast<Execute>(dlsym(library, "coreclr_execute_assembly"));
    if (!initialize || !execute) return 5;
    const char *keys[] = {"TRUSTED_PLATFORM_ASSEMBLIES", "APP_PATHS", "NATIVE_DLL_SEARCH_DIRECTORIES",
                         "System.Globalization.Invariant"};
    const std::string native = runtime + ":" + application;
    const char *values[] = {tpa.c_str(), application.c_str(), native.c_str(), "true"};
    void *host;
    unsigned domain;
    int result = initialize(argv[0], "Native HFA fixture", 4, keys, values, &host, &domain);
    if (result < 0) { std::fprintf(stderr, "initialize %x\n", result); return 6; }
    const std::string fixture = application + "/libnative-hfa.so";
    const char *arguments[] = {"--native-hfa", fixture.c_str()};
    unsigned exit_code;
    result = execute(host, domain, 2, arguments,
        (application + "/Il2CppInterop.Runtime.Tests.dll").c_str(), &exit_code);
    if (result < 0) { std::fprintf(stderr, "execute %x\n", result); return 7; }
    std::fflush(nullptr);
    _exit(static_cast<int>(exit_code));
}
