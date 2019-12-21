// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2019 Marcel Koester
//                                www.ilgpu.net
//
// File: BlockArgumentBindings.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details
// -----------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Analyses;
using ILGPU.IR.Values;
using System.Collections;
using System.Collections.Generic;

namespace ILGPU.Backends
{
    /// <summary>
    /// An abstract binding allocator for the <see cref="BlockArgumentBindings{TParameterAllocator, TBinding}"/>
    /// class.
    /// </summary>
    /// <typeparam name="TBinding">The custom binding type (e.g. a variable or a register).</typeparam>
    public interface IBlockArgumentBindingsAllocator<TBinding>
    {
        /// <summary>
        /// Processes all parameters that are declared in the given basic block.
        /// </summary>
        /// <param name="block">The current basic block.</param>
        void Process(BasicBlock block);

        /// <summary>
        /// Allocates the given block parameter.
        /// </summary>
        /// <param name="block">The current block.</param>
        /// <param name="parameter">The parameter to allocate.</param>
        TBinding Allocate(BasicBlock block, Parameter parameter);
    }

    /// <summary>
    /// Utility methods for <see cref="BlockArgumentBindings{TParameterAllocator, TBinding}"/>
    /// </summary>
    public static class BlockArgumentBindings
    {
        /// <summary>
        /// Creates a new bindings mapping.
        /// </summary>
        /// <param name="scope">The source scope.</param>
        /// <param name="allocator">The allocator to use.</param>
        /// <returns>The created bindings.</returns>
        public static BlockArgumentBindings<TParameterAllocator, TBinding>
            Create<TParameterAllocator, TBinding>(
            Scope scope,
            TParameterAllocator allocator)
            where TParameterAllocator : IBlockArgumentBindingsAllocator<TBinding> =>
            BlockArgumentBindings<TParameterAllocator, TBinding>.Create(scope, allocator);
    }

    /// <summary>
    /// A helper that manages block argument allocations and helps to wire block arguments with
    /// their associated block parameters.
    /// </summary>
    /// <typeparam name="TParameterAllocator">The custom parameter allocator.</typeparam>
    /// <typeparam name="TBinding">The custom binding type (e.g. a variable or a register).</typeparam>
    public readonly struct BlockArgumentBindings<TParameterAllocator, TBinding>
        where TParameterAllocator : IBlockArgumentBindingsAllocator<TBinding>
    {
        #region Nested Types

        /// <summary>
        /// Represents a variable-parameter binding entry.
        /// </summary>
        public readonly struct BindingEntry
        {
            /// <summary>
            /// Constructs a new binding entry.
            /// </summary>
            /// <param name="value">The value to bind.</param>
            /// <param name="parameter">The target parameter to bind to.</param>
            /// <param name="binding">The binding of the parameter value.</param>
            internal BindingEntry(
                Value value,
                Parameter parameter,
                TBinding binding)
            {
                Value = value;
                Parameter = parameter;
                Binding = binding;
            }
            #region Instance

            #endregion

            #region Properties

            /// <summary>
            /// The block argument value to bind.
            /// </summary>
            public Value Value { get; }

            /// <summary>
            /// The block parameter of a successor block to bind to.
            /// </summary>
            public Parameter Parameter { get; }

            /// <summary>
            /// The current binding that represents a variable or a register.
            /// </summary>
            public TBinding Binding { get; }

            #endregion
        }

        /// <summary>
        /// Represents a readonly list of phi entries.
        /// </summary>
        public readonly struct BindingCollection : IEnumerable<BindingEntry>
        {
            #region Instance

            /// <summary>
            /// Constructs a new binding collection.
            /// </summary>
            /// <param name="bindings">The parent bindings.</param>
            /// <param name="block">The current block.</param>
            internal BindingCollection(
                in BlockArgumentBindings<TParameterAllocator, TBinding> bindings,
                BasicBlock block)
            {
                Parent = bindings;
                Block = block;
            }

            #endregion

            #region Properties

            /// <summary>
            /// Returns the parent block argument binding.
            /// </summary>
            public BlockArgumentBindings<TParameterAllocator, TBinding> Parent { get; }

            /// <summary>
            /// Returns the current block.
            /// </summary>
            public BasicBlock Block { get; }

            #endregion

            #region Methods

            /// <summary>
            /// Returns an enumerator to enumerate all entries in this collection.
            /// </summary>
            /// <returns>An enumerator to enumerate all entries in this collection.</returns>
            public IEnumerator<BindingEntry> GetEnumerator()
            {
                var arguments = Block.Arguments;
                foreach (var successor in Block.Successors)
                {
                    for (int i = 0, e = arguments.Length; i < e; ++i)
                    {
                        var param = successor.Parameters[i];
                        yield return new BindingEntry(
                            arguments[i],
                            param,
                            Parent.bindingMapping[param]);
                    }
                }
            }

            /// <summary cref="IEnumerable.GetEnumerator"/>
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            #endregion
        }

        #endregion

        #region Static

        /// <summary>
        /// Creates a new bindings mapping.
        /// </summary>
        /// <param name="scope">The source scope.</param>
        /// <param name="allocator">The allocator to use.</param>
        /// <returns>The created phi bindings.</returns>
        public static BlockArgumentBindings<TParameterAllocator, TBinding> Create(
            Scope scope,
            TParameterAllocator allocator) =>
            new BlockArgumentBindings<TParameterAllocator, TBinding>(scope, allocator);

        #endregion

        #region Instance

        /// <summary>
        /// Stores all parameter bindings.
        /// </summary>
        private readonly Dictionary<Parameter, TBinding> bindingMapping;

        /// <summary>
        /// Constructs new block parameter bindings.
        /// </summary>
        /// <param name="scope">The source scope.</param>
        /// <param name="allocator">The allocator to use.</param>
        private BlockArgumentBindings(Scope scope, TParameterAllocator allocator)
        {
            Allocator = allocator;
            bindingMapping = new Dictionary<Parameter, TBinding>();

            foreach (var block in scope)
            {
                // Check for block parameters
                if (block.NumParameters > 0)
                {
                    allocator.Process(block);
                    foreach (var param in block.Parameters)
                        bindingMapping[param] = allocator.Allocate(block, param);
                }
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated parameter allocator.
        /// </summary>
        public TParameterAllocator Allocator { get; }

        /// <summary>
        /// Returns a binding collection for all block arguments of the given block.
        /// </summary>
        /// <param name="block">The block.</param>
        /// <returns>
        /// The resulting binding collection containing binding information for all
        /// block arguments.
        /// </returns>
        public BindingCollection this[BasicBlock block] => new BindingCollection(this, block);

        #endregion
    }
}
