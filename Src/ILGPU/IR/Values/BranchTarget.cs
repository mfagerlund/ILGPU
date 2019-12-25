// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2019 Marcel Koester
//                                www.ilgpu.net
//
// File: BanchTarget.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details
// -----------------------------------------------------------------------------

using ILGPU.IR.Construction;
using ILGPU.IR.Types;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace ILGPU.IR.Values
{
    /// <summary>
    /// Represents a single branch target that links values and block arguments
    /// with a specific basic block to jump to.
    /// </summary>
    public sealed class BranchTarget : Value
    {
        #region Nested Types

        /// <summary>
        /// Specifies an abstract mapper that determines which block arguments to keep
        /// and which arguments to drop.
        /// </summary>
        public interface IArgumentMapper
        {
            /// <summary>
            /// Returns true if the specified argument can be dropped.
            /// This can happen when a block parameter has been replaced and does not
            /// need specific arguments any more.
            /// </summary>
            /// <param name="target">The current target.</param>
            /// <param name="argumentIndex">The current argument index.</param>
            /// <returns>True, if the specified argument can be dropped.</returns>
            bool CanMapBlockArgument(BranchTarget target, int argumentIndex);
        }

        /// <summary>
        /// A builder for branch targets.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1710: IdentifiersShouldHaveCorrectSuffix",
            Justification = "This is the correct name of the current entity")]
        public sealed class Builder : IReadOnlyCollection<ValueReference>
        {
            #region Instance

            /// <summary>
            /// The current builder storing all arguments.
            /// </summary>
            private ImmutableArray<ValueReference>.Builder arguments;

            /// <summary>
            /// Constructs a new branch builder.
            /// </summary>
            /// <param name="branchTarget">The current branch target.</param>
            internal Builder(BranchTarget branchTarget)
            {
                Debug.Assert(branchTarget != null, "Invalid target");
                Target = branchTarget;

                arguments = branchTarget.Nodes.ToBuilder();
            }

            #endregion

            #region Properties

            /// <summary>
            /// Returns the associated basic block.
            /// </summary>
            public BasicBlock BasicBlock => Target.BasicBlock;

            /// <summary>
            /// Returns the associated branch target block.
            /// </summary>
            public BasicBlock TargetBlock => Target.TargetBlock;

            /// <summary>
            /// Returns the associated branch target.
            /// </summary>
            public BranchTarget Target { get; }

            /// <summary>
            /// Returns the number of attached arguments.
            /// </summary>
            public int Count => arguments.Count;

            /// <summary>
            /// Returns the i-th argument.
            /// </summary>
            /// <param name="index">The argument index.</param>
            /// <returns>The resolved argument.</returns>
            public ValueReference this[int index] => arguments[index];

            /// <summary>
            /// Returns true if this builder has been sealed.
            /// </summary>
            public bool IsSealed { get; private set; }

            #endregion

            #region Methods

            /// <summary>
            /// Converts all block arguments to an immutable array.
            /// </summary>
            /// <returns>The created immutable array storing all block arguments.</returns>
            public ImmutableArray<ValueReference> ToImmutable() => arguments.ToImmutable();

            /// <summary>
            /// Adds the given argument.
            /// </summary>
            /// <param name="value">The argument value to add.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddArgument(Value value)
            {
                Debug.Assert(value != null, "Invalid block argument");
                arguments.Add(value);
            }

            /// <summary>
            /// Adds the given arguments.
            /// </summary>
            /// <param name="values">The argument values to add.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddArguments(ImmutableArray<ValueReference> values) =>
                arguments.AddRange(values);

            /// <summary>
            /// Maps all internal arguments using the given mapper.
            /// </summary>
            /// <typeparam name="TMapper">The mapper type.</typeparam>
            /// <param name="mapper">The mapper instance to use.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void MapArguments<TMapper>(TMapper mapper)
                where TMapper : IArgumentMapper
            {
                // Compute all block arguments that have to removed. This can be required since the
                // associated target parameter(s) could have been replaced by another value.
                if (Count < 1)
                    return;

                var newArguments = ImmutableArray.CreateBuilder<ValueReference>(Count);
                for (int i = 0, e = Count; i < e; ++i)
                {
                    if (!mapper.CanMapBlockArgument(Target, i))
                        continue;
                    newArguments.Add(this[i]);
                }
                arguments = newArguments;
            }

            /// <summary>
            /// Seals this branch target.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public BranchTarget Seal()
            {
                if (!IsSealed)
                {
                    Target.Seal(ToImmutable());
                    IsSealed = true;
                }
                return Target;
            }

            /// <summary>
            /// Rebuilds the associated branch target.
            /// </summary>
            /// <param name="source">
            /// The source branch target from which the arguments have to be copied.
            /// </param>
            /// <param name="rebuilder">The rebuilder to use.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal BranchTarget Rebuild(BranchTarget source, IRRebuilder rebuilder)
            {
                if (IsSealed)
                    return Target;

                // Register all arguments
                for (int i = 0, e = source.Nodes.Length; i < e; ++i)
                {
                    if (!rebuilder.CanMapBlockArgument(source, Target, i))
                        continue;
                    Value argumentValue = source[i];
                    AddArgument(rebuilder.Rebuild(argumentValue));
                }

                return Seal();
            }

            #endregion

            #region IEnumerable

            /// <summary cref="IEnumerable{T}.GetEnumerator"/>
            public IEnumerator<ValueReference> GetEnumerator() => arguments.GetEnumerator();

            /// <summary cref="IEnumerable.GetEnumerator"/>
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            #endregion
        }

        /// <summary>
        /// Encapsulates a lazily constructed mapping of <see cref="Builder"/> instances.
        /// </summary>
        public struct Builders
        {
            #region Instance

            private Dictionary<BasicBlock, Builder> builders;

            /// <summary>
            /// Constructs a new builders instance.
            /// </summary>
            /// <param name="builder">The parent IR builder to use.</param>
            public Builders(IRBuilder builder)
            {
                builders = default;
                Builder = builder;
            }

            #endregion

            #region Properties

            /// <summary>
            /// Returns the parent IR builder.
            /// </summary>
            public IRBuilder Builder { get; }

            /// <summary>
            /// Returns the current IR builder.
            /// </summary>
            public Builder this[BasicBlock target] => builders[target];

            #endregion

            #region Methods

            /// <summary>
            /// Initializes the internal builders.
            /// </summary>
            private void Init()
            {
                if (builders != null)
                    return;

                builders = new Dictionary<BasicBlock, Builder>(capacity: 2);
            }

            /// <summary>
            /// Registers the given branch by replacing all branch targets with
            /// new ones and storing the created builders.
            /// </summary>
            /// <param name="branch">The source branch (if any).</param>
            public void Register(Branch branch)
            {
                Init();
                foreach (BranchTarget target in branch.Targets)
                {
                    if (builders.TryGetValue(target.TargetBlock, out var otherBuilder))
                    {
                        if (otherBuilder.Target != target)
                        {
                            otherBuilder.Seal();
                            builders[target.TargetBlock] = target.ToBuilder(Builder);
                        }
                    }
                    else
                        builders[target.TargetBlock] = target.ToBuilder(Builder);
                }
            }

            /// <summary>
            /// Registers the given branch builder.
            /// </summary>
            /// <param name="builder">The builder to register.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Register(Builder builder)
            {
                Debug.Assert(builder != null, "Invalid builder");

                Init();

                if (builders.TryGetValue(builder.TargetBlock, out var otherBuilder) &&
                    otherBuilder != builder)
                    otherBuilder.Seal();
                builders[builder.TargetBlock] = builder;
            }

            /// <summary>
            /// Adds the given argument.
            /// </summary>
            /// <param name="target">The target block to add the argument to.</param>
            /// <param name="value">The argument value to add.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddArgument(BasicBlock target, Value value)
            {
                Debug.Assert(target != null, "Invalid target");

                this[target].AddArgument(value);
            }

            /// <summary>
            /// Maps all internal arguments using the given mapper.
            /// </summary>
            /// <typeparam name="TMapper">The mapper type.</typeparam>
            /// <param name="mapper">The mapper instance to use.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void MapArguments<TMapper>(TMapper mapper)
                where TMapper : IArgumentMapper
            {
                if (builders == null)
                    return;

                foreach (var builder in builders.Values)
                    builder.MapArguments(mapper);
            }

            /// <summary>
            /// Seals all branch targets.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Seal()
            {
                if (builders == null)
                    return;

                foreach (var builder in builders.Values)
                    builder.Seal();
            }

            #endregion
        }

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new branch target value.
        /// </summary>
        /// <param name="basicBlock">The parent basic block.</param>
        /// <param name="target">The target basic block to jump to.</param>
        /// <param name="voidType">The void type.</param>
        internal BranchTarget(
            BasicBlock basicBlock,
            BasicBlock target,
            VoidType voidType)
            : base(
                  ValueKind.BranchTarget,
                  basicBlock,
                  voidType)
        {
            Debug.Assert(target != null, "Invalid target");
            TargetBlock = target;
        }

        #endregion

        #region Properties

        /// <summary>
        /// The actual target block.
        /// </summary>
        public BasicBlock TargetBlock { get; }

        #endregion

        #region Methods

        /// <summary cref="Value.UpdateType(IRContext)"/>
        protected override TypeNode UpdateType(IRContext context) => context.VoidType;

        /// <summary cref="Value.Rebuild(IRBuilder, IRRebuilder)"/>
        protected internal override Value Rebuild(IRBuilder builder, IRRebuilder rebuilder) =>
            builder.CreateBranchTarget(
                rebuilder.LookupTarget(TargetBlock)).Rebuild(this, rebuilder);

        /// <summary cref="Value.Accept"/>
        public override void Accept<T>(T visitor) => visitor.Visit(this);

        /// <summary>
        /// Creates a new branch builder from this node and replaces this node with
        /// the newly created branch target.
        /// </summary>
        /// <param name="builder">The parent IR builder to use.</param>
        /// <returns>The created new branch builder.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Builder ToBuilder(IRBuilder builder)
        {
            var targetBuilder = builder.CreateBranchTarget(TargetBlock);
            targetBuilder.AddArguments(Nodes);
            Replace(targetBuilder.Target);
            return targetBuilder;
        }

        /// <summary>
        /// Converts this branch target into its associated target block.
        /// </summary>
        /// <returns>The target block.</returns>
        public BasicBlock ToBasicBlock() => TargetBlock;

        #endregion

        #region Object

        /// <summary cref="Node.ToPrefixString"/>
        protected override string ToPrefixString() => TargetBlock.ToReferenceString();

        /// <summary cref="Value.ToArgString"/>
        protected override string ToArgString()
        {
            if (Nodes.Length < 1)
                return "()";

            var result = new StringBuilder();
            result.Append('(');
            for (int i = 0, e = Nodes.Length; i < e; ++i)
            {
                result.Append(this[i].ToString());
                if (i + 1 < e)
                    result.Append(", ");
            }
            result.Append(')');
            return result.ToString();
        }

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given branch target implicitly into its associated target block.
        /// </summary>
        /// <param name="branchTarget">The branch target to convert.</param>
        public static implicit operator BasicBlock(BranchTarget branchTarget) =>
            branchTarget.ToBasicBlock();

        #endregion
    }
}
