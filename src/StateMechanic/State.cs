﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StateMechanic
{
    public delegate void StateHandler(StateHandlerInfo<State> info);
    public delegate void StateHandler<TStateData>(StateHandlerInfo<State<TStateData>> info);

    internal class StateInner<TState> where TState : IState<TState>
    {
        public readonly IStateDelegate<TState> StateDelegate;

        public string Name;

        internal StateInner(string name, IStateDelegate<TState> stateDelegate)
        {
            this.Name = name;
            this.StateDelegate = stateDelegate;
        }

        public ITransitionBuilder<TState> AddTransitionOn(TState fromState, Event evt)
        {
            return new TransitionBuilder<TState>(fromState, evt, this.StateDelegate);
        }

        public ITransitionBuilder<TState, TEventData> AddTransitionOn<TEventData>(TState fromState, Event<TEventData> evt)
        {
            return new TransitionBuilder<TState, TEventData>(fromState, evt, this.StateDelegate);
        }

        public Transition<TState> AddInnerSelfTransitionOn(TState fromAndToState, Event evt)
        {
            return new Transition<TState>(fromAndToState, evt, this.StateDelegate);
        }

        public Transition<TState, TEventData> AddInnerSelfTransitionOn<TEventData>(TState fromAndToState, Event<TEventData> evt)
        {
            return new Transition<TState, TEventData>(fromAndToState, evt, this.StateDelegate);
        }
    }

    public class State : IState<State>
    {
        private readonly StateInner<State> innerState;

        public StateHandler OnEntry { get; set; }
        public StateHandler OnExit { get; set; }

        public string Name { get { return this.innerState.Name; } }

        public StateMachine ChildStateMachine { get; private set; }

        internal IStateDelegate<State> StateDelegate
        {
            get { return this.innerState.StateDelegate; }
        }

        public IStateMachine StateMachine
        {
            get { return this.innerState.StateDelegate; }
        }

        internal State(string name, IStateDelegate<State> stateDelegate)
        {
            this.innerState = new StateInner<State>(name, stateDelegate);
        }

        public ITransitionBuilder<State> AddTransitionOn(Event evt)
        {
             return this.innerState.AddTransitionOn(this, evt);
        }

        public ITransitionBuilder<State, TEventData> AddTransitionOn<TEventData>(Event<TEventData> evt)
        {
            return this.innerState.AddTransitionOn<TEventData>(this, evt);
        }

        public ITransition<State> AddInnerSelfTransitionOn(Event evt)
        {
            return this.innerState.AddInnerSelfTransitionOn(this, evt);
        }

        public ITransition<State, TEventData> AddInnerSelfTransitionOn<TEventData>(Event<TEventData> evt)
        {
            return this.innerState.AddInnerSelfTransitionOn<TEventData>(this, evt);
        }

        public State WithEntry(StateHandler onEntry)
        {
            this.OnEntry = onEntry;
            return this;
        }

        public State WithExit(StateHandler onExit)
        {
            this.OnExit = onExit;
            return this;
        }

        public StateMachine CreateChildStateMachine(string name)
        {
            this.ChildStateMachine = new StateMachine(name, this.StateDelegate);
            return this.ChildStateMachine;
        }

        void IState<State>.FireOnEntry(StateHandlerInfo<State> info)
        {
            var onEntry = this.OnEntry;
            if (onEntry != null)
                onEntry(info);

            if (this.ChildStateMachine != null)
                this.ChildStateMachine.ForceTransition(info.To, this.ChildStateMachine.InitialState, this.ChildStateMachine.InitialState, info.Event);
        }

        void IState<State>.FireOnExit(StateHandlerInfo<State> info)
        {
            if (this.ChildStateMachine != null)
                this.ChildStateMachine.ForceTransition(this.ChildStateMachine.CurrentState, info.From, null, info.Event);

            var onExit = this.OnExit;
            if (onExit != null)
                onExit(info);
        }

        string IState.Name
        {
            get { return this.innerState.Name; }
        }

        IStateMachine<State> IState<State>.ChildStateMachine
        {
            get { return this.ChildStateMachine; }
        }
    }

    public class State<TStateData> : IState<State<TStateData>>
    {
        private readonly StateInner<State<TStateData>> innerState;

        public TStateData Data { get; set; }
        public StateHandler<TStateData> OnEntry { get; set; }
        public StateHandler<TStateData> OnExit { get; set; }

        public StateMachine<TStateData> ChildStateMachine { get; private set; }

        public string Name { get { return this.innerState.Name; } }

        internal IStateDelegate<State<TStateData>> StateDelegate
        {
            get { return this.innerState.StateDelegate; }
        }

        public IStateMachine StateMachine
        {
            get { return this.innerState.StateDelegate; }
        }

        internal State(string name, IStateDelegate<State<TStateData>> stateDelegate)
        {
            this.innerState = new StateInner<State<TStateData>>(name, stateDelegate);
        }

        public ITransitionBuilder<State<TStateData>> AddTransitionOn(Event evt)
        {
            return this.innerState.AddTransitionOn(this, evt);
        }

        public ITransitionBuilder<State<TStateData>, TEventData> AddTransitionOn<TEventData>(Event<TEventData> evt)
        {
            return this.innerState.AddTransitionOn<TEventData>(this, evt);
        }

        public ITransition<State<TStateData>> AddInnerSelfTransitionOn(Event evt)
        {
            return this.innerState.AddInnerSelfTransitionOn(this, evt);
        }

        public ITransition<State<TStateData>, TEventData> AddInnerSelfTransitionOn<TEventData>(Event<TEventData> evt)
        {
            return this.innerState.AddInnerSelfTransitionOn<TEventData>(this, evt);
        }

        public State<TStateData> WithData(TStateData data)
        {
            this.Data = data;
            return this;
        }

        public State<TStateData> WithEntry(StateHandler<TStateData> onEntry)
        {
            this.OnEntry = onEntry;
            return this;
        }

        public State<TStateData> WithExit(StateHandler<TStateData> onExit)
        {
            this.OnExit = onExit;
            return this;
        }

        public StateMachine<TStateData> CreateChildStateMachine(string name)
        {
            this.ChildStateMachine = new StateMachine<TStateData>(name, this.StateDelegate);
            return this.ChildStateMachine;
        }

        void IState<State<TStateData>>.FireOnEntry(StateHandlerInfo<State<TStateData>> info)
        {
            var onEntry = this.OnEntry;
            if (onEntry != null)
                onEntry(info);

            if (this.ChildStateMachine != null)
                this.ChildStateMachine.ForceTransition(info.To, this.ChildStateMachine.InitialState, this.ChildStateMachine.InitialState, info.Event);
        }

        void IState<State<TStateData>>.FireOnExit(StateHandlerInfo<State<TStateData>> info)
        {
            if (this.ChildStateMachine != null)
                this.ChildStateMachine.ForceTransition(this.ChildStateMachine.CurrentState, info.From, null, info.Event);

            var onExit = this.OnExit;
            if (onExit != null)
                onExit(info);
        }

        string IState.Name
        {
            get { return this.innerState.Name; }
        }

        IStateMachine<State<TStateData>> IState<State<TStateData>>.ChildStateMachine
        {
            get { return this.ChildStateMachine; }
        }
    }
}
