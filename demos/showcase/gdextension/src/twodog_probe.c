// SPDX-License-Identifier: MIT
// 2dog: this file is part of https://2dog.dev

// A GDExtension in plain C (no godot-cpp) registering TwoDogProbe (RefCounted) with add() and describe(), so each host
// can prove the extension was loaded, registered and is callable, and TwoDogTicker (Node), which emits ticked(count)
// once per second from its _process override for the showcase's signal table. Deliberately uses libc (calloc,
// snprintf): on the web the side module imports those from the host's main module.

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

#include "gdextension_interface.h"

#if defined(_WIN32)
#define PROBE_EXPORT __declspec(dllexport)
#else
#define PROBE_EXPORT __attribute__((visibility("default")))
#endif

#if defined(__EMSCRIPTEN__)
#define PROBE_PLATFORM "web"
#elif defined(_WIN32)
#define PROBE_PLATFORM "windows"
#elif defined(__APPLE__)
#define PROBE_PLATFORM "macos"
#else
#define PROBE_PLATFORM "linux"
#endif

// StringName and String are opaque, one pointer wide.
typedef struct {
	void *opaque;
} StringName;

typedef struct {
	void *opaque;
} String;

// Variant is 24 bytes in single-precision builds, on 32- and 64-bit platforms alike.
typedef struct {
	uint64_t opaque[3];
} Variant;

typedef struct {
	GDExtensionObjectPtr object;
	int64_t calls;
} Probe;

#define TICKER_INTERVAL 1.0 // seconds

typedef struct {
	GDExtensionObjectPtr object;
	int64_t count;
	double elapsed;
} Ticker;

static struct {
	GDExtensionClassLibraryPtr library;
	GDExtensionInterfaceClassdbConstructObject3 classdb_construct_object3;
	GDExtensionInterfaceClassdbRegisterExtensionClass6 classdb_register_extension_class6;
	GDExtensionInterfaceClassdbRegisterExtensionClassMethod classdb_register_extension_class_method;
	GDExtensionInterfaceClassdbRegisterExtensionClassSignal classdb_register_extension_class_signal;
	GDExtensionInterfaceClassdbUnregisterExtensionClass classdb_unregister_extension_class;
	GDExtensionInterfaceObjectSetInstance object_set_instance;
	GDExtensionInterfaceStringNameNewWithLatin1Chars string_name_new_with_latin1_chars;
	GDExtensionInterfaceStringNewWithUtf8Chars string_new_with_utf8_chars;
	GDExtensionInterfaceVariantGetType variant_get_type;
	GDExtensionInterfaceVariantCall variant_call;
	GDExtensionInterfaceVariantDestroy variant_destroy;
	GDExtensionPtrDestructor string_name_destroy;
	GDExtensionPtrDestructor string_destroy;
	GDExtensionVariantFromTypeConstructorFunc variant_from_int;
	GDExtensionVariantFromTypeConstructorFunc variant_from_string;
	GDExtensionVariantFromTypeConstructorFunc variant_from_string_name;
	GDExtensionVariantFromTypeConstructorFunc variant_from_object;
	GDExtensionTypeFromVariantConstructorFunc int_from_variant;
} api;

static StringName probe_class_name;
static StringName ticker_class_name;
static StringName process_name;
static StringName emit_signal_name;
static StringName ticked_name;

static GDExtensionObjectPtr construct_parent(const char *p_parent) {
	StringName parent;
	api.string_name_new_with_latin1_chars(&parent, p_parent, 0);
	GDExtensionObjectPtr object = api.classdb_construct_object3(&parent);
	api.string_name_destroy(&parent);
	return object;
}

static GDExtensionObjectPtr probe_create(void *p_class_userdata, GDExtensionBool p_notify_postinitialize) {
	(void)p_class_userdata;
	(void)p_notify_postinitialize;

	Probe *self = (Probe *)calloc(1, sizeof(Probe));
	self->object = construct_parent("RefCounted");
	api.object_set_instance(self->object, &probe_class_name, self);
	return self->object;
}

static void instance_free(void *p_class_userdata, GDExtensionClassInstancePtr p_instance) {
	(void)p_class_userdata;
	free(p_instance);
}

