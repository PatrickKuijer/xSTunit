using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // Native-stub boundary for Loopback (TcXunit-w5x.15.5 / T4 design, fault
    // vocabulary per TcXunit-w5x.15.8 / T5 design): one Loopback instance = one
    // fixed link. Transmit is a discrete copy (sink.Value = source.Value,
    // per-field cloned for STRUCT/ARRAY payloads via CellCloner - TcXunit-w5x.15.10),
    // called explicitly by the test author - no implicit wiring, no StepCycles
    // hook. One active fault mode at a time - each fault-setting call clears
    // any other pending fault state. LinkUp and LastUpdateTime are published
    // into the owning instance's own Cell fields (set up in Engine.NewInstance),
    // the same way TimerHost publishes Q/ET.
    public sealed class LoopbackHost
    {
        private bool _dropped;
        private bool _frozen;
        private int _delayDepth;
        private readonly Queue<object> _delayQueue = new Queue<object>();
        private bool _duplicatePending;
        private bool _corruptPending;
        private object _corruptValue;
        private object _lastTransmittedValue;

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
