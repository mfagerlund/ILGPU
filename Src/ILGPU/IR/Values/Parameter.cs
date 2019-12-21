// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2019 Marcel Koester
//                                www.ilgpu.net
//
// File: Parameter.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details
// -----------------------------------------------------------------------------

using ILGPU.IR.Construction;
using ILGPU.IR.Types;
using ILGPU.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace ILGPU.IR.Values
{
    /// <summary>
    /// Represents a function or a block parameter.
    /// </summary>
    public sealed class Parameter : Value
    {
        #region Instance

        /// <summary>
        /// Constructs a new parameter.
        /// </summary>
        /// <param name="method">The parent method.</param>
        /// <param name="type">The parameter type.</param>
        /// <param name="name">The parameter name (for debugging purposes).</param>
        internal Parameter(
            Method method,
            TypeNode type,
            string name)
            : this(method, null, type, name)
        { }

        /// <summary>
        /// Constructs a new parameter.
        /// </summary>
        /// <param name="basicBlock">The associated basic block.</param>
        /// <param name="type">The parameter type.</param>
        /// <param name="name">The parameter name (for debugging purposes).</param>
        internal Parameter(
            BasicBlock basicBlock,
            TypeNode type,
            string name)
            : this(basicBlock.Method, basicBlock, type, name)
        { }

        /// <summary>
        /// Constructs a new parameter.
        /// </summary>
        /// <param name="method">The parent method.</param>
        /// <param name="basicBlock">The associated basic block.</param>
        /// <param name="type">The parameter type.</param>
        /// <param name="name">The parameter name (for debugging purposes).</param>
        private Parameter(
            Method method,
            BasicBlock basicBlock,
            TypeNode type,
            string name)
            : base(ValueKind.Parameter, basicBlock, type, basicBlock != null)
        {
            Method = method;
            ParameterType = type;
            Name = name ?? (basicBlock != null ? "param" : "phi");
            Seal(ImmutableArray<ValueReference>.Empty);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the actual parameter type.
        /// </summary>
        public TypeNode ParameterType { get; }

        /// <summary>
        /// Returns the parameter name (for debugging purposes).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Returns the parameter index.
        /// </summary>
        public int Index { get; internal set; } = -1;

        /// <summary>
        /// Returns true if this is a function parameter.
        /// </summary>
        public bool IsFunctionParameter => BasicBlock == null;

        /// <summary>
        /// Returns true if this is a block parameter.
        /// </summary>
        public bool IsBlockParameter => BasicBlock != null;

        #endregion

        #region Methods

        /// <summary cref="Value.UpdateType(IRContext)"/>
        protected override TypeNode UpdateType(IRContext context) => ParameterType;

        /// <summary cref="Value.Rebuild(IRBuilder, IRRebuilder)"/>
        protected internal override Value Rebuild(IRBuilder builder, IRRebuilder rebuilder)
        {
            // Params have already been mapped in the beginning
            return rebuilder.Rebuild(this);
        }

        /// <summary cref="Value.Accept" />
        public override void Accept<T>(T visitor) => visitor.Visit(this);

        #endregion

        #region Object

        /// <summary cref="Node.ToPrefixString"/>
        protected override string ToPrefixString() => Name;

        /// <summary cref="Value.ToArgString"/>
        protected override string ToArgString() =>
            Type.ToString() + " @ " +
                (IsFunctionParameter ?
                Method.ToReferenceString() :
                BasicBlock.ToReferenceString());

        /// <summary>
        /// Return the parameter string.
        /// </summary>
        /// <returns>The parameter string.</returns>
        internal string ToParameterString() =>
            $"{Type.ToString()} {ToReferenceString()}";

        #endregion
    }

    /// <summary>
    /// An abstract interface that 
    /// </summary>
    public interface IParameterContainer
    {
        /// <summary>
        /// Returns all attached parameters.
        /// </summary>
        ParameterCollection Parameters { get; }

        /// <summary>
        /// Returns the number of attached parameters.
        /// </summary>
        int NumParameters { get; }
    }

    /// <summary>
    /// Represents a readonly view on all parameters.
    /// </summary>
    public readonly struct ParameterCollection : IReadOnlyList<Parameter>
    {
        #region Nested Types

        /// <summary>
        /// Represents an abstract parameter builder.
        /// </summary>
        public interface IParameterBuilder
        {
            /// <summary>
            /// Will be invoked if the given parameter has been added to the parameter collection.
            /// </summary>
            /// <param name="parameter">The parameter that has been added.</param>
            void Add(Parameter parameter);

            /// <summary>
            /// Will be invoked if a parameter has been replaced and will be removed.
            /// </summary>
            /// <param name="parameter">The parameter that has been removed.</param>
            void Remove(Parameter parameter);

            /// <summary>
            /// Creates a parameter with the type information and name.
            /// </summary>
            /// <param name="type">The parameter type.</param>
            /// <param name="name">The parameter name (for debugging purposes).</param>
            /// <returns>The created parameter.</returns>
            Parameter CreateParameter(TypeNode type, string name);
        }

        /// <summary>
        /// Represents a parameter-list builder.
        /// </summary>
        public struct Builder<TParameterBuilder> : IDisposable
            where TParameterBuilder : IParameterBuilder
        {
            #region Instance

            /// <summary>
            /// The current parameter builder
            /// </summary>
            private TParameterBuilder parameterBuilder;

            /// <summary>
            /// The underlying parameter list to use.
            /// </summary>
            private readonly List<Parameter> parameters;

            /// <summary>
            /// Constructs a new builder.
            /// </summary>
            /// <param name="builder">The parameter builder to use.</param>
            /// <param name="parameterList">The parameter list to use.</param>
            internal Builder(in TParameterBuilder builder, List<Parameter> parameterList)
            {
                parameterBuilder = builder;
                parameters = parameterList;
            }

            #endregion

            #region Properties

            /// <summary>
            /// Returns the number of parameters.
            /// </summary>
            public int Count => parameters.Count;

            /// <summary>
            /// Returns the i-th parameter.
            /// </summary>
            /// <param name="index">The index of the parameter to return.</param>
            /// <returns>The resulting parameter.</returns>
            public Parameter this[int index] => parameters[index];

            #endregion

            #region Methods

            /// <summary>
            /// Adds the given parameter to this collection.
            /// </summary>
            /// <param name="parameter">The parameter to use.</param>
            public void Add(Parameter parameter)
            {
                parameter.Index = parameters.Count;
                parameters.Add(parameter);
                parameterBuilder.Add(parameter);
            }

            /// <summary>
            /// Adds the given parameters to this collection.
            /// </summary>
            /// <param name="builder">The other builder.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add(Builder<TParameterBuilder> builder)
            {
                foreach (var param in builder.parameters)
                    Add(param);
            }

            /// <summary>
            /// Adds the given parameters to this collection.
            /// </summary>
            /// <param name="others">The parameters to add.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add(ParameterCollection others)
            {
                foreach (var param in others)
                    Add(param);
            }

            /// <summary>
            /// Resolves the index of the given parameter.
            /// </summary>
            /// <param name="parameter">The parameter.</param>
            /// <returns>The resolved index (if any).</returns>
            public int IndexOf(Parameter parameter) => parameters.IndexOf(parameter);

            /// <summary>
            /// Clears this parameter list.
            /// </summary>
            public void Clear() => parameters.Clear();

            /// <summary>
            /// Returns true if the given parameter could be found.
            /// </summary>
            /// <param name="parameter">The parameter to find.</param>
            /// <returns>True, if the given parameter could be found.</returns>
            public bool Contains(Parameter parameter) => parameters.Contains(parameter);

            /// <summary>
            /// Removes the given parameter.
            /// </summary>
            /// <param name="parameter">The parameter to remove.</param>
            /// <returns>True, if the given parameter could be removed.</returns>
            public bool Remove(Parameter parameter) => parameters.Remove(parameter);

            /// <summary>
            /// Removes the specified parameter.
            /// </summary>
            /// <param name="index">The index of the parameter to remove.</param>
            public void RemoveAt(int index) => parameters.RemoveAt(index);

            /// <summary>
            /// Adds a new parameter to the associated IR object.
            /// </summary>
            /// <param name="type">The parameter type.</param>
            /// <returns>The created parameter.</returns>
            public Parameter AddParameter(TypeNode type) => AddParameter(type, null);

            /// <summary>
            /// Adds a new parameter to the associated IR object.
            /// </summary>
            /// <param name="type">The parameter type.</param>
            /// <param name="name">The parameter name (for debugging purposes).</param>
            /// <returns>The created parameter.</returns>
            public Parameter AddParameter(TypeNode type, string name)
            {
                var param = parameterBuilder.CreateParameter(type, name);
                Add(param);
                return param;
            }

            /// <summary>
            /// Inserts a new parameter at the beginning.
            /// </summary>
            /// <param name="type">The parameter type.</param>
            /// <returns>The created parameter.</returns>
            public Parameter InsertParameter(TypeNode type) => InsertParameter(type, null);

            /// <summary>
            /// Inserts a new parameter at the beginning.
            /// </summary>
            /// <param name="type">The parameter type.</param>
            /// <param name="name">The parameter name (for debugging purposes).</param>
            /// <returns>The created parameter.</returns>
            public Parameter InsertParameter(TypeNode type, string name)
            {
                var param = parameterBuilder.CreateParameter(type, name);
                parameters.Insert(0, param);
                UpdateIndices();
                return param;
            }

            /// <summary>
            /// Updates all parameter indices.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void UpdateIndices()
            {
                // Adjust parameter indices
                for (int i = 0, e = parameters.Count; i < e; ++i)
                    parameters[i].Index = i;
            }

            /// <summary>
            /// Removes parameters that have been replaced and updates all indices accordignly.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void PerformRemoval()
            {
                for (int i = 0; i < Count;)
                {
                    var param = parameters[i];
                    if (param.IsReplaced)
                    {
                        parameterBuilder.Remove(param);
                        parameters.RemoveAt(i);
                    }
                    else
                        param.Index = i++;
                }
            }

            #endregion

            #region IDisposable

            /// <summary>
            /// Disposes this builder and applies all changes.
            /// </summary>
            public void Dispose() => PerformRemoval();

            #endregion
        }

        /// <summary>
        /// Enumerates all actual (not replaced) parameters.
        /// </summary>
        public struct Enumerator : IEnumerator<Parameter>
        {
            private List<Parameter>.Enumerator enumerator;

            /// <summary>
            /// Constructs a new parameter enumerator.
            /// </summary>
            /// <param name="arguments">The parent source array.</param>
            internal Enumerator(List<Parameter> arguments)
            {
                enumerator = arguments.GetEnumerator();
            }

            /// <summary>
            /// Returns the current parameter.
            /// </summary>
            public Parameter Current => enumerator.Current;

            /// <summary cref="IEnumerator.Current"/>
            object IEnumerator.Current => Current;

            /// <summary cref="IDisposable.Dispose"/>
            public void Dispose() => enumerator.Dispose();

            /// <summary cref="IEnumerator.MoveNext"/>
            public bool MoveNext() => enumerator.MoveNext();

            /// <summary cref="IEnumerator.Reset"/>
            void IEnumerator.Reset() => throw new InvalidOperationException();
        }

        #endregion

        #region Static

        /// <summary>
        /// Creates an empty parameter collection.
        /// </summary>
        /// <returns>The parameter collection.</returns>
        public static ParameterCollection Create() =>
            new ParameterCollection(new List<Parameter>());

        #endregion

        #region Instance

        private readonly List<Parameter> parameters;

        /// <summary>
        /// Constructs a new parameter collection.
        /// </summary>
        /// <param name="parameterList">The source parameters.</param>
        internal ParameterCollection(List<Parameter> parameterList)
        {
            parameters = parameterList;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the number of attached parameters.
        /// </summary>
        public int Count => parameters.Count;

        /// <summary>
        /// Returns the i-th parameter.
        /// </summary>
        /// <param name="index">The parameter index.</param>
        /// <returns>The resolved parameter.</returns>
        public Parameter this[int index] => parameters[index];

        #endregion

        #region Methods

        /// <summary>
        /// Converts this readonly collection to a builder.
        /// </summary>
        /// <param name="builder">The builder to use.</param>
        /// <returns>The resolved builder.</returns>
        internal Builder<TParameterBuilder> ToBuilder<TParameterBuilder>(
            in TParameterBuilder builder)
            where TParameterBuilder : IParameterBuilder =>
            new Builder<TParameterBuilder>(builder, parameters);

        /// <summary>
        /// Returns an enumerator to enumerate all actual (not replaced) parameters.
        /// </summary>
        /// <returns>The enumerator.</returns>
        public Enumerator GetEnumerator() => new Enumerator(parameters);

        /// <summary>
        /// Returns an enumerator to enumerator all actual (not replaced) parameters.
        /// </summary>
        /// <returns>The enumerator.</returns>
        IEnumerator<Parameter> IEnumerable<Parameter>.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Returns an enumerator to enumerator all actual (not replaced) parameters.
        /// </summary>
        /// <returns>The enumerator.</returns>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion
    }
}
