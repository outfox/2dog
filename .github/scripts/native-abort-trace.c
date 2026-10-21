#define _GNU_SOURCE
#include <execinfo.h>
#include <signal.h>
#include <unistd.h>

static void trace_abort(int signal_number) {
	void *frames[64];
	signal(signal_number, SIG_DFL);
	alarm(15);
	write(STDERR_FILENO, "Native abort stack:\n", sizeof("Native abort stack:\n") - 1);
	int count = backtrace(frames, 64);
	backtrace_symbols_fd(frames, count, STDERR_FILENO);
	raise(signal_number);
}

__attribute__((constructor)) static void install_abort_trace(void) {
	// Load the unwinder before an allocator failure makes loading it unsafe.
	void *frames[1];
	backtrace(frames, 1);
	signal(SIGABRT, trace_abort);
}
