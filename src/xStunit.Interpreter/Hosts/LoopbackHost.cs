using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // A simulated communication link: one Loopback instance is one fixed
    // link, and Transmit copies a source Cell to a sink Cell (deep-cloned via
    // CellCloner, so a STRUCT/ARRAY payload delivered earlier is not aliased
    // by a later mutation of the source).
    //
    // Unlike the timers and edge triggers, this is NOT cycle-driven: nothing
    // steps it, and Transmit only happens when the test author writes it. The
    // fault modes below therefore count transmissions, never cycles or time.
    //
    // At most one fault mode is active at a time - every fault-setting call
    // clears whatever was pending. LinkUp and LastUpdateTime are published
    // into the owning instance's Cell fields (seeded in Engine.NewInstance),
    // the same way TimerHost publishes Q/ET.
    public sealed class LoopbackHost
    {
        private bool _dropped;
        private bool _frozen;
        private int _delayDepth;
        private Queue<object> _delayQueue = new Queue<object>();
        private bool _duplicatePending;
        private bool _corruptPending;
        private object _corruptValue;
        private object _lastTransmittedValue;

        internal LoopbackHost CloneState()
        {
            var copy = (LoopbackHost)MemberwiseClone();
            copy._delayQueue = new Queue<object>(_delayQueue);
            return copy;
        }

        public void Transmit(FbInstance instance, Cell source, Cell sink, long clockTotalMs)
        {
            if (_dropped || _frozen)
                return;

            if (_delayDepth > 0)
            {
                _delayQueue.Enqueue(source.Value);
                if (_delayQueue.Count < _delayDepth)
                    return;

                var delivered = _delayQueue.Dequeue();
                _delayQueue.Clear();
                _delayDepth = 0;
                Deliver(instance, sink, delivered, clockTotalMs);
                return;
            }

            if (_duplicatePending)
            {
                _duplicatePending = false;
                Deliver(instance, sink, _lastTransmittedValue, clockTotalMs);
                return;
            }

            if (_corruptPending)
            {
                _corruptPending = false;
                var value = _corruptValue;
                _corruptValue = null;
                Deliver(instance, sink, value, clockTotalMs);
                return;
            }

            Deliver(instance, sink, source.Value, clockTotalMs);
        }

        public void Drop(FbInstance instance)
        {
            ClearFaultState();
            _dropped = true;
            instance.Fields["LinkUp"].Value = false;
        }

        public void Restore(FbInstance instance)
        {
            ClearFaultState();
            instance.Fields["LinkUp"].Value = true;
        }

        public void Freeze(FbInstance instance)
        {
            ClearFaultState();
            _frozen = true;
            instance.Fields["LinkUp"].Value = true;
        }

        public void SetDelay(FbInstance instance, int n)
        {
            ClearFaultState();
            _delayDepth = n;
            instance.Fields["LinkUp"].Value = true;
        }

        public void Duplicate(FbInstance instance)
        {
            ClearFaultState();
            _duplicatePending = true;
            instance.Fields["LinkUp"].Value = true;
        }

        public void Corrupt(FbInstance instance, object value)
        {
            ClearFaultState();
            _corruptPending = true;
            _corruptValue = value;
            instance.Fields["LinkUp"].Value = true;
        }

        private void Deliver(FbInstance instance, Cell sink, object value, long clockTotalMs)
        {
            var delivered = CellCloner.CloneValue(value);
            sink.Value = delivered;
            _lastTransmittedValue = delivered;
            instance.Fields["LastUpdateTime"].Value = clockTotalMs;
        }

        private void ClearFaultState()
        {
            _dropped = false;
            _frozen = false;
            _delayDepth = 0;
            _delayQueue.Clear();
            _duplicatePending = false;
            _corruptPending = false;
            _corruptValue = null;
        }
    }
}
