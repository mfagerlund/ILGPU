// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2019 Marcel Koester
//                                www.ilgpu.net
//
// File: Terminators.cs
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
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace ILGPU.IR.Values
{
    /// <summary>
    /// Represents a single terminator value.
    /// </summary>
    public abstract class TerminatorValue : Value
    {
        #region Nested Types

        /// <summary>
        /// A type safe collection of branch targets.
        /// </summary>
        public readonly struct TargetCollection : IReadOnlyList<BranchTarget>
        {
            #region Nested Types

            /// <summary>
            /// An enumerator to iterate over all branch targets.
            /// </summary>
            public struct Enumerator : IEnumerator<BranchTarget>
            {
                #region Instance

                private ImmutableArray<ValueReference>.Enumerator enumerator;

                /// <summary>
                /// Constructs a new enumerator.
                /// </summary>
                /// <param name="collection">The parent collection.</param>
                internal Enumerator(in TargetCollection collection)
                {
                    enumerator = collection.RawTargets.GetEnumerator();
                }

                #endregion

                #region Properties

                /// <summary>
                /// Returns the current branch target.
                /// </summary>
                public BranchTarget Current => enumerator.Current.ResolveAs<BranchTarget>();

                /// <summary cref="IEnumerator.Current"/>
                object IEnumerator.Current => Current;

                #endregion

                #region Methods

                /// <summary cref="IDisposable.Dispose"/>
                public void Dispose() { }

                /// <summary cref="IEnumerator.MoveNext"/>
                public bool MoveNext() => enumerator.MoveNext();

                /// <summary cref="IEnumerator.Reset"/>
                void IEnumerator.Reset() => throw new InvalidOperationException();

                #endregion
            }

            #endregion

            #region Instance

            /// <summary>
            /// Constructs a new target collection.
            /// </summary>
            /// <param name="rawTargets">The underlying raw target references.</param>
            internal TargetCollection(ImmutableArray<ValueReference> rawTargets)
            {
                RawTargets = rawTargets;
            }

            #endregion

            #region Properties

            /// <summary>
            /// Returns all underlying raw target references.
            /// </summary>
            public ImmutableArray<ValueReference> RawTargets { get; }

            /// <summary>
            /// Returns the number of branch targets.
            /// </summary>
            public int Count => RawTargets.Length;

            /// <summary>
            /// Returns the i-th branch target.
            /// </summary>
            /// <param name="index">The index of the branch target.</param>
            /// <returns>The i-th branch target.</returns>
            public BranchTarget this[int index] => RawTargets[index].ResolveAs<BranchTarget>();

            #endregion

            #region Methods

            /// <summary>
            /// Converts this target collection into an unsafe array of value references.
            /// </summary>
            /// <returns>An array containing value references to all targets.</returns>
            public ImmutableArray<ValueReference> ToImmutableArray() => RawTargets;

            #endregion

            #region IEnumerable

            /// <summary>
            /// Returns an enumerator to enumerate all branch targets.
            /// </summary>
            /// <returns>An enumerator to enumerate all branch targets.</returns>
            public Enumerator GetEnumerator() => new Enumerator(this);

            /// <summary cref="IEnumerable{T}.GetEnumerator()"/>
            IEnumerator<BranchTarget> IEnumerable<BranchTarget>.GetEnumerator() => GetEnumerator();

            /// <summary cref="IEnumerable.GetEnumerator()"/>
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            #endregion

            #region Operators

            /// <summary>
            /// Converts the given target collection into an unsafe array of value references.
            /// </summary>
            /// <param name="collection">The collection to convert.</param>
            /// <returns>An array containing value references to all targets.</returns>
            public static implicit operator ImmutableArray<ValueReference>(
                TargetCollection collection) => collection.ToImmutableArray();

            #endregion
        }

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new terminator value that is marked.
        /// </summary>
        /// <param name="kind">The value kind.</param>
        /// <param name="basicBlock">The parent basic block.</param>
        /// <param name="targets">The associated targets.</param>
        /// <param name="initialType">The initial node type.</param>
        protected TerminatorValue(
            ValueKind kind,
            BasicBlock basicBlock,
            ImmutableArray<ValueReference> targets,
            TypeNode initialType)
            : base(kind, basicBlock, initialType)
        {
            RawTargets = targets;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated targets.
        /// </summary>
        public ImmutableArray<ValueReference> RawTargets { get; }

        /// <summary>
        /// Returns a collection of all branch targets.
        /// </summary>
        public TargetCollection Targets => new TargetCollection(RawTargets);

        /// <summary>
        /// Returns a collection of all target blocks.
        /// </summary>
        public BasicBlock.SuccessorCollection TargetBlocks =>
            new BasicBlock.SuccessorCollection(Targets);

        /// <summary>
        /// Returns the number of attached targets.
        /// </summary>
        public int NumTargets => RawTargets.Length;

        /// <summary>
        /// The internal branch arguments that directly influence the behavior
        /// of the branch-terminator semantics.
        /// </summary>
        public ImmutableArray<ValueReference> Arguments { get; private set; }

        /// <summary>
        /// Returns the number of attached arguments.
        /// </summary>
        public int NumArguments => Arguments.Length;

        #endregion

        #region Methods

        /// <summary>
        /// Returns an immutable array of target blocks.
        /// </summary>
        /// <returns>The computed immutable array of target blocks.</returns>
        public ImmutableArray<BasicBlock> GetTargetBlocks()
        {
            var result = ImmutableArray.CreateBuilder<BasicBlock>(NumTargets);
            foreach (var target in Targets)
                result.Add(target.TargetBlock);
            return result.MoveToImmutable();
        }

        /// <summary>
        /// Seals the given block arguments.
        /// </summary>
        /// <param name="arguments">The block arguments.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected new void Seal(ImmutableArray<ValueReference> arguments)
        {
            Arguments = arguments;
            base.Seal(RawTargets.AddRange(arguments));
        }

        #endregion
    }

    /// <summary>
    /// Represents a simple return terminator.
    /// </summary>
    public sealed class ReturnTerminator : TerminatorValue
    {
        #region Static

        /// <summary>
        /// Computes a return node type.
        /// </summary>
        /// <param name="returnType">The return type.</param>
        /// <returns>The resolved type node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TypeNode ComputeType(TypeNode returnType) =>
            returnType;

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new return terminator.
        /// </summary>
        /// <param name="basicBlock">The parent basic block.</param>
        /// <param name="returnValue">The current return value.</param>
        internal ReturnTerminator(
            BasicBlock basicBlock,
            ValueReference returnValue)
            : base(
                  ValueKind.Return,
                  basicBlock,
                  ImmutableArray<ValueReference>.Empty,
                  ComputeType(returnValue.Type))
        {
            Seal(ImmutableArray.Create(returnValue));
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns true if the current return terminator is a void return.
        /// </summary>
        public bool IsVoidReturn => Type.IsVoidType;

        /// <summary>
        /// Returns the associated return value.
        /// In case of a void return value the result is a <see cref="NullValue"/>.
        /// </summary>
        public ValueReference ReturnValue => this[0];

        #endregion

        #region Methods

        /// <summary cref="Value.UpdateType(IRContext)"/>
        protected override TypeNode UpdateType(IRContext context) =>
            ComputeType(ReturnValue.Type);

        /// <summary cref="Value.Rebuild(IRBuilder, IRRebuilder)"/>
        protected internal override Value Rebuild(IRBuilder builder, IRRebuilder rebuilder) =>
            builder.CreateReturn(
                rebuilder.Rebuild(ReturnValue));

        /// <summary cref="Value.Accept"/>
        public override void Accept<T>(T visitor) => visitor.Visit(this);

        #endregion

        #region Object

        /// <summary cref="Node.ToPrefixString"/>
        protected override string ToPrefixString() => "ret";

        /// <summary cref="Value.ToArgString"/>
        protected override string ToArgString() => ReturnValue.ToString();

        #endregion
    }

    /// <summary>
    /// Represents a branch-based terminator.
    /// </summary>
    public abstract class Branch : TerminatorValue
    {
        #region Static

        /// <summary>
        /// Computes a branch node type.
        /// </summary>
        /// <param name="context">The parent IR context.</param>
        /// <returns>The resolved type node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TypeNode ComputeType(IRContext context) =>
            context.VoidType;

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new branch terminator.
        /// </summary>
        /// <param name="kind">The value kind.</param>
        /// <param name="context">The parent IR context.</param>
        /// <param name="basicBlock">The parent basic block.</param>
        /// <param name="targets">The jump targets.</param>
        /// <param name="branchArguments">The branch arguments.</param>
        internal Branch(
            ValueKind kind,
            IRContext context,
            BasicBlock basicBlock,
            ImmutableArray<ValueReference> targets,
            ImmutableArray<ValueReference> branchArguments)
            : base(
                  kind,
                  basicBlock,
                  targets,
                  ComputeType(context))
        {
            Seal(branchArguments);
        }

        #endregion

        #region Methods

        /// <summary cref="Value.UpdateType(IRContext)"/>
        protected override TypeNode UpdateType(IRContext context) =>
            ComputeType(context);

        #endregion
    }

    /// <summary>
    /// Represents an unconditional branch terminator.
    /// </summary>
    public sealed class UnconditionalBranch : Branch
    {
        #region Instance

        /// <summary>
        /// Constructs a new branch terminator.
        /// </summary>
        /// <param name="context">The parent IR context.</param>
        /// <param name="basicBlock">The parent basic block.</param>
        /// <param name="target">The jump target.</param>
        internal UnconditionalBranch(
            IRContext context,
            BasicBlock basicBlock,
            BranchTarget target)
            : base(
                  ValueKind.UnconditionalBranch,
                  context,
                  basicBlock,
                  ImmutableArray.Create<ValueReference>(target),
                  ImmutableArray<ValueReference>.Empty)
        { }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the unconditional jump target.
        /// </summary>
        public BranchTarget Target => Targets[0];

        #endregion

        #region Methods

        /// <summary cref="Value.Rebuild(IRBuilder, IRRebuilder)"/>
        protected internal override Value Rebuild(IRBuilder builder, IRRebuilder rebuilder) =>
            builder.CreateUnconditionalBranch(rebuilder.RebuildAs<BranchTarget>(Target));

        /// <summary cref="Value.Accept"/>
        public override void Accept<T>(T visitor) => visitor.Visit(this);

        #endregion

        #region Object

        /// <summary cref="Node.ToPrefixString"/>
        protected override string ToPrefixString() => "branch";

        /// <summary cref="Value.ToArgString"/>
        protected override string ToArgString() => Target.ToString();

        #endregion
    }

    /// <summary>
    /// Represents a conditional branch terminator.
    /// </summary>
    public sealed class ConditionalBranch : Branch
    {
        #region Instance

        /// <summary>
        /// Constructs a new conditional branch terminator.
        /// </summary>
        /// <param name="context">The parent IR context.</param>
        /// <param name="basicBlock">The parent basic block.</param>
        /// <param name="condition">The jump condition.</param>
        /// <param name="falseTarget">The false jump target.</param>
        /// <param name="trueTarget">The true jump target.</param>
        internal ConditionalBranch(
            IRContext context,
            BasicBlock basicBlock,
            ValueReference condition,
            BranchTarget trueTarget,
            BranchTarget falseTarget)
            : base(
                  ValueKind.ConditionalBranch,
                  context,
                  basicBlock,
                  ImmutableArray.Create<ValueReference>(trueTarget, falseTarget),
                  ImmutableArray.Create(condition))
        {
            Debug.Assert(
                condition.Type.IsPrimitiveType &&
                condition.Type.BasicValueType == BasicValueType.Int1,
                "Invalid boolean predicate");
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated branch condition.
        /// </summary>
        public ValueReference Condition => this[0];

        /// <summary>
        /// Returns the true jump target.
        /// </summary>
        public BranchTarget TrueTarget => Targets[0];

        /// <summary>
        /// Returns the false jump target.
        /// </summary>
        public BranchTarget FalseTarget => Targets[1];

        #endregion

        #region Methods

        /// <summary cref="Value.Rebuild(IRBuilder, IRRebuilder)"/>
        protected internal override Value Rebuild(IRBuilder builder, IRRebuilder rebuilder) =>
            builder.CreateConditionalBranch(
                rebuilder.Rebuild(Condition),
                rebuilder.RebuildAs<BranchTarget>(TrueTarget),
                rebuilder.RebuildAs<BranchTarget>(FalseTarget));

        /// <summary cref="Value.Accept"/>
        public override void Accept<T>(T visitor) => visitor.Visit(this);

        #endregion

        #region Object

        /// <summary cref="Node.ToPrefixString"/>
        protected override string ToPrefixString() => "branch";

        /// <summary cref="Value.ToArgString"/>
        protected override string ToArgString() =>
            $"{Condition} ? {TrueTarget.ToReferenceString()} : {FalseTarget.ToReferenceString()}";

        #endregion
    }

    /// <summary>
    /// Represents a single switch terminator.
    /// </summary>
    public sealed class SwitchBranch : Branch
    {
        #region Instance

        /// <summary>
        /// Constructs a new switch terminator.
        /// </summary>
        /// <param name="context">The parent IR context.</param>
        /// <param name="basicBlock">The parent basic block.</param>
        /// <param name="value">The value to switch over.</param>
        /// <param name="targets">The jump targets.</param>
        internal SwitchBranch(
            IRContext context,
            BasicBlock basicBlock,
            ValueReference value,
            ImmutableArray<ValueReference> targets)
            : base(
                  ValueKind.SwitchBranch,
                  context,
                  basicBlock,
                  targets,
                  ImmutableArray.Create(value))
        {
            Debug.Assert(
                value.Type.IsPrimitiveType &&
                value.Type.BasicValueType.IsInt(),
                "Invalid integer selection value");
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated predicate value.
        /// </summary>
        public ValueReference Condition => this[0];

        /// <summary>
        /// Returns the default block.
        /// </summary>
        public BranchTarget DefaultBlock => Targets[0];

        /// <summary>
        /// Returns the number of actual switch cases without the default case.
        /// </summary>
        public int NumCasesWithoutDefault => Targets.Count - 1;

        #endregion

        #region Methods

        /// <summary>
        /// Returns the case target for the i-th case.
        /// </summary>
        /// <param name="i">The index of the i-th case.</param>
        /// <returns>The resulting jump target.</returns>
        [SuppressMessage("Microsoft.Usage", "CA2233: OperationsShouldNotOverflow",
            Justification = "Exception checks avoided for performance reasons")]
        public BranchTarget GetCaseTarget(int i)
        {
            Debug.Assert(i < Targets.Count - 1, "Invalid case argument");
            return Targets[i + 1];
        }

        /// <summary cref="Value.Rebuild(IRBuilder, IRRebuilder)"/>
        protected internal override Value Rebuild(IRBuilder builder, IRRebuilder rebuilder)
        {
            var targets = ImmutableArray.CreateBuilder<ValueReference>(Targets.Count);
            foreach (var target in Targets)
                targets.Add(rebuilder.Rebuild(target));

            return builder.CreateSwitchBranch(
                rebuilder.Rebuild(Condition),
                targets.MoveToImmutable());
        }

        /// <summary cref="Value.Accept"/>
        public override void Accept<T>(T visitor) => visitor.Visit(this);

        #endregion

        #region Object

        /// <summary cref="Node.ToPrefixString"/>
        protected override string ToPrefixString() => "switch";

        /// <summary cref="Value.ToArgString"/>
        protected override string ToArgString()
        {
            var result = new StringBuilder();
            result.Append(Condition.ToString());
            result.Append(" ");
            for (int i = 1, e = Targets.Count; i < e; ++i)
            {
                result.Append(Targets[i].ToString());
                if (i + 1 < e)
                    result.Append(", ");
            }
            result.Append(" - default: ");
            result.Append(DefaultBlock.ToString());
            return result.ToString();
        }

        #endregion
    }

    /// <summary>
    /// Represents a temporary builder terminator.
    /// </summary>
    public sealed class BuilderTerminator : Branch
    {
        #region Instance

        /// <summary>
        /// Constructs a temporary builder terminator.
        /// </summary>
        /// <param name="context">The parent IR context.</param>
        /// <param name="basicBlock">The parent basic block.</param>
        /// <param name="targets">The jump targets.</param>
        internal BuilderTerminator(
            IRContext context,
            BasicBlock basicBlock,
            ImmutableArray<ValueReference> targets)
            : base(
                  ValueKind.BuilderTerminator,
                  context,
                  basicBlock,
                  targets,
                  ImmutableArray<ValueReference>.Empty)
        { }

        #endregion

        #region Methods

        /// <summary cref="Value.Rebuild(IRBuilder, IRRebuilder)"/>
        protected internal override Value Rebuild(IRBuilder builder, IRRebuilder rebuilder) =>
            throw new InvalidOperationException();

        /// <summary cref="Value.Accept"/>
        public override void Accept<T>(T visitor) =>
            throw new InvalidOperationException();

        #endregion

        #region Object

        /// <summary cref="Node.ToPrefixString"/>
        protected override string ToPrefixString() => "builderBr";

        /// <summary cref="Value.ToArgString"/>
        protected override string ToArgString()
        {
            var result = new StringBuilder();
            for (int i = 0, e = Targets.Count; i < e; ++i)
            {
                result.Append(Targets[i].ToReferenceString());
                if (i + 1 < e)
                    result.Append(", ");
            }
            return result.ToString();
        }

        #endregion
    }
}