static int64_t probe_add(Probe *self, int64_t a, int64_t b) {
	self->calls++;
	return a + b;
}

static void probe_describe(Probe *self, String *r_string) {
	char text[96];
	self->calls++;
	snprintf(text, sizeof(text), "twodog_probe (C) on %s, %d-bit, call #%lld", PROBE_PLATFORM,
			(int)(sizeof(void *) * 8), (long long)self->calls);
	api.string_new_with_utf8_chars(r_string, text);
}

static int check_args(const GDExtensionConstVariantPtr *p_args, GDExtensionInt p_count, GDExtensionInt p_expected, GDExtensionCallError *r_error) {
	if (p_count != p_expected) {
		r_error->error = p_count < p_expected ? GDEXTENSION_CALL_ERROR_TOO_FEW_ARGUMENTS : GDEXTENSION_CALL_ERROR_TOO_MANY_ARGUMENTS;
		r_error->expected = (int32_t)p_expected;
		return 0;
	}
	for (GDExtensionInt i = 0; i < p_count; i++) {
		if (api.variant_get_type(p_args[i]) != GDEXTENSION_VARIANT_TYPE_INT) {
			r_error->error = GDEXTENSION_CALL_ERROR_INVALID_ARGUMENT;
			r_error->argument = (int32_t)i;
			r_error->expected = GDEXTENSION_VARIANT_TYPE_INT;
			return 0;
		}
	}
	return 1;
}

static void add_call(void *p_method_userdata, GDExtensionClassInstancePtr p_instance, const GDExtensionConstVariantPtr *p_args, GDExtensionInt p_argument_count, GDExtensionVariantPtr r_return, GDExtensionCallError *r_error) {
	(void)p_method_userdata;
	if (!check_args(p_args, p_argument_count, 2, r_error)) {
		return;
	}
	int64_t a;
	int64_t b;
	api.int_from_variant(&a, (GDExtensionVariantPtr)p_args[0]);
	api.int_from_variant(&b, (GDExtensionVariantPtr)p_args[1]);
	int64_t result = probe_add((Probe *)p_instance, a, b);
	api.variant_from_int(r_return, &result);
}

static void add_ptrcall(void *p_method_userdata, GDExtensionClassInstancePtr p_instance, const GDExtensionConstTypePtr *p_args, GDExtensionTypePtr r_ret) {
	(void)p_method_userdata;
	*(int64_t *)r_ret = probe_add((Probe *)p_instance, *(const int64_t *)p_args[0], *(const int64_t *)p_args[1]);
}

static void describe_call(void *p_method_userdata, GDExtensionClassInstancePtr p_instance, const GDExtensionConstVariantPtr *p_args, GDExtensionInt p_argument_count, GDExtensionVariantPtr r_return, GDExtensionCallError *r_error) {
	(void)p_method_userdata;
	if (!check_args(p_args, p_argument_count, 0, r_error)) {
		return;
	}
	String text;
	probe_describe((Probe *)p_instance, &text);
	api.variant_from_string(r_return, &text);
	api.string_destroy(&text);
}

static void describe_ptrcall(void *p_method_userdata, GDExtensionClassInstancePtr p_instance, const GDExtensionConstTypePtr *p_args, GDExtensionTypePtr r_ret) {
	(void)p_method_userdata;
	(void)p_args;
	// The caller passes an initialized String.
	api.string_destroy(r_ret);
	probe_describe((Probe *)p_instance, (String *)r_ret);
}

static GDExtensionObjectPtr ticker_create(void *p_class_userdata, GDExtensionBool p_notify_postinitialize) {
	(void)p_class_userdata;
	(void)p_notify_postinitialize;

	Ticker *self = (Ticker *)calloc(1, sizeof(Ticker));
	self->object = construct_parent("Node");
	api.object_set_instance(self->object, &ticker_class_name, self);
	return self->object;
}

