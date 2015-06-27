﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StateMechanic
{
    // Given an IState, returns an action to invoke the transition handler on it, or null if it doesn't exist
    internal delegate bool EventInvocation(Func<IState, Action> transitionInvocation);

    internal class EventInner<TEvent, TTransition>
    {
        private readonly Dictionary<IState, List<TTransition>> transitions = new Dictionary<IState, List<TTransition>>();

        public readonly string Name;
        public readonly IEventDelegate eventDelegate;

        public EventInner(string name, IEventDelegate eventDelegate)
        {
            this.Name = name;
            this.eventDelegate = eventDelegate;
        }

        public void AddTransition(IState state, TTransition transitionInvocation)
        {
            List<TTransition> transitions;
            if (!this.transitions.TryGetValue(state, out transitions))
            {
                transitions = new List<TTransition>();
                this.transitions.Add(state, transitions);
            }

            transitions.Add(transitionInvocation);
        }

        public bool Fire(Func<TTransition, bool> transitionInvoker, IEvent parentEvent, bool throwIfNotFound)
        {
            return this.eventDelegate.RequestEventFireFromEvent(parentEvent, state =>
            {
                List<TTransition> transitions;
                if (!this.transitions.TryGetValue(state, out transitions))
                    return false;

                // Keep trying until one works (i.e. has a guard that lets it execute)
                bool anyFound = false;
                foreach (var transition in transitions)
                {
                    if (transitionInvoker(transition))
                    {
                        anyFound = true;
                        break;
                    }
                }

                return anyFound;
            }, throwIfNotFound);
        }
    }

    public class Event<TEventData> : IEvent
    {
        private readonly EventInner<Event<TEventData>, IInvokableTransition<TEventData>> innerEvent;

        public string Name { get { return this.innerEvent.Name; } }
        public IStateMachine ParentStateMachine { get { return this.innerEvent.eventDelegate; } }

        internal Event(string name, IEventDelegate eventDelegate)
        {
            this.innerEvent = new EventInner<Event<TEventData>, IInvokableTransition<TEventData>>(name, eventDelegate);
        }

        internal void AddTransition(IState state, IInvokableTransition<TEventData> transition)
        {
            this.innerEvent.AddTransition(state, transition);
        }

        public bool TryFire(TEventData eventData)
        {
            return this.innerEvent.Fire(transition => transition.TryInvoke(eventData), this, false);
        }

        public void Fire(TEventData eventData)
        {
            this.innerEvent.Fire(transition => transition.TryInvoke(eventData), this, true);
        }

        void IEvent.Fire()
        {
            this.Fire(default(TEventData));
        }

        bool IEvent.TryFire()
        {
            return this.TryFire(default(TEventData));
        }

        public override string ToString()
        {
            return String.Format("<Event Name={0}>", this.Name);
        }
    }

    public class Event : IEvent
    {
        private readonly EventInner<Event, IInvokableTransition> innerEvent;

        public string Name { get { return this.innerEvent.Name; } }
        public IStateMachine ParentStateMachine { get { return this.innerEvent.eventDelegate; } }

        internal Event(string name, IEventDelegate eventDelegate)
        {
            this.innerEvent = new EventInner<Event, IInvokableTransition>(name, eventDelegate);
        }

        internal void AddTransition(IState state, IInvokableTransition transition)
        {
            this.innerEvent.AddTransition(state, transition);
        }

        public bool TryFire()
        {
            return this.innerEvent.Fire(transition => transition.TryInvoke(), this, false);
        }

        public void Fire()
        {
            this.innerEvent.Fire(transition => transition.TryInvoke(), this, true);
        }

        public override string ToString()
        {
            return String.Format("<Event Name={0}>", this.Name);
        }
    }
}
