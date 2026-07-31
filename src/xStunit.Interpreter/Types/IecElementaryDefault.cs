using System;

namespace xStunit.Interpreter
{
    // Single source of truth for the IEC 61131-3 *elementary non-numeric*
    // type name -> default value fact (TcXunit-mvbk). Engine.DefaultValue's
    // BOOL/TIME/LTIME/DATE-family/STRING chain and SeedReturnCell's
    // IsSeedableElementaryNonNumericType each encoded this same set, the
    // latter with a comment conceding it had to be "kept in sync with the
    // branches Engine.DefaultValue actually returns a plain constant for".
    //
    // Scope is deliberately narrow, mirroring IecNumericType's placement and
    // shape (static, pure typeName->value, no Engine state):
    //   - numeric defaults stay with IecNumericType, already single-owned;
    //   - STRUCT/ARRAY/FB defaults stay in Engine, since they need
    //     materialization (FbInstance, element construction), not a constant;
    //   - POINTER TO/REFERENCE TO stay in Engine, being a prefix rule rather
    //     than a type-name entry.
    internal static class IecElementaryDefault
    {
        // TcXunit-fzm: IEC 61131-3 type names are case-insensitive (a VAR
        // declared 'bool' or 'Time' is exactly as valid as 'BOOL'/'TIME'), so
        // every compare here is OrdinalIgnoreCase - the same decision as
        // IecNumericType, StringTypeInfo, ArrayTypeInfo, and TypeRegistry.
        public static bool TryGetDefault(string typeName, out object value)
        {
            if (typeName != null)
            {
                // STRING/WSTRING plus their sized STRING(n)/WSTRING(n) forms,
                // whose size text may itself be a constant expression - hence
                // StringTypeInfo rather than a keyword compare here.
                if (StringTypeInfo.IsStringType(typeName))
                {
                    value = "";
                    return true;
                }

                if (typeName.Equals("BOOL", StringComparison.OrdinalIgnoreCase))
                {
                    value = false;
                    return true;
                }

                if (typeName.Equals("TIME", StringComparison.OrdinalIgnoreCase))
                {
                    value = 0u;
                    return true;
                }

                if (typeName.Equals("LTIME", StringComparison.OrdinalIgnoreCase))
                {
                    value = 0ul;
                    return true;
                }

                // DATE/DATE_AND_TIME/TIME_OF_DAY (TcXunit-gd2.13) all box as
                // uint (see DateTimeLiteral.cs), same as TIME above.
                if (typeName.Equals("DATE", StringComparison.OrdinalIgnoreCase)
                    || typeName.Equals("DATE_AND_TIME", StringComparison.OrdinalIgnoreCase)
                    || typeName.Equals("TIME_OF_DAY", StringComparison.OrdinalIgnoreCase))
                {
                    value = 0u;
                    return true;
                }
            }

            value = null;
            return false;
        }
    }
}