// Emits ticked(count) through Object::emit_signal, as a script would.
static int64_t ticker_tick(Ticker *self) {
	self->count++;

	Variant object;
	Variant signal;
	Variant count;
	Variant result;
	api.variant_from_object(&object, &self->object);
	api.variant_from_string_name(&signal, &ticked_name);
	api.variant_from_int(&count, &self->count);

	const GDExtensionConstVariantPtr args[2] = { &signal, &count };
	GDExtensionCallError error;
	api.variant_call(&object, &emit_signal_name, args, 2, &result, &error);

	api.variant_destroy(&result);
	api.variant_destroy(&count);
	api.variant_destroy(&signal);
	api.variant_destroy(&object);
	return self->count;
}

static void ticker_process(GDExtensionClassInstancePtr p_instance, const GDExtensionConstTypePtr *p_args, GDExtensionTypePtr r_ret) {
	(void)r_ret;
	Ticker *self = (Ticker *)p_instance;
	self->elapsed += *(const double *)p_args[0];
	if (self->elapsed >= TICKER_INTERVAL) {
		self->elapsed -= TICKER_INTERVAL;
		ticker_tick(self);
	}
}

// Overriding _process also makes Node enable processing on ready.
static GDExtensionClassCallVirtual ticker_get_virtual(void *p_class_userdata, GDExtensionConstStringNamePtr p_name, uint32_t p_hash) {
	(void)p_class_userdata;
	(void)p_hash;
	// StringNames are interned: equal names share one data pointer, which is all StringName::operator== compares.
	return ((const StringName *)p_name)->opaque == process_name.opaque ? ticker_process : NULL;
}

static void tick_call(void *p_method_userdata, GDExtensionClassInstancePtr p_instance, const GDExtensionConstVariantPtr *p_args, GDExtensionInt p_argument_count, GDExtensionVariantPtr r_return, GDExtensionCallError *r_error) {
	(void)p_method_userdata;
	if (!check_args(p_args, p_argument_count, 0, r_error)) {
		return;
	}
	int64_t count = ticker_tick((Ticker *)p_instance);
	api.variant_from_int(r_return, &count);
}

static void tick_ptrcall(void *p_method_userdata, GDExtensionClassInstancePtr p_instance, const GDExtensionConstTypePtr *p_args, GDExtensionTypePtr r_ret) {
	(void)p_method_userdata;
	(void)p_args;
	*(int64_t *)r_ret = ticker_tick((Ticker *)p_instance);
}

static void register_class(const StringName *p_class, const char *p_parent, GDExtensionClassCreateInstance3 p_create, GDExtensionClassGetVirtual2 p_get_virtual) {
	StringName parent;
	api.string_name_new_with_latin1_chars(&parent, p_parent, 0);

	GDExtensionClassCreationInfo6 info = { 0 };
	info.is_exposed = 1;
	info.create_instance_func = p_create;
	info.free_instance_func = instance_free;
	info.get_virtual_func = p_get_virtual;
	api.classdb_register_extension_class6(api.library, p_class, &parent, &info);
	api.string_name_destroy(&parent);
}

static void register_int_signal(const StringName *p_class, const StringName *p_signal, const char *p_argument_name) {
	StringName argument_name;
	StringName empty_name;
	String empty_hint;
	api.string_name_new_with_latin1_chars(&argument_name, p_argument_name, 0);
	api.string_name_new_with_latin1_chars(&empty_name, "", 0);
	api.string_new_with_utf8_chars(&empty_hint, "");

	const uint32_t usage_default = 6; // PROPERTY_USAGE_STORAGE | PROPERTY_USAGE_EDITOR
	GDExtensionPropertyInfo argument = { GDEXTENSION_VARIANT_TYPE_INT, &argument_name, &empty_name, 0, &empty_hint, usage_default };
	api.classdb_register_extension_class_signal(api.library, p_class, p_signal, &argument, 1);

	api.string_destroy(&empty_hint);
	api.string_name_destroy(&empty_name);
	api.string_name_destroy(&argument_name);
}

