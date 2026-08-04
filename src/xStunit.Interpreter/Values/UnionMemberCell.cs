namespace xStunit.Interpreter
{
    // One member of a UNION, as a typed view onto the instance's single backing
    // buffer rather than storage of its own - the ArrayElementCell of overlaid
    // memory. Reading reinterprets the overlaid bytes through this member's
    // declared type; writing puts this member's value back into them, which is
    // what makes a write through one member visible through every other.
    internal sealed class UnionMemberCell : Cell
    {
        private readonly UnionStorage _storage;
        private readonly string _typeName;
        private object _view;

        public UnionMemberCell(UnionStorage storage, string typeName, int stringCapacity)
        {
            _storage = storage;
            _typeName = typeName;
            DeclaredTypeName = typeName;
            StringCapacity = stringCapacity;
        }

        public override object Value
        {
            get
            {
                _storage.Materialize(this);
                return _view;
            }
            set
            {
                _storage.Claim(this);
                _view = ClampToCapacity(value, StringCapacity);
            }
        }

        public void PackInto(TypeLayout layout, byte[] bytes) => layout.Pack(bytes, 0, _view, _typeName);

        public void UnpackFrom(TypeLayout layout, byte[] bytes) => _view = layout.Unpack(bytes, 0, _typeName);
    }
}
