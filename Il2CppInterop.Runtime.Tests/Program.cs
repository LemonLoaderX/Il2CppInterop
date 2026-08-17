using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.Injection;

VerifyHfa<Float2>(8, typeof(float), 2);
VerifyHfa<Double4>(32, typeof(double), 4);

var mixed = TrampolineHelpers.GetFixedSizeStructType(typeof(Mixed), 8);
Assert(!TrampolineHelpers.IsArm64Hfa(mixed), "Mixed aggregates must use the integer ABI class.");
Assert(mixed == typeof(ulong), "An eight-byte non-HFA should use the primitive integer carrier.");

var float5 = TrampolineHelpers.GetFixedSizeStructType(typeof(Float5), 20);
Assert(!TrampolineHelpers.IsArm64Hfa(float5), "AAPCS64 limits HFAs to four elements.");
Assert(Marshal.SizeOf(float5) == 20, "The non-HFA carrier size is incorrect.");

Console.WriteLine("Android ARM64 aggregate classification tests passed.");

static void VerifyHfa<T>(int size, Type elementType, int elementCount)
{
    var carrier = TrampolineHelpers.GetFixedSizeStructType(typeof(T), size);
    Assert(TrampolineHelpers.IsArm64Hfa(carrier), $"{typeof(T).Name} was not classified as an HFA.");
    Assert(Marshal.SizeOf(carrier) == size, $"{typeof(T).Name} carrier size is incorrect.");
    var fields = carrier.GetFields();
    Assert(fields.Length == elementCount, $"{typeof(T).Name} carrier field count is incorrect.");
    Assert(fields.All(field => field.FieldType == elementType),
        $"{typeof(T).Name} carrier element type is incorrect.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

[StructLayout(LayoutKind.Explicit, Size = 8)]
struct Float2
{
    [FieldOffset(0)] public float X;
    [FieldOffset(4)] public float Y;
}

[StructLayout(LayoutKind.Explicit, Size = 32)]
struct Double4
{
    [FieldOffset(0)] public double X;
    [FieldOffset(8)] public double Y;
    [FieldOffset(16)] public double Z;
    [FieldOffset(24)] public double W;
}

[StructLayout(LayoutKind.Explicit, Size = 8)]
struct Mixed
{
    [FieldOffset(0)] public float X;
    [FieldOffset(4)] public int Y;
}

[StructLayout(LayoutKind.Explicit, Size = 20)]
struct Float5
{
    [FieldOffset(0)] public float A;
    [FieldOffset(4)] public float B;
    [FieldOffset(8)] public float C;
    [FieldOffset(12)] public float D;
    [FieldOffset(16)] public float E;
}