static void register_method(const StringName *p_class, const char *p_name, GDExtensionClassMethodCall p_call, GDExtensionClassMethodPtrCall p_ptrcall, GDExtensionVariantType p_return_type, uint32_t p_argument_count, const char *const *p_argument_names) {
	StringName method_name;
	StringName empty_name;
	StringName argument_names[2];
	String empty_hint;
	api.string_name_new_with_latin1_chars(&method_name, p_name, 0);
	api.string_name_new_with_latin1_chars(&empty_name, "", 0);
	api.string_new_with_utf8_chars(&empty_hint, "");

	const uint32_t usage_default = 6; // PROPERTY_USAGE_STORAGE | PROPERTY_USAGE_EDITOR
	GDExtensionPropertyInfo return_info = { p_return_type, &empty_name, &empty_name, 0, &empty_hint, usage_default };
	GDExtensionPropertyInfo arguments_info[2];
	GDExtensionClassMethodArgumentMetadata arguments_metadata[2];
	for (uint32_t i = 0; i < p_argument_count; i++) {
		api.string_name_new_with_latin1_chars(&argument_names[i], p_argument_names[i], 0);
		GDExtensionPropertyInfo info = { GDEXTENSION_VARIANT_TYPE_INT, &argument_names[i], &empty_name, 0, &empty_hint, usage_default };
		arguments_info[i] = info;
		arguments_metadata[i] = GDEXTENSION_METHOD_ARGUMENT_METADATA_INT_IS_INT64;
	}

	GDExtensionClassMethodInfo method;
	method.name = &method_name;
	method.method_userdata = NULL;
	method.call_func = p_call;
	method.ptrcall_func = p_ptrcall;
	method.method_flags = GDEXTENSION_METHOD_FLAGS_DEFAULT;
	method.has_return_value = 1;
	method.return_value_info = &return_info;
	method.return_value_metadata = p_return_type == GDEXTENSION_VARIANT_TYPE_INT ? GDEXTENSION_METHOD_ARGUMENT_METADATA_INT_IS_INT64 : GDEXTENSION_METHOD_ARGUMENT_METADATA_NONE;
	method.argument_count = p_argument_count;
	method.arguments_info = arguments_info;
	method.arguments_metadata = arguments_metadata;
	method.default_argument_count = 0;
	method.default_arguments = NULL;
	api.classdb_register_extension_class_method(api.library, p_class, &method);

	for (uint32_t i = 0; i < p_argument_count; i++) {
		api.string_name_destroy(&argument_names[i]);
	}
	api.string_destroy(&empty_hint);
	api.string_name_destroy(&empty_name);
	api.string_name_destroy(&method_name);
}

static void initialize(void *p_userdata, GDExtensionInitializationLevel p_level) {
	(void)p_userdata;
	if (p_level != GDEXTENSION_INITIALIZATION_SCENE) {
		return;
	}

	api.string_name_new_with_latin1_chars(&probe_class_name, "TwoDogProbe", 0);
	register_class(&probe_class_name, "RefCounted", probe_create, NULL);
	static const char *const add_arguments[] = { "a", "b" };
	register_method(&probe_class_name, "add", add_call, add_ptrcall, GDEXTENSION_VARIANT_TYPE_INT, 2, add_arguments);
	register_method(&probe_class_name, "describe", describe_call, describe_ptrcall, GDEXTENSION_VARIANT_TYPE_STRING, 0, NULL);

	api.string_name_new_with_latin1_chars(&ticker_class_name, "TwoDogTicker", 0);
	api.string_name_new_with_latin1_chars(&process_name, "_process", 0);
	api.string_name_new_with_latin1_chars(&emit_signal_name, "emit_signal", 0);
	api.string_name_new_with_latin1_chars(&ticked_name, "ticked", 0);
	register_class(&ticker_class_name, "Node", ticker_create, ticker_get_virtual);
	register_method(&ticker_class_name, "tick", tick_call, tick_ptrcall, GDEXTENSION_VARIANT_TYPE_INT, 0, NULL);
	register_int_signal(&ticker_class_name, &ticked_name, "count");
}

