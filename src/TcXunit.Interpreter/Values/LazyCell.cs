using System;

namespace TcXunit.Interpreter
{
    // A Cell whose Value is computed on first read rather than eagerly at
    // declaration time (TcXunit-mxx). Engine.NewInstance walks an FB type's
    // declared fields to populate FbInstance.Fields; for an FB-typed field,
    // that field's own DefaultValue recurses into NewInstance for THAT
    // type, which repeats the same walk for its own fields, and so on -
    // transitively constructing the whole reachable FB type graph no matter
    // whether the code that declared the outer instance/local ever reads
    // that particular field. If any field anywhere in that graph has an
    // unsupported construct (e.g. an ARRAY[*] open-array bound) or an
    // unresolvable default-value expression, the whole outer construction
    // fails - even for a field the calling test never touches.
    //
    // Wrapping an FB-typed field's construction in a LazyCell defers that
    // inner NewInstance call (and any fault inside it) until the field is
    // actually dereferenced, scoping the cost/risk to code paths that
    // actually read the field instead of the whole declared type graph.
    // Nested FB-typed fields several levels deep compose the same way
    // automatically: materializing this cell calls NewInstance again, which
    // wraps ITS OWN FB-typed fields in further LazyCells, so laziness
    // extends arbitrarily deep without any extra bookkeeping here.
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
                    // Assign to the backing fields only after the factory
                    // returns successfully, so a factory that throws (e.g.
                    // an unsupported nested construct several types away)
                    // leaves this cell re-triggerable on a later read
                    // rather than caching a half-built result.
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
