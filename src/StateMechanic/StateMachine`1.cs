﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace StateMechanic
{
    /// <summary>
    /// A state machine
    /// </summary>
    public class StateMachine<TState> : ChildStateMachine<TState>, IEventDelegate, ITransitionDelegate<TState>
        where TState : StateBase<TState>, new()
    {
        private readonly Queue<TransitionQueueItem> transitionQueue = new Queue<TransitionQueueItem>();
        private bool executingTransition;

        internal override StateMachine<TState> TopmostStateMachineInternal => this;

        /// <summary>
        /// Gets the fault associated with this state machine. A state machine will fault if one of its handlers throws an exception
        /// </summary>
        public StateMachineFaultInfo Fault { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this state machine is faulted. A state machine will fault if one of its handlers throws an exception
        /// </summary>
        public bool IsFaulted => this.Fault != null;

        /// <summary>
        /// Gets or sets the synchronizer used by this state machine to achieve thread safety. State machines are not thread safe by default
        /// </summary>
        public IStateMachineSynchronizer Synchronizer { get; set; }

        private IStateMachineSerializer<TState> serializer = new StateMachineSerializer<TState>();

        /// <summary>
        /// Gets or sets the serializer used by this state machine to serialize or deserialize itself, (see <see cref="Serialize()"/> and <see cref="Deserialize(string)"/>).
        /// </summary>
        public IStateMachineSerializer<TState> Serializer
        {
            get { return this.serializer; }
            set
            {
                if (value == null)
                    throw new ArgumentNullException();
                this.serializer = value;
            }
        }

        /// <summary>
        /// Event raised when a fault occurs in this state machine. A state machine will fault if one of its handlers throws an exception
        /// </summary>
        public event EventHandler<StateMachineFaultedEventArgs> Faulted;

        /// <summary>
        /// Event raised when a transition occurs in this state machine, or any of its child state machines
        /// </summary>
        public event EventHandler<TransitionEventArgs<TState>> Transition;

        /// <summary>
        /// Event raised whenever an event is fired but no corresponding transition is found on this state machine or any of its child state machines
        /// </summary>
        public event EventHandler<TransitionNotFoundEventArgs<TState>> TransitionNotFound;

        /// <summary>
        /// Instantiates a new instance of the <see cref="StateMachine{TState}"/> class, with the given name
        /// </summary>
        /// <param name="name">Name of this state machine</param>
        public StateMachine(string name = null)
            : base(name, null)
        {
        }

        #region IEventDelegate

        bool IEventDelegate.RequestEventFireFromEvent(Event @event, EventFireMethod eventFireMethod)
        {
            var transitionInvoker = new EventTransitionInvoker<TState>(@event, eventFireMethod, this);
            return this.RequestEventFireFromEvent(transitionInvoker);
        }

        bool IEventDelegate.RequestEventFireFromEvent<TEventData>(Event<TEventData> @event, TEventData eventData, EventFireMethod eventFireMethod)
        {
            var transitionInvoker = new EventTransitionInvoker<TState, TEventData>(@event, eventFireMethod, eventData);
            return this.RequestEventFireFromEvent(transitionInvoker);
        }

        // invoker: Action which actually triggers the transition. Takes the state to transition from, and returns whether the transition was found
        private bool RequestEventFireFromEvent(ITransitionInvoker<TState> transitionInvoker)
        {
            if (this.Synchronizer != null)
                return this.Synchronizer.FireEvent(() => this.InvokeTransition(this.RequestEventFire, transitionInvoker), transitionInvoker.EventFireMethod);
            else
                return this.InvokeTransition(this.RequestEventFire, transitionInvoker);
        }

        private bool InvokeTransition(Func<ITransitionInvoker<TState>, bool> method, ITransitionInvoker<TState> transitionInvoker)
        {
            this.EnsureNoFault();

            if (this.executingTransition)
            {
                this.transitionQueue.Enqueue(new TransitionQueueItem(method, transitionInvoker));
                return true;
            }

            bool success;

            try
            {
                try
                {
                    this.executingTransition = true;
                    success = method(transitionInvoker);
                }
                catch (InternalTransitionFaultException e)
                {
                    var faultInfo = new StateMachineFaultInfo(this, e.FaultedComponent, e.InnerException, e.From, e.To, e.Event, e.Group);
                    this.SetFault(faultInfo);
                    throw new TransitionFailedException(faultInfo);
                }
                finally
                {
                    this.executingTransition = false;
                }

                this.FireQueuedTransitions();
            }
            finally
            {
                // Whatever happens, when we've either failed or executed everything in the transition queue,
                // the queue should end up empty.
                this.transitionQueue.Clear();
            }

            return success;
        }

        private void FireQueuedTransitions()
        {
            while (this.transitionQueue.Count > 0)
            {
                // If Fire fails, that affects the status of the outer parent transition. TryFire will not
                var item = this.transitionQueue.Dequeue();
                item.Method(item.TransitionInvoker);
            }
        }

        #endregion

        #region ITransitionDelegate

        void ITransitionDelegate<TState>.CoordinateTransition<TTransitionInfo>(TState from, TState to, IEvent @event, bool isInnerTransition, Action<TTransitionInfo> handler, TTransitionInfo transitionInfo)
        {
            // We require that from.ParentStateMachine.TopmostStateMachine == to.ParentStateMachine.TopmostStateMachine == this

            var stateHandlerInfo = new StateHandlerInfo<TState>(from, to, @event, isInnerTransition);

            if (!isInnerTransition)
            {
                if (from.ChildStateMachine != null)
                    this.ExitChildStateMachine(from.ChildStateMachine, to, @event, isInnerTransition);

                this.ExitState(stateHandlerInfo);
            }

            if (handler != null)
            {
                try
                {
                    handler(transitionInfo);
                }
                catch (Exception e)
                {
                    throw new InternalTransitionFaultException(from, to, @event, FaultedComponent.TransitionHandler, e);
                }
            }

            from.ParentStateMachine.SetCurrentState(to);

            if (!isInnerTransition)
            {
                this.EnterState(stateHandlerInfo);

                if (to.ChildStateMachine != null)
                    this.EnterChildStateMachine(to.ChildStateMachine, from, @event, isInnerTransition);
            }

            this.OnTransition(from, to, @event, from.ParentStateMachine, isInnerTransition);
        }

        private void ExitChildStateMachine(ChildStateMachine<TState> childStateMachine, TState to, IEvent @event, bool isInnerTransition)
        {
            if (childStateMachine.CurrentState != null && childStateMachine.CurrentState.ChildStateMachine != null)
                this.ExitChildStateMachine(childStateMachine.CurrentState.ChildStateMachine, to, @event, isInnerTransition);

            this.ExitState(new StateHandlerInfo<TState>(childStateMachine.CurrentState, to, @event, isInnerTransition));

            childStateMachine.SetCurrentState(null);
        }

        private void EnterChildStateMachine(ChildStateMachine<TState> childStateMachine, TState from, IEvent @event, bool isInnerTransition)
        {
            childStateMachine.SetCurrentState(childStateMachine.InitialState);

            this.EnterState(new StateHandlerInfo<TState>(from, childStateMachine.InitialState, @event, isInnerTransition));

            if (childStateMachine.InitialState.ChildStateMachine != null)
                this.EnterChildStateMachine(childStateMachine.InitialState.ChildStateMachine, from, @event, isInnerTransition);
        }

        private void ExitState(StateHandlerInfo<TState> info)
        {
            try
            {
                info.From.OnExit(info);
            }
            catch (Exception e)
            {
                throw new InternalTransitionFaultException(info.From, info.To, info.Event, FaultedComponent.ExitHandler, e);
            }

            foreach (var group in info.From.Groups.Reverse())
            {
                // We could use .Except, but that uses a HashSet which is complete overkill here
                if (info.To.Groups.Contains(group))
                    continue;

                try
                {
                    group.OnExit(info);
                }
                catch (Exception e)
                {
                    throw new InternalTransitionFaultException(info.From, info.To, info.Event, FaultedComponent.GroupExitHandler, e, group);
                }
            }
        }

        private void EnterState(StateHandlerInfo<TState> info)
        {
            try
            {
                info.To.OnEntry(info);
            }
            catch (Exception e)
            {
                throw new InternalTransitionFaultException(info.From, info.To, info.Event, FaultedComponent.EntryHandler, e);
            }

            foreach (var group in info.To.Groups)
            {
                // We could use .Except, but that uses a HashSet which is complete overkill here
                if (info.From.Groups.Contains(group))
                    continue;

                try
                {
                    group.OnEntry(info);
                }
                catch (Exception e)
                {
                    throw new InternalTransitionFaultException(info.From, info.To, info.Event, FaultedComponent.GroupEntryHandler, e, group);
                }
            }
        }

        #endregion

        #region Forced Transitions

        /// <summary>
        /// Force a transition to the given state, even though there may not be a valid configured transition to that state from the current state
        /// </summary>
        /// <remarks>Exit and entry handlers will be fired, but no transition handler will be fired</remarks>
        /// <param name="toState">State to transition to</param>
        /// <param name="event">Event pass to the exit/entry handlers</param>
        public void ForceTransition(TState toState, IEvent @event)
        {
            if (toState == null)
                throw new ArgumentNullException(nameof(toState));
            if (@event == null)
                throw new ArgumentNullException(nameof(@event));

            var transitionInvoker = new ForcedTransitionInvoker<TState>(toState, @event, this);
            if (this.Synchronizer != null)
                this.Synchronizer.ForceTransition(() => this.InvokeTransition(this.ForceTransitionImpl, transitionInvoker));
            else
                this.InvokeTransition(this.ForceTransitionImpl, transitionInvoker);
        }

        private bool ForceTransitionImpl(ITransitionInvoker<TState> transitionInvoker)
        {
            transitionInvoker.TryInvoke(this.CurrentState);
            return true;
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Serialize the current state of this state machine into a string, which can later be used to restore the state of the state machine
        /// </summary>
        /// <returns></returns>
        public string Serialize()
        {
            return this.serializer.Serialize(this);
        }

        /// <summary>
        /// Restore the state of this state machine from a previously-created string (from calling the <see cref="Serialize()"/> method).
        /// </summary>
        /// <param name="serialized">String created by <see cref="Serialize()"/></param>
        public void Deserialize(string serialized)
        {
            this.Reset();

            var childState = this.serializer.Deserialize(this, serialized);
            var intermediateStates = new List<TState>();
            for (TState state = childState; state != null; state = state.ParentStateMachine.ParentState)
            {
                intermediateStates.Add(state);
            }

            ChildStateMachine<TState> stateMachine = this;

            foreach (var state in intermediateStates.AsEnumerable().Reverse())
            {
                // We should never hit this
                Trace.Assert(stateMachine != null, $"Unable to deserialize from \"{serialized}\": the previous state has no child state machine. This should not happen");

                // This will throw if the state doesn't belong to the state machine
                stateMachine.SetCurrentState(state);

                stateMachine = state.ChildStateMachine;
            }

            // Did we run out of identifiers?
            // We need to check this to avoid internal inconsistency
            if (stateMachine != null)
                throw new StateMachineSerializationException($"Unable to deserialize from \"{serialized}\": a parent state has the child state machine {stateMachine}, but no information is present in the serialized string saying what its state should be. Make sure you're deserializing into exactly the same state machine as created the serialized string.");
        }

        #endregion

        #region Resetting

        /// <summary>
        /// Resets the state machine, removing any fault and returning it and any child state machines to their initial state
        /// </summary>
        public void Reset()
        {
            if (this.Synchronizer != null)
                this.Synchronizer.Reset(this.ResetInternal);
            else
                this.ResetInternal();
        }

        private void ResetInternal()
        {
            this.Fault = null;
            this.transitionQueue.Clear();

            this.ResetChildStateMachine();
        }

        #endregion

        #region Events and helpers

        internal void EnsureNoFault()
        {
            if (this.Fault != null)
                throw new StateMachineFaultedException(this.Fault);
        }

        private void SetFault(StateMachineFaultInfo faultInfo)
        {
            this.Fault = faultInfo;
            this.Faulted?.Invoke(this, new StateMachineFaultedEventArgs(faultInfo));
        }

        internal override void HandleTransitionNotFound(IEvent @event, EventFireMethod eventFireMethod)
        {
            this.TransitionNotFound?.Invoke(this, new TransitionNotFoundEventArgs<TState>(this.CurrentState, @event, this, eventFireMethod));

            if (eventFireMethod == EventFireMethod.Fire)
                throw new TransitionNotFoundException(this.CurrentState, @event, this);
        }

        private void OnTransition(TState from, TState to, IEvent @event, IStateMachine stateMachine, bool isInnerTransition)
        {
            this.Transition?.Invoke(this, new TransitionEventArgs<TState>(from, to, @event, stateMachine, isInnerTransition));
        }

        #endregion

        private struct TransitionQueueItem
        {
            public readonly Func<ITransitionInvoker<TState>, bool> Method;
            public readonly ITransitionInvoker<TState> TransitionInvoker;

            public TransitionQueueItem(Func<ITransitionInvoker<TState>, bool> method, ITransitionInvoker<TState> transitionInvoker)
            {
                this.Method = method;
                this.TransitionInvoker = transitionInvoker;
            }
        }
    }
}
