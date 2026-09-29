namespace xStunit.Interpreter
{
    // A global whose declaration can never produce a value. Any access throws,
    // so a suite touching it fails with the real reason while suites that do
    // not are unaffected.
    //
    // Each access throws a fresh exception wrapping the root fault: fault-site
    // data is stamped onto the exception object as it unwinds, so rethrowing
    // one shared instance would show every later reader the first reader's
    // location and a call stack that keeps growing.
    internal sealed class FaultedCell : Cell
    {
        private readonly FbInitArgumentException _root;

        public FaultedCell(FbInitArgumentException root, string declaredTypeName)
        {
            _root = root;
            DeclaredTypeName = declaredTypeName;
        }

        public override object Value
        {
            get => throw Fault();
            set => throw Fault();
        }

        private FbInitArgumentException Fault() => new FbInitArgumentException(_root.Message, _root);
    }
}
