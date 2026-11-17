#include <cstdint>
#include <cstdio>
#include <cstdlib>

// The stable GDExtension 4.1 initialization ABI keeps this probe independent of Godot's generated headers.
using Function = void (*)();
using GetProcAddress = Function (*)(const char *);
using Callback = void (*)(void *, int);
struct Initialization {
    int minimum_initialization_level;
    void *userdata;
    Callback initialize;
    Callback deinitialize;
};

static void (*free_memory)(void *) = nullptr;
static void *allocation = nullptr;

static void record(const char *step, int level = -1) {
    const char *path = std::getenv("MTP_CONTRACT_EXTENSION_LOG");
    if (path) {
        if (FILE *file = std::fopen(path, "a")) {
            std::fprintf(file, "%s:%d\n", step, level);
            std::fclose(file);
        }
    }
}

static struct Sentinel {
    ~Sentinel() {
        record("static-destructor-enter");
        if (allocation) {
            free_memory(allocation);
        }
        record("static-destructor");
    }
} sentinel;

static void initialize(void *, int level) {
    record("initialize", level);
}

static void deinitialize(void *, int level) {
    record("deinitialize", level);
}

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

extern "C" EXPORT uint8_t shutdown_probe_init(GetProcAddress get_proc_address, void *, Initialization *initialization) {
    auto allocate = reinterpret_cast<void *(*)(size_t)>(get_proc_address("mem_alloc"));
    free_memory = reinterpret_cast<void (*)(void *)>(get_proc_address("mem_free"));
    allocation = allocate(64);
    *initialization = {2, nullptr, initialize, deinitialize};
    return allocation != nullptr;
}
