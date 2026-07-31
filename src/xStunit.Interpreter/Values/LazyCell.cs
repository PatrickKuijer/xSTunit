using System;

namespace xStunit.Interpreter
{
    // A Cell whose Value is computed on first read rather than at declaration
    // time. Engine.NewInstance constructs an FB-typed field by recursing into
    // NewInstance for that type, which does the same for its own fields, so
    // eager construction would build the entire reachable FB type graph - and
    // one unsupported construct (an ARRAY[*] open bound, an unresolvable
    // default-value expression) anywhere in it would sink the outer instance,
    // even in a field the test never touches. Deferring to first dereference
    // scopes that risk to fields actually read. Nesting needs no extra
    // bookkeeping: materializing this cell calls NewInstance, which wraps its
    // own FB-typed fields in further LazyCells.
    public sealed class LazyCell : Cell
    {
        private readonly Func<object> _factory;
        private object _value;
        private bool _materialized;

        public LazyCell(Func<object> factory, string declaredTypeName)
        {
            _factory = factory;
            DeclaredTypeName = declaredTypeName;
        }

        public override object Value
        {
            get
            {
                if (!_materialized)
                {
                    // Assign only after the factory returns, so a factory that
                    // throws leaves the cell re-triggerable on a later read
                    // instead of caching a half-built result.
                    var value = _factory();
                    _value = value;
                    _materialized = true;
                }

                return _value;
            }
            set
            {
                _value = value;
                _materialized = true;
            }
        }
    }
}
