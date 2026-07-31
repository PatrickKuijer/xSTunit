namespace xStunit.Interpreter.Extensibility
{
    // One TwinCAT library function TcXunit can't get source for (TcXunit-6k2).
    //
    // Every library shipped with TwinCAT (Tc2_Utilities, Tc2_Standard, ...) is
    // compiled-only: there is no .TcPOU to parse, so a suite that calls
    // F_CheckSum16/F_ToUPPER/etc. can never resolve it the way it resolves a
    // FUNCTION POU in the user's own tree. Rather than growing
    // Engine.Expressions.cs's intrinsic if-chain once per vendor function,
    // an implementation of this interface supplies the behavior from outside
    // the interpreter - in-process (tests, a host that news one up) or loaded
    // from a plugin assembly at run time (see the CLI's --plugins).
    //
    // Deliberately the smallest possible surface: a name and an Invoke. The
    // interpreter never hands out its Engine, TypeRegistry, or Frame, so a
    // plugin can compute a value from its arguments and nothing else. It
    // cannot reach into the running program's state, define types, or alter
    // dispatch.
    //
    // Engine consults registered functions LAST (Engine.Invocation.cs) - after
    // intrinsics, methods on the ancestry chain, bare-invoked FB fields, native
    // hosts, and real global FUNCTION POUs. A plugin therefore can never shadow
    // interpreted source that actually exists: it only fills a hole that would
    // otherwise have been a "not found" error.
    public interface IXstunitNativeFunction
    {
        // The ST identifier this implements, as written in PLC source (e.g.
        // "F_CheckSum16"). Matched case-insensitively, since IEC 61131-3
        // identifiers are case-insensitive and real code is inconsistent about
        // it. Two registrations of the same name are a registration-time error
        // rather than a silent last-one-wins (see NativeFunctionRegistry).
        string Name { get; }

        // Computes the function's return value from its call arguments.
        //
        // The returned object must use the same CLR representation the
        // interpreter uses for the corresponding IEC type, or a later
        // assignment/assertion will misbehave: BOOL -> bool, SINT/USINT/BYTE/
        // INT/UINT/WORD/DINT -> int, UDINT/DWORD/LINT -> long, ULINT/LWORD ->
        // ulong, REAL -> float, LREAL -> double, STRING -> string. Return null
        // for a function with no return value. NativeCallContext's typed
        // accessors produce and consume these same shapes, so a plugin that
        // reads its arguments through them and returns one of their results
        // stays consistent by construction.
        //
        // Throwing is a legitimate outcome (bad argument count, unsupported
        // argument shape): the exception surfaces through the normal
        // interpreter fault path, attributed to the PLC call site that made
        // the call, exactly like any other run-time fault.
        object Invoke(NativeCallContext context);
    }
}
