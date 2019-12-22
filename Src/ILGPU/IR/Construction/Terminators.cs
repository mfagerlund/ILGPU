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

using ILGPU.IR.Values;
using ILGPU.Util;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace ILGPU.IR.Construction
{
    partial class IRBuilder
    {
        /// <summary>
        /// Creates a new return terminator.
        /// </summary>
        /// <returns>The created terminator.</returns>
        public TerminatorValue CreateReturn() =>
            CreateReturn(
                CreateNull(Method.ReturnType));

        /// <summary>
        /// Creates a new return terminator.
        /// </summary>
        /// <param name="returnValue">The return value.</param>
        /// <returns>The created terminator.</returns>
        public TerminatorValue CreateReturn(Value returnValue)
        {
            Debug.Assert(returnValue != null, "Invalid return value");
            Debug.Assert(returnValue.Type == Method.ReturnType, "Incompatible return value");
            return CreateTerminator(new ReturnTerminator(
                BasicBlock,
                returnValue));
        }

        /// <summary>
        /// Creates a new branch target builder.
        /// </summary>
        /// <param name="targetBlock">The actual branch target block to jump to.</param>
        /// <returns>The created target builder.</returns>
        public BranchTarget.Builder CreateBranchTarget(BasicBlock targetBlock)
        {
            Debug.Assert(targetBlock != null, "Invalid basic block");
            var branchTarget = new BranchTarget(
                BasicBlock,
                targetBlock,
                VoidType);
            Context.Create(branchTarget);
            var builder = new BranchTarget.Builder(branchTarget);
            OnCreateBranchTarget(builder);
            return builder;
        }

        /// <summary>
        /// Creates a new unconditional branch.
        /// </summary>
        /// <param name="target">The target block.</param>
        /// <returns>The created terminator.</returns>
        public Branch CreateUnconditionalBranch(BasicBlock target)
        {
            var branchTarget = CreateBranchTarget(target);
            var branch = CreateUnconditionalBranch(branchTarget.Target);
            branchTarget.Seal();
            return branch;
        }

        /// <summary>
        /// Creates a new unconditional branch.
        /// </summary>
        /// <param name="target">The branch target.</param>
        /// <returns>The created terminator.</returns>
        public Branch CreateUnconditionalBranch(BranchTarget target)
        {
            Debug.Assert(target != null, "Invalid target");
            return CreateTerminator(new UnconditionalBranch(
                Context,
                BasicBlock,
                target)) as Branch;
        }

        /// <summary>
        /// Creates a new conditional branch.
        /// </summary>
        /// <param name="condition">The branch condition.</param>
        /// <param name="trueTarget">The true target block.</param>
        /// <param name="falseTarget">The false target block.</param>
        /// <returns>The created terminator.</returns>
        public Branch CreateConditionalBranch(
            Value condition,
            BasicBlock trueTarget,
            BasicBlock falseTarget)
        {
            var trueBranchTarget = CreateBranchTarget(trueTarget);
            var falseBranchTarget = CreateBranchTarget(falseTarget);
            var branch = CreateConditionalBranch(
                condition,
                trueBranchTarget.Target,
                falseBranchTarget.Target);
            trueBranchTarget.Seal();
            falseBranchTarget.Seal();
            return branch;
        }

        /// <summary>
        /// Creates a new conditional branch.
        /// </summary>
        /// <param name="condition">The branch condition.</param>
        /// <param name="trueTarget">The true branch target.</param>
        /// <param name="falseTarget">The false branch target.</param>
        /// <returns>The created terminator.</returns>
        public Branch CreateConditionalBranch(
            Value condition,
            BranchTarget trueTarget,
            BranchTarget falseTarget)
        {
            Debug.Assert(condition != null, "Invalid condition");
            Debug.Assert(trueTarget != null, "Invalid true target");
            Debug.Assert(falseTarget != null, "Invalid false target");

            return CreateTerminator(new ConditionalBranch(
                Context,
                BasicBlock,
                condition,
                trueTarget,
                falseTarget)) as Branch;
        }

        /// <summary>
        /// Creates a switch terminator.
        /// </summary>
        /// <param name="value">The selection value.</param>
        /// <param name="targets">All switch targets.</param>
        /// <returns>The created terminator.</returns>
        public Branch CreateSwitchBranch(
            Value value,
            ImmutableArray<ValueReference> targets)
        {
            Debug.Assert(value != null, "Invalid value node");
            Debug.Assert(value.BasicValueType.IsInt(), "Invalid value type");
            Debug.Assert(targets.Length > 0, "Invalid number of targets");

            value = CreateConvert(value, GetPrimitiveType(BasicValueType.Int32));

            // Transformation to create simple predicates
            if (targets.Length == 2)
            {
                return CreateConditionalBranch(
                    CreateCompare(value, CreatePrimitiveValue(0), CompareKind.Equal),
                    targets[0].ResolveAs<BranchTarget>(),
                    targets[1].ResolveAs<BranchTarget>());
            }

            return CreateTerminator(new SwitchBranch(
                Context,
                BasicBlock,
                value,
                targets)) as Branch;
        }

        /// <summary>
        /// Creates a temporary builder terminator.
        /// </summary>
        /// <param name="targetBlocks">All branch target blocks.</param>
        /// <returns>The created terminator.</returns>
        public Branch CreateBuilderTerminator<TCollection>(TCollection targetBlocks)
            where TCollection : IReadOnlyList<BasicBlock>
        {
            var targets = ImmutableArray.CreateBuilder<ValueReference>(targetBlocks.Count);
            for (int i = 0, e = targetBlocks.Count; i < e; ++i)
                targets.Add(CreateBranchTarget(targetBlocks[i]).Seal());
            return CreateBuilderTerminator(targets.MoveToImmutable());
        }

        /// <summary>
        /// Creates an empty temporary builder terminator.
        /// </summary>
        /// <returns>The created empty terminator.</returns>
        public Branch CreateBuilderTerminator() =>
            CreateBuilderTerminator(ImmutableArray<ValueReference>.Empty);

        /// <summary>
        /// Creates a temporary builder terminator.
        /// </summary>
        /// <param name="targets">All branch targets.</param>
        /// <returns>The created terminator.</returns>
        public Branch CreateBuilderTerminator(ImmutableArray<ValueReference> targets) =>
            CreateTerminator(new BuilderTerminator(
                Context,
                BasicBlock,
                targets)) as Branch;
    }
}
