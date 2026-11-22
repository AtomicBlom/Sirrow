// AAudio's callbacks, forwarded to C#.
//
// Android builds libaaudio with Control-Flow Integrity: before it calls a stream's data or error callback it checks the
// callback's address against the shadow bionic keeps for every loaded library, and a call to code outside any library
// faults (SIGSEGV in __cfi_slowpath). A C# [UnmanagedCallersOnly] method's entry point is a stub in memory the .NET
// runtime allocated, in no library, so AAudio cannot call one. It calls these instead: they live in a library, built
// without CFI so bionic marks it unchecked, and their own call into C# is not checked at all.
//
// The stream's user data is a loom_aaudio_target the C# side allocates, naming the C# callbacks and their own user data.
// Rebuild with build.ps1 beside this file (needs the Android NDK); the libraries it makes are checked in under lib/.

#include <stdint.h>

typedef struct loom_aaudio_target
{
	int32_t (*data)(void* stream, void* user, void* audio, int32_t frames);
	void (*error)(void* stream, void* user, int32_t error);
	void* user;
} loom_aaudio_target;

__attribute__((visibility("default")))
int32_t loom_aaudio_data(void* stream, void* target, void* audio, int32_t frames)
{
	const loom_aaudio_target* t = (const loom_aaudio_target*)target;
	return t->data(stream, t->user, audio, frames);
}

__attribute__((visibility("default")))
void loom_aaudio_error(void* stream, void* target, int32_t error)
{
	const loom_aaudio_target* t = (const loom_aaudio_target*)target;
	t->error(stream, t->user, error);
}
