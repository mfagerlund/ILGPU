// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2019 Marcel Koester
//                                www.ilgpu.net
//
// File: BasicBlock.Builder.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details
// -----------------------------------------------------------------------------

using ILGPU.Frontend.DebugInformation;
using ILGPU.IR.Analyses;
using ILGPU.IR.Construction;
using ILGPU.IR.Types;
using ILGPU.IR.Values;
using ILGPU.Util;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ILGPU.IR
{
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
        Justification = "Handled in Method.Builder.Dispose(bool)")]
    partial class BasicBlock
    {
        /// <summary>
        /// A parameter builder for use in combination with the
        /// <see cref="ParameterCollection.Builder{TParameterBuilder}"/> object.
        /// </summary>
        public readonly struct ParameterBuilder : ParameterCollection.IParameterBuilder
        {
            /// <summary>
            /// Constructs a new parameter builder.
            /// </summary>
            /// <param name="builder">The parent builder.</param>
            public ParameterBuilder(Builder builder)
            {
                Debug.Assert(builder != null, "Invalid builder");
                Builder = builder;
            }

            /// <summary>
            /// Returns the current basic block.
            /// </summary>
            public Builder Builder { get; }

            /// <summary cref="ParameterCollection.IParameterBuilder.Add(Parameter)"/>
            public void Add(Parameter parameter) => parameter.BasicBlock = Builder.BasicBlock;

            /// <summary cref="ParameterCollection.IParameterBuilder.Remove(Parameter)"/>
            public void Remove(Parameter parameter) => parameter.BasicBlock = null;

            /// <summary cref="ParameterCollection.IParameterBuilder.CreateParameter(TypeNode, string)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Parameter CreateParameter(TypeNode type, string name)
            {
                var param = new Parameter(Builder.BasicBlock, type, name);
                Builder.Context.Create(param);
                return param;
            }
        }

        /// <summary>
        /// Represents a basic block builder.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1710: IdentifiersShouldHaveCorrectSuffix",
            Justification = "This is the correct name of the current entity")]
        public sealed class Builder : IRBuilder, IEnumerable<ValueEntry>
        {
            #region Instance

            /// <summary>
            /// A local cache of the value list.
            /// </summary>
            private List<ValueReference> values;

            /// <summary>
            /// A collection of values to remove
            /// </summary>
            private readonly HashSet<Value> toRemove = new HashSet<Value>();

            /// <summary>
            /// The current insert position for new instructions.
            /// </summary>
            private int insertPosition;

            private Branch currentBranch;

            /// <summary>
            /// The current branch builders.
            /// </summary>
            private BranchTarget.Builders targetBuilders;

            /// <summary>
            /// Constructs a new builder.
            /// </summary>
            /// <param name="methodBuilder">The parent method builder.</param>
            /// <param name="block">The parent block.</param>
            internal Builder(Method.Builder methodBuilder, BasicBlock block)
                : base(block)
            {
                Debug.Assert(methodBuilder != null, "Invalid method builder");
                Debug.Assert(block != null, "Invalid block");
                MethodBuilder = methodBuilder;

                Parameters = block.Parameters.ToBuilder(new ParameterBuilder(this));
                targetBuilders = new BranchTarget.Builders(this);

                values = block.values;
                insertPosition = Count;
                Terminator = block.CompactTerminator();
            }

            #endregion

            #region Properties

            /// <summary>
            /// Returns the parent function builder.
            /// </summary>
            public Method.Builder MethodBuilder { get; }

            /// <summary>
            /// Returns the current parameter builder.
            /// </summary>
            public ParameterCollection.Builder<ParameterBuilder> Parameters { get; }

            /// <summary>
            /// Gets or sets the current terminator.
            /// </summary>
            public TerminatorValue Terminator
            {
                get => BasicBlock.Terminator;
                private set => BasicBlock.Terminator = value;
            }

            /// <summary>
            /// Returns the number of attached values.
            /// </summary>
            public int Count => Values.Count;

            /// <summary>
            /// Gets or sets the current value list.
            /// </summary>
            private List<ValueReference> Values
            {
                get => values;
                set { BasicBlock.values = values = value; }
            }

            /// <summary>
            /// Gets or sets the current insert position for new instructions.
            /// </summary>
            public int InsertPosition
            {
                get => insertPosition;
                set
                {
                    Debug.Assert(value >= 0 && value <= Count, "Invalid insert position");
                    insertPosition = value;
                }
            }

            #endregion

            #region Methods

            /// <summary>
            /// Adds the given value to the current block arguments.
            /// </summary>
            /// <param name="target">The target block..</param>
            /// <param name="value">The value to add.</param>
            public void AddArgument(BasicBlock target, Value value) =>
                SetupBranchBuilder().AddArgument(target, value);

            /// <summary>
            /// Returns the i-th argument.
            /// </summary>
            /// <param name="target">The target block.</param>
            /// <param name="index">The argument index.</param>
            /// <returns>The desired argument.</returns>
            public Value GetArgument(BasicBlock target, int index) =>
                SetupBranchBuilder()[target][index];

            /// <summary>
            /// Maps all block arguments using the given mapper.
            /// </summary>
            /// <typeparam name="TMapper">The mapper type.</typeparam>
            /// <param name="mapper">The mapper instance to use.</param>
            public void MapArguments<TMapper>(TMapper mapper)
                where TMapper : BranchTarget.IArgumentMapper =>
                SetupBranchBuilder().MapArguments(mapper);

            /// <summary>
            /// Maps all block arguments using the given mapper.
            /// </summary>
            /// <typeparam name="TMapper">The mapper type.</typeparam>
            /// <param name="mapper">The mapper instance to use.</param>
            internal void MapChangedArguments<TMapper>(TMapper mapper)
                where TMapper : BranchTarget.IArgumentMapper =>
                targetBuilders.MapArguments(mapper);

            /// <summary>
            /// Setups the internal branch builder.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private ref BranchTarget.Builders SetupBranchBuilder()
            {
                if (Terminator is Branch branch && currentBranch != branch)
                {
                    currentBranch = branch;
                    targetBuilders.Register(branch);
                }

                return ref targetBuilders;
            }

            /// <summary>
            /// Setups the current sequence point of this basic block and
            /// sets the current sequence point of the parent method builder to
            /// the given point.
            /// </summary>
            /// <param name="sequencePoint">The sequence point to setup.</param>
            public void SetupSequencePoint(SequencePoint sequencePoint)
            {
                BasicBlock.SequencePoint = sequencePoint;
                MethodBuilder.SequencePoint = sequencePoint;
            }

            /// <summary>
            /// Sets the insert position to the index stored in the given value entry.
            /// </summary>
            /// <param name="valueEntry">The value entry.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetupInsertPosition(in ValueEntry valueEntry)
            {
                InsertPosition = valueEntry.Index + 1;
            }

            /// <summary>
            /// Sets the insert position to the index stored in the given value entry.
            /// </summary>
            /// <param name="value">The value entry.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetupInsertPosition(in Value value)
            {
                Debug.Assert(value.BasicBlock == BasicBlock, "Invalid block association");
                InsertPosition = Values.IndexOf(value);
            }

            /// <summary>
            /// Inserts the given value at the beginning of this block.
            /// </summary>
            /// <param name="value">The value to add.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void InsertAtBeginning(Value value)
            {
                Debug.Assert(value != null, "Invalid value");
                Debug.Assert(value.BasicBlock == BasicBlock, "Invalid block association");

                Values.Insert(0, value);
                ++InsertPosition;
            }

            /// <summary>
            /// Adds the given value to this block.
            /// </summary>
            /// <param name="value">The value to add.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add(Value value)
            {
                Debug.Assert(value != null, "Invalid value");
                Debug.Assert(value.BasicBlock == BasicBlock, "Invalid block association");
                Debug.Assert(!(value is Parameter), "Invalid parameter to add");

                if (insertPosition < Count)
                    Values.Insert(insertPosition, value);
                else
                    Values.Add(value);
                ++insertPosition;
            }

            /// <summary>
            /// Clears all attached values (except the terminator).
            /// </summary>
            public void Clear()
            {
                Values.Clear();
                toRemove.Clear();
            }

            /// <summary>
            /// Schedules the given value for removal.
            /// </summary>
            /// <param name="value">The value to remove.</param>
            public void Remove(Value value)
            {
                Debug.Assert(value != null, "Invalid value");
                Debug.Assert(value.BasicBlock == BasicBlock, "Invalid block association");

                toRemove.Add(value);
            }

            /// <summary>
            /// Applies all scheduled removal operations.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void PerformRemoval()
            {
                if (toRemove.Count < 1)
                    return;
                var newValues = new List<ValueReference>(
                    IntrinsicMath.Max(Count - toRemove.Count, 0));
                PerformRemoval(newValues);
                Values = newValues;
            }

            /// <summary>
            /// Applies all scheduled removal operations by adding them to
            /// the given <paramref name="targetCollection"/>.
            /// </summary>
            /// <param name="targetCollection">
            /// The target collection to which all elements will be appended.
            /// </param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void PerformRemoval<TCollection>(TCollection targetCollection)
                where TCollection : ICollection<ValueReference>
            {
                for (int i = 0, e = Count; i < e; ++i)
                {
                    var valueRef = values[i];
                    if (toRemove.Contains(valueRef.DirectTarget))
                        continue;
                    targetCollection.Add(valueRef);
                }

                toRemove.Clear();
            }

            /// <summary>
            /// Specializes a function call.
            /// </summary>
            /// <typeparam name="TScopeProvider">The provider to resolve methods to scopes.</typeparam>
            /// <param name="call">The call to specialize.</param>
            /// <param name="scopeProvider">Resolves methods to scopes.</param>
            /// <returns>The created target block.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Builder SpecializeCall<TScopeProvider>(MethodCall call, TScopeProvider scopeProvider)
                where TScopeProvider : IScopeProvider
            {
                Debug.Assert(call != null, "Invalid call");

                var scope = scopeProvider[call.Target];
                return SpecializeCall(call, scope);
            }

            /// <summary>
            /// Specializes a function call.
            /// </summary>
            /// <param name="call">The call to specialize.</param>
            /// <param name="scope">The call target's scope.</param>
            /// <returns>The created target block.</returns>
            public Builder SpecializeCall(MethodCall call, Scope scope)
            {
                Debug.Assert(call != null, "Invalid call");
                Debug.Assert(scope != null, "Invalid scope");
                Debug.Assert(scope.Method == call.Target, "Incompatible scope");

                // Perform local rebuilding step
                var callTarget = call.Target;
                var tempBlock = SplitBlock(call, false);
                var mapping = scope.Method.CreateParameterMapping(call.Nodes);
                var rebuilder = MethodBuilder.CreateRebuilder(mapping, scope);
                var exitBlocks = rebuilder.Rebuild();

                // Wire current block with new entry block
                CreateUnconditionalBranch(rebuilder.EntryBlock.BasicBlock);

                // Wire exit blocks with temp block
                foreach (var (block, _) in exitBlocks)
                    block.CreateUnconditionalBranch(tempBlock.BasicBlock);

                // Replace call with the appropriate return value
                if (!callTarget.IsVoid)
                {
                    if (exitBlocks.Length < 2)
                    {
                        // Replace with single return value
                        call.Replace(exitBlocks[0].Item2);
                    }
                    else
                    {
                        // We require a custom block parameter attached to the temp block
                        var blockParameter = tempBlock.Parameters.AddParameter(
                            callTarget.ReturnType);
                        foreach (var (returnBuilder, returnValue) in exitBlocks)
                            returnBuilder.AddArgument(tempBlock.BasicBlock, returnValue);
                        call.Replace(blockParameter);
                    }
                }
                else
                {
                    // Replace call with an empty null value
                    call.Replace(CreateNull(VoidType));
                }

                // Unlink call node from the current block
                Remove(call);

                return tempBlock;
            }

            /// <summary>
            /// Splits the current block at the given value.
            /// </summary>
            /// <param name="splitPoint">The split point.</param>
            /// <param name="keepSplitPoint">True, if you want to keep the split point.</param>
            /// <returns>The created temporary block.</returns>
            public Builder SplitBlock(Value splitPoint, bool keepSplitPoint)
            {
                PerformRemoval();

                Debug.Assert(splitPoint != null, "Invalid split point");

                // Create a new basic block to jump to
                var valueIndex = Values.IndexOf(splitPoint);
                var splitPointOffset = keepSplitPoint ? 0 : 1;
                Debug.Assert(valueIndex >= 0, "Invalid split point");

                // Create temp block and move instructions
                var tempBlock = MethodBuilder.CreateBasicBlock(BasicBlock.Name + "'");
                for (int i = valueIndex + splitPointOffset, e = Count; i < e; ++i)
                {
                    Value valueToMove = Values[i];
                    valueToMove.BasicBlock = tempBlock.BasicBlock;
                    tempBlock.Add(valueToMove);
                }
                while (Count > valueIndex)
                    Values.RemoveAt(Count - 1);

                // Adjust terminators
                tempBlock.Terminator = Terminator;
                Terminator = null;
                CreateUnconditionalBranch(tempBlock.BasicBlock);

                return tempBlock;
            }

            /// <summary>
            /// Merges the given block into the current one while merging all parameters.
            /// </summary>
            /// <param name="other">The other block to merge.</param>
            public void MergeBlock(BasicBlock other) => MergeBlock(other, true);

            /// <summary>
            /// Merges the given block into the current one.
            /// </summary>
            /// <param name="other">The other block to merge.</param>
            /// <param name="mergeParameters">
            /// True, if the parameters of the other block should be merged with the current parameters.
            /// </param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void MergeBlock(BasicBlock other, bool mergeParameters)
            {
                Debug.Assert(other != null, "Invalid other block");
                Debug.Assert(other != BasicBlock, "Invalid block association");

                var otherBuilder = MethodBuilder[other];

                int offset = Count;
                otherBuilder.PerformRemoval(values);

                // Attach parameters
                if (mergeParameters)
                {
                    Parameters.Add(other.Parameters);

                    // TODO: merge block argument values
                    // CAUTION: might have to be merged to their predecessor values
                }

                // Attach values to another block
                for (; offset < Count; ++offset)
                {
                    Value movedValue = values[offset];
                    movedValue.BasicBlock = BasicBlock;
                }
                otherBuilder.Clear();
                insertPosition = Count;

                // Wire terminators
                Terminator = other.Terminator;
            }

            /// <summary>
            /// Replaces the given value with a call to the provided function.
            /// </summary>
            /// <param name="value">The value to replace.</param>
            /// <param name="implementationMethod">The target implementation method.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ReplaceWithCall(Value value, Method implementationMethod)
            {
                Debug.Assert(value != null, "Invalid value");
                Debug.Assert(value.BasicBlock == BasicBlock, "Invalid value method");
                Debug.Assert(implementationMethod != null, "Invalid method");
                Debug.Assert(
                    BasicBlock.Method != implementationMethod,
                    "Cannot introduce recursive methods");

                var call = CreateCall(implementationMethod, value.Nodes);
                value.Replace(call);
                Remove(call);
            }

            /// <summary cref="IRBuilder.CreateTerminator(TerminatorValue)"/>
            protected override TerminatorValue CreateTerminator(TerminatorValue node)
            {
                MethodBuilder.Create(node);
                Terminator?.Replace(node);
                Terminator = node;

                SetupBranchBuilder();
                return node;
            }

            /// <summary cref="IRBuilder.OnCreateBranchTarget(BranchTarget.Builder)"/>
            protected override void OnCreateBranchTarget(BranchTarget.Builder builder) =>
                SetupBranchBuilder().Register(builder);

            /// <summary cref="IRBuilder.Append{T}(T)"/>
            protected override T Append<T>(T node)
            {
                MethodBuilder.Create(node);
                // Insert node into current basic block builder
                Add(node);
                return node;
            }

            /// <summary>
            /// Implicitly converts the current builder into its associated basic block.
            /// </summary>
            public BasicBlock ToBasicBlock() => BasicBlock;

            #endregion

            #region Operators

            /// <summary>
            /// Implicitly converts the given builder into its associated basic block.
            /// </summary>
            /// <param name="builder">The builder to convert.</param>
            public static implicit operator BasicBlock(Builder builder)
            {
                Debug.Assert(builder != null, "Invalid block builder");
                return builder.BasicBlock;
            }

            #endregion

            #region IEnumerable

            /// <summary>
            /// Returns a value enumerator.
            /// </summary>
            /// <returns>The resolved enumerator.</returns>
            public Enumerator GetEnumerator() => new Enumerator(BasicBlock);

            /// <summary cref="IEnumerable{T}.GetEnumerator"/>
            IEnumerator<ValueEntry> IEnumerable<ValueEntry>.GetEnumerator() => GetEnumerator();

            /// <summary cref="IEnumerable.GetEnumerator"/>
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            #endregion

            #region IDisposable

            /// <summary cref="DisposeBase.Dispose(bool)"/>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    // Dispose the parameter builder
                    Parameters.Dispose();

                    // Seal all branch builders
                    targetBuilders.Seal();

                    // Perform removal operations and release the builder
                    PerformRemoval();
                    BasicBlock.ReleaseBuilder(this);
                }
                base.Dispose(disposing);
            }

            #endregion
        }
    }
}
