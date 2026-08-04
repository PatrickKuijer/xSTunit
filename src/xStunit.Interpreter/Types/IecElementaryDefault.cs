using System;

namespace xStunit.Interpreter
{
    // Single source of truth for the initial value of an IEC 61131-3
    // *elementary non-numeric* type - a declared VAR of one of these starts at
    // the value returned here, not at CLR null. Everything outside that set is
    // deliberately elsewhere: numerics belong to IecNumericType; STRUCT/ARRAY/
    // FB defaults stay in Engine, needing materialization rather than a
    // constant; POINTER TO/REFERENCE TO belong to AddressTypeInfo, being a
    // prefix rule rather than a type-name entry.
    internal static class IecElementaryDefault
    {
        // IEC 61131-3 type names are case-insensitive (a VAR declared 'bool'
        // or 'Time' is as valid as 'BOOL'/'TIME'), hence OrdinalIgnoreCase
        // throughout.
        public static bool TryGetDefault(string typeName, out object value)
        {
            if (typeName != null)
            {
                // A keyword compare would not do: the sized STRING(n) form's
                // size may itself be a constant expression.
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

                // The whole DATE family boxes as uint, like TIME above, though
                // the three disagree on unit and origin (see DateTimeLiteral).
                // DT and TOD are IEC's own abbreviations of the last two, not
                // types of their own.
                if (typeName.Equals("DATE", StringComparison.OrdinalIgnoreCase)
                    || typeName.Equals("DATE_AND_TIME", StringComparison.OrdinalIgnoreCase)
                    || typeName.Equals("DT", StringComparison.OrdinalIgnoreCase)
                    || typeName.Equals("TIME_OF_DAY", StringComparison.OrdinalIgnoreCase)
                    || typeName.Equals("TOD", StringComparison.OrdinalIgnoreCase))
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
