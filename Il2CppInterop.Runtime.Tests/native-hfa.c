#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

typedef struct Class Class;
typedef struct { Class *type; uint32_t offset; int flags; } Field;
struct Class { int kind; int size; bool value_type; bool enum_type; bool byref; int count; Field *fields; };
#define FIELD(type, offset) { &(type), 2 * sizeof(void *) + (offset), 0 }
static Class single = {0x0c, 4, true}, real = {0x0d, 8, true}, integer = {0x08, 4, true};
static Class reference = {0x12, sizeof(void *), false};
static Class byref = {0x0c, 4, true, false, true}, enumeration = {0x11, 4, true, true};
static Field pair_f_fields[] = { FIELD(single, 0), FIELD(single, 4), { &integer, 0, 0x10 } };
static Field pair_d_fields[] = { FIELD(real, 0), FIELD(real, 8) };
static Class pair_f = {0x15, 8, true, false, false, 3, pair_f_fields};
static Class pair_d = {0x15, 16, true, false, false, 2, pair_d_fields};
static Field nested_f[] = { FIELD(pair_f, 0), FIELD(pair_f, 8) };
static Field nested_d[] = { FIELD(pair_d, 0), FIELD(pair_d, 16) };
static Field mixed[] = { FIELD(single, 0), FIELD(integer, 4) };
static Field padded[] = { FIELD(single, 0), FIELD(single, 8) };
static Field five[] = { FIELD(single, 0), FIELD(single, 4), FIELD(single, 8), FIELD(single, 12), FIELD(single, 16) };
static Field refs[] = { FIELD(reference, 0) };
static Field byrefs[] = { FIELD(byref, 0), FIELD(single, 4) };
static Field enums[] = { FIELD(enumeration, 0), FIELD(single, 4) };
static Field overlap[] = { FIELD(single, 0), FIELD(single, 0) };
static Class cases[] = {
    {0x15, 8, true, false, false, 3, pair_f_fields},
    {0x15, 16, true, false, false, 2, pair_d_fields},
    {0x15, 16, true, false, false, 2, nested_f},
    {0x15, 32, true, false, false, 2, nested_d},
    {0x15, 8, true, false, false, 2, mixed},
    {0x15, 12, true, false, false, 2, padded},
    {0x15, 20, true, false, false, 5, five},
    {0x15, 8, true, false, false, 1, refs},
    {0x15, 8, true, false, false, 2, byrefs},
    {0x15, 8, true, false, false, 2, enums},
    {0x15, 8, true, false, false, 2, overlap},
};

EXPORT void *fixture_class(int index) { return &cases[index]; }
EXPORT void *il2cpp_domain_get(void) { return cases; }
EXPORT void **il2cpp_domain_get_assemblies(void *domain, uint32_t *count) { (void)domain; *count = 0; return NULL; }
EXPORT bool il2cpp_class_is_valuetype(Class *klass) { return klass->value_type; }
EXPORT bool il2cpp_class_is_enum(Class *klass) { return klass->enum_type; }
EXPORT void *il2cpp_class_get_type(Class *klass) { return klass; }
EXPORT int il2cpp_type_get_type(Class *type) { return type->kind; }
EXPORT bool il2cpp_type_is_byref(Class *type) { return type->byref; }
EXPORT void *il2cpp_class_from_type(Class *type) { return type; }
EXPORT int il2cpp_class_value_size(Class *klass, uint32_t *align) { *align = 4; return klass->size; }
EXPORT void *il2cpp_class_get_fields(Class *klass, void **iterator) {
    uintptr_t index = (uintptr_t)*iterator;
    if (index >= (uintptr_t)klass->count) return NULL;
    *iterator = (void *)(index + 1);
    return &klass->fields[index];
}
EXPORT int il2cpp_field_get_flags(Field *field) { return field->flags; }
EXPORT uint32_t il2cpp_field_get_offset(Field *field) { return field->offset; }
EXPORT void *il2cpp_field_get_type(Field *field) { return field->type; }

typedef struct { float x, y; } Float2;
typedef struct { double x, y; } Double2;
typedef struct { Float2 x, y; } Float4;
typedef struct { Double2 x, y; } Double4;
EXPORT int fixture_callback_0(Float2 (*callback)(Float2)) {
    Float2 result = callback((Float2){1.25f, -2.5f});
    return result.x == 2.5f && result.y == -5.0f;
}
EXPORT int fixture_callback_1(Double2 (*callback)(Double2)) {
    Double2 result = callback((Double2){1.25, -2.5});
    return result.x == 2.5 && result.y == -5.0;
}
EXPORT int fixture_callback_2(Float4 (*callback)(Float4)) {
    Float4 result = callback((Float4){{1.25f, -2.5f}, {3.75f, 4.5f}});
    return result.x.x == 2.5f && result.x.y == -5.0f && result.y.x == 7.5f && result.y.y == 9.0f;
}
EXPORT int fixture_callback_3(Double4 (*callback)(Double4)) {
    Double4 result = callback((Double4){{1.25, -2.5}, {3.75, 4.5}});
    return result.x.x == 2.5 && result.x.y == -5.0 && result.y.x == 7.5 && result.y.y == 9.0;
}
