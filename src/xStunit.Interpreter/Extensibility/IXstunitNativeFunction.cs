namespace xStunit.Interpreter.Extensibility
{
    // Supplies the behavior of one function the interpreter can never have
    // source for. Every library shipped with TwinCAT (Tc2_Utilities,
    // Tc2_Standard, ...) is compiled-only: there is no .TcPOU to parse, so a
    // suite calling F_CheckSum16/F_ToUPPER/etc. cannot resolve it the way it
    // resolves a FUNCTION POU in the user's own tree. An implementation is
    // supplied from outside the interpreter, either in-process or from a
    // plugin assembly loaded at run time (the CLI's --plugins).
    //
    // Deliberately the smallest possible surface: a name and an Invoke. The
    // interpreter never hands out its Engine, TypeRegistry, or Frame, so a
    // plugin can compute a value from its arguments and nothing else - it
    // cannot reach into the running program's state, define types, or alter
    // dispatch.
    //
    // Engine consults registered functions LAST (Engine.Invocation.cs), after
    // intrinsics, methods on the ancestry chain, bare-invoked FB fields,
    // native hosts, and real global FUNCTION POUs. A plugin can therefore
    // never shadow interpreted source that actually exists: it only fills a
    // hole that would otherwise have been a "not found" error.
    public interface IXstunitNativeFunction
    {
        // The ST identifier this implements, as written in PLC source (e.g.
        // "F_CheckSum16"). Matched case-insensitively, since IEC 61131-3
        // identifiers are case-insensitive. Registering the same name twice is
        // an error rather than last-one-wins (see NativeFunctionRegistry).
        string Name { get; }

        // Computes the function's return value from its call arguments.
        //
        // The returned object must use the same CLR representation the
        // interpreter uses for the corresponding IEC type, or a later
        // assignment/assertion misbehaves: BOOL -> bool, SINT/USINT/BYTE/INT/
        // UINT/WORD/DINT -> int, UDINT/DWORD/LINT -> long, ULINT/LWORD ->
        // ulong, REAL -> float, LREAL -> double, STRING -> string. Return null
        // for a function with no return value. NativeCallContext's typed
        // accessors produce and consume these same shapes, so a plugin that
        // reads its arguments through them stays consistent by construction.
        //
        // Throwing is a legitimate outcome (bad argument count, unsupported
        // argument shape): the exception surfaces through the normal
        // interpreter fault path, attributed to the PLC call site, exactly
        // like any other run-time fault.
        object Invoke(NativeCallContext context);
    }
}