static void deinitialize(void *p_userdata, GDExtensionInitializationLevel p_level) {
	(void)p_userdata;
	if (p_level != GDEXTENSION_INITIALIZATION_SCENE) {
		return;
	}
	api.classdb_unregister_extension_class(api.library, &ticker_class_name);
	api.classdb_unregister_extension_class(api.library, &probe_class_name);
	api.string_name_destroy(&ticked_name);
	api.string_name_destroy(&emit_signal_name);
	api.string_name_destroy(&process_name);
	api.string_name_destroy(&ticker_class_name);
	api.string_name_destroy(&probe_class_name);
}

// Interface functions arrive as void (*)(); casting through void (*)(void) states the real type explicitly (compilers
// exempt that cast from -Wcast-function-type).
#define LOAD(type, name) ((type)(void (*)(void))p_get_proc_address(name))

PROBE_EXPORT GDExtensionBool twodog_probe_init(GDExtensionInterfaceGetProcAddress p_get_proc_address, GDExtensionClassLibraryPtr p_library, GDExtensionInitialization *r_initialization) {
	api.library = p_library;
	api.classdb_construct_object3 = LOAD(GDExtensionInterfaceClassdbConstructObject3, "classdb_construct_object3");
	api.classdb_register_extension_class6 = LOAD(GDExtensionInterfaceClassdbRegisterExtensionClass6, "classdb_register_extension_class6");
	api.classdb_register_extension_class_method = LOAD(GDExtensionInterfaceClassdbRegisterExtensionClassMethod, "classdb_register_extension_class_method");
	api.classdb_register_extension_class_signal = LOAD(GDExtensionInterfaceClassdbRegisterExtensionClassSignal, "classdb_register_extension_class_signal");
	api.classdb_unregister_extension_class = LOAD(GDExtensionInterfaceClassdbUnregisterExtensionClass, "classdb_unregister_extension_class");
	api.object_set_instance = LOAD(GDExtensionInterfaceObjectSetInstance, "object_set_instance");
	api.string_name_new_with_latin1_chars = LOAD(GDExtensionInterfaceStringNameNewWithLatin1Chars, "string_name_new_with_latin1_chars");
	api.string_new_with_utf8_chars = LOAD(GDExtensionInterfaceStringNewWithUtf8Chars, "string_new_with_utf8_chars");
	api.variant_get_type = LOAD(GDExtensionInterfaceVariantGetType, "variant_get_type");
	api.variant_call = LOAD(GDExtensionInterfaceVariantCall, "variant_call");
	api.variant_destroy = LOAD(GDExtensionInterfaceVariantDestroy, "variant_destroy");

	GDExtensionInterfaceVariantGetPtrDestructor get_destructor = LOAD(GDExtensionInterfaceVariantGetPtrDestructor, "variant_get_ptr_destructor");
	GDExtensionInterfaceGetVariantFromTypeConstructor from_type = LOAD(GDExtensionInterfaceGetVariantFromTypeConstructor, "get_variant_from_type_constructor");
	GDExtensionInterfaceGetVariantToTypeConstructor to_type = LOAD(GDExtensionInterfaceGetVariantToTypeConstructor, "get_variant_to_type_constructor");
	if (!api.classdb_construct_object3 || !api.classdb_register_extension_class6 || !get_destructor || !from_type || !to_type) {
		return 0; // Needs the Godot 4.7 interface.
	}
	api.string_name_destroy = get_destructor(GDEXTENSION_VARIANT_TYPE_STRING_NAME);
	api.string_destroy = get_destructor(GDEXTENSION_VARIANT_TYPE_STRING);
	api.variant_from_int = from_type(GDEXTENSION_VARIANT_TYPE_INT);
	api.variant_from_string = from_type(GDEXTENSION_VARIANT_TYPE_STRING);
	api.variant_from_string_name = from_type(GDEXTENSION_VARIANT_TYPE_STRING_NAME);
	api.variant_from_object = from_type(GDEXTENSION_VARIANT_TYPE_OBJECT);
	api.int_from_variant = to_type(GDEXTENSION_VARIANT_TYPE_INT);

	r_initialization->minimum_initialization_level = GDEXTENSION_INITIALIZATION_SCENE;
	r_initialization->userdata = NULL;
	r_initialization->initialize = initialize;
	r_initialization->deinitialize = deinitialize;
	return 1;
}
